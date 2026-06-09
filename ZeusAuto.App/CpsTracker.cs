using ZeusAuto.Engine.Core;
using ZeusAuto.Engine.Core.Interfaces;

namespace ZeusAuto.App;

// ─────────────────────────────────────────────────────────────────────────────
//  CountingMouseSimulator
//
//  Envolve o MouseSimulator real e dispara o evento Clicked a cada clique
//  efetivamente emitido pela engine. É injetado no MacroEngine via construtor
//  para interceptar cada clique sem alterar a engine.
//
//  IMPORTANTE: com a nova arquitetura do MacroEngine (DispatchClick),
//  os cliques são emitidos via PressButton + ReleaseButton, nunca via Click().
//  Por isso o Clicked é disparado nos métodos Press*, que marcam o início
//  real de cada clique — é o mesmo instante que o loop registraria como tick.
//  O Click() legado (ClickHoldMs = 0) também dispara para manter compatibilidade.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class CountingMouseSimulator : IMouseSimulator
{
    private readonly MouseSimulator _inner = new();

    /// <summary>
    /// Disparado no início de cada clique real emitido pela engine.
    /// Usado pelo CpsTracker para registrar o timestamp exato do clique.
    /// </summary>
    public event EventHandler? Clicked;

    // ── Click() legado (ClickHoldMs = 0) ─────────────────────────────────────
    public void Click(string buttonName) { _inner.Click(buttonName);  Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickLeft()              { _inner.ClickLeft();        Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickRight()             { _inner.ClickRight();       Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickMiddle()            { _inner.ClickMiddle();      Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickX1()                { _inner.ClickX1();          Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickX2()                { _inner.ClickX2();          Clicked?.Invoke(this, EventArgs.Empty); }

    // ── Press* (caminho principal com DispatchClick) ──────────────────────────
    // O Clicked é disparado aqui porque o MacroEngine chama PressButton no loop
    // principal — esse é o instante exato do clique para fins de CPS.
    public void PressLeft()    { _inner.PressLeft();    Clicked?.Invoke(this, EventArgs.Empty); }
    public void PressRight()   { _inner.PressRight();   Clicked?.Invoke(this, EventArgs.Empty); }
    public void PressMiddle()  { _inner.PressMiddle();  Clicked?.Invoke(this, EventArgs.Empty); }
    public void PressX1()      { _inner.PressX1();      Clicked?.Invoke(this, EventArgs.Empty); }
    public void PressX2()      { _inner.PressX2();      Clicked?.Invoke(this, EventArgs.Empty); }

    // Release não conta como clique — apenas encerra o hold
    public void ReleaseLeft()   => _inner.ReleaseLeft();
    public void ReleaseRight()  => _inner.ReleaseRight();
    public void ReleaseMiddle() => _inner.ReleaseMiddle();
    public void ReleaseX1()     => _inner.ReleaseX1();
    public void ReleaseX2()     => _inner.ReleaseX2();
}

// ─────────────────────────────────────────────────────────────────────────────
//  CpsTracker
//
//  Janela deslizante de 1 segundo: a cada clique registrado, guarda o
//  timestamp de Stopwatch numa Queue. RealCps descarta amostras velhas
//  e retorna o CPS calculado pelo intervalo médio entre os últimos cliques.
//
//  Quando a engine para, a fila esvazia naturalmente em até ~1.2 s
//  e RealCps retorna 0.0.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class CpsTracker
{
    private readonly Queue<long> _timestamps = new();
    private readonly object      _lock       = new();

    private const int  MaxSamples = 9;    // N cliques → N-1 intervalos para média
    private const long StaleMs    = 1200; // descarta amostras com mais de 1.2 s

    private static readonly double TicksPerMs =
        System.Diagnostics.Stopwatch.Frequency / 1000.0;

    /// <summary>Registra o timestamp exato (Stopwatch) de um clique.</summary>
    public void RegisterClick()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (_lock)
        {
            _timestamps.Enqueue(now);
            while (_timestamps.Count > MaxSamples)
                _timestamps.Dequeue();
        }
    }

    /// <summary>
    /// CPS real calculado pelo intervalo médio entre os últimos cliques.
    /// Retorna 0.0 se não houver cliques recentes ou apenas 1 amostra.
    /// </summary>
    public double RealCps
    {
        get
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_lock)
            {
                PruneStale(now);

                if (_timestamps.Count < 2)
                    return 0.0;

                long[] arr   = _timestamps.ToArray();
                int    pairs = arr.Length - 1;

                double totalMs = (arr[pairs] - arr[0]) / TicksPerMs;
                double avgMs   = totalMs / pairs;

                return avgMs > 10 ? 1000.0 / avgMs : 0.0;
            }
        }
    }

    private void PruneStale(long now)
    {
        long cutoffTicks = now - (long)(StaleMs * TicksPerMs);
        while (_timestamps.Count > 0 && _timestamps.Peek() < cutoffTicks)
            _timestamps.Dequeue();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  EngineSlot
//
//  Agrupa MacroEngine + CountingMouseSimulator + CpsTracker para um macro.
//  O MainForm cria um EngineSlot por macro ativo e os passa ao CpsOverlayForm.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class EngineSlot : IDisposable
{
    private readonly MacroEngine            _engine;
    private readonly CountingMouseSimulator _sim;
    private readonly CpsTracker             _tracker = new();
    private          bool                   _disposed;

    public string MacroKey { get; }

    public MacroState State         => _engine.State;
    public int        IntervalMs    => _engine.CurrentConfig.IntervalMs;
    public int? DoubleClickWindowMs => _engine.CurrentConfig.DoubleClickWindowMs;
    public double RealCps           => _tracker.RealCps;

    /// <summary>CPS configurado atual (derivado do IntervalMs da config).</summary>
    public double ConfigCps =>
        _engine.CurrentConfig.IntervalMs > 0
            ? 1000.0 / _engine.CurrentConfig.IntervalMs
            : 0.0;

    /// <summary>
    /// Aplica nova config à engine SEM reiniciá-la nem destruí-la.
    /// Seguro chamar enquanto a engine está em MacroState.Running.
    /// Usado pelo MainForm em profile:update para não interromper uma ativação em andamento.
    /// </summary>
    public void LoadConfig(MacroConfig config) => _engine.LoadConfig(config);

    /// <summary>
    /// Habilita o monitoramento de input desta engine.
    /// Chamado pelo MainForm ao despausar todos os macros.
    /// </summary>
    public void EnableMonitoring() => _engine.EnableMonitoring();

    /// <summary>
    /// Desabilita o monitoramento de input desta engine e para o loop de cliques.
    /// Chamado pelo MainForm ao pausar todos os macros.
    /// </summary>
    public void DisableMonitoring() => _engine.DisableMonitoring();

    /// <summary>
    /// Override global de bip. Quando <c>false</c>, suprime o beep de todos os acionamentos.
    /// Chamado pelo MainForm ao receber o evento BipHotkeyPressed.
    /// </summary>
    public void SetBipOverride(bool enabled) => _engine.SetBipOverride(enabled);

    /// <summary>
    /// Espelha o evento CpsChanged da engine — disparado a cada pressão de
    /// atalho de ajuste de CPS. Usado pelo MainForm para exibir o toast.
    /// </summary>
    public event EventHandler<ZeusAuto.Engine.Core.CpsChangedEventArgs>? CpsChanged;

    public EngineSlot(string macroKey, MacroConfig config, ProfileManager? profileManager = null)
    {
        MacroKey = macroKey;

        _sim = new CountingMouseSimulator();
        _sim.Clicked += (_, _) => _tracker.RegisterClick();

        _engine = new MacroEngine(mouseSimulator: _sim);

        // Repassa CpsChanged para o exterior, passando o macroKey como sender.
        // Isso permite ao MainForm identificar qual slot disparou o evento sem
        // precisar de acesso direto ao _engine privado.
        _engine.CpsChanged += (_, args) => CpsChanged?.Invoke(macroKey, args);

        // Quando um hotkey de CPS ajusta o intervalo, persiste imediatamente no JSON
        if (profileManager is not null)
        {
            _engine.CpsChanged += (_, _) =>
            {
                try { profileManager.SaveConfig(_engine.CurrentConfig); }
                catch { /* ignora falha de I/O — o ajuste em memória já foi aplicado */ }
            };
        }

        _engine.LoadConfig(config);
        _engine.StartListening();
        _engine.EnableMonitoring();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine.Dispose();
        _disposed = true;
    }
}
