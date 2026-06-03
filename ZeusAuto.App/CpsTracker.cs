using ZeusAuto.Engine.Core;
using ZeusAuto.Engine.Core.Interfaces;

namespace ZeusAuto.App;

// ─────────────────────────────────────────────────────────────────────────────
//  CountingMouseSimulator
//
//  Envolve o MouseSimulator real e dispara o evento Clicked a cada Click().
//  É injetado no MacroEngine via construtor para interceptar cada clique
//  efetivamente emitido pelo loop RunMacroAsync — sem alterar a engine.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class CountingMouseSimulator : IMouseSimulator
{
    private readonly MouseSimulator _inner = new();

    /// <summary>
    /// Disparado imediatamente após cada Click() real emitido pela engine.
    /// Usado pelo CpsTracker para registrar o timestamp exato do clique.
    /// </summary>
    public event EventHandler? Clicked;

    public void Click(string buttonName)    { _inner.Click(buttonName);  Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickLeft()                 { _inner.ClickLeft();        Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickRight()                { _inner.ClickRight();       Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickMiddle()               { _inner.ClickMiddle();      Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickX1()                   { _inner.ClickX1();          Clicked?.Invoke(this, EventArgs.Empty); }
    public void ClickX2()                   { _inner.ClickX2();          Clicked?.Invoke(this, EventArgs.Empty); }

    // Press/Release não são cliques completos — não contam no CPS
    public void PressLeft()    => _inner.PressLeft();
    public void ReleaseLeft()  => _inner.ReleaseLeft();
    public void PressRight()   => _inner.PressRight();
    public void ReleaseRight() => _inner.ReleaseRight();
    public void PressMiddle()  => _inner.PressMiddle();
    public void ReleaseMiddle()=> _inner.ReleaseMiddle();
    public void PressX1()      => _inner.PressX1();
    public void ReleaseX1()    => _inner.ReleaseX1();
    public void PressX2()      => _inner.PressX2();
    public void ReleaseX2()    => _inner.ReleaseX2();
}

// ─────────────────────────────────────────────────────────────────────────────
//  CpsTracker
//
//  Janela deslizante de 1 segundo: a cada clique registrado, guarda o
//  Environment.TickCount64 numa Queue. RealCps descarta os ticks com mais
//  de 1000 ms e retorna o número restante → CPS exato de saída da engine.
//
//  Quando a engine para de emitir cliques a fila esvazia naturalmente
//  em até 1 segundo → RealCps retorna 0.0.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class CpsTracker
{
    // Timestamps em ticks de Stopwatch dos últimos N cliques registrados.
    // Mantemos até MaxSamples para calcular o intervalo médio entre cliques.
    private readonly Queue<long> _timestamps = new();
    private readonly object      _lock       = new();

    // Janela máxima de amostras: média dos últimos 8 intervalos é suficiente
    // para estabilidade sem introduzir inércia perceptível.
    private const int  MaxSamples = 9;    // N cliques → N-1 intervalos
    private const long StaleMs    = 1200; // descarta amostras com mais de 1.2 s

    private static readonly double TicksPerMs =
        System.Diagnostics.Stopwatch.Frequency / 1000.0;

    /// <summary>
    /// Registra o timestamp exato (Stopwatch) de um clique.
    /// </summary>
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
    /// Sem inércia de arranque: o valor é preciso já a partir do 2.º clique.
    /// </summary>
    public double RealCps
    {
        get
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_lock)
            {
                PruneStale(now);

                // Precisamos de pelo menos 2 timestamps para ter 1 intervalo
                if (_timestamps.Count < 2)
                    return 0.0;

                long[] arr   = _timestamps.ToArray();
                int    pairs = arr.Length - 1;

                // Intervalo médio em ms entre cliques consecutivos
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

    /// <summary>Estado atual da engine (Idle / WaitingSecondClick / Running).</summary>
    public MacroState State => _engine.State;

    /// <summary>Intervalo em ms lido da configuração ativa da engine.</summary>
    public int IntervalMs => _engine.CurrentConfig.IntervalMs;

    /// <summary>CPS exato medido na última janela de 1 segundo.</summary>
    public double RealCps => _tracker.RealCps;

    public EngineSlot(string macroKey, MacroConfig config)
    {
        MacroKey = macroKey;

        _sim = new CountingMouseSimulator();
        _sim.Clicked += (_, _) => _tracker.RegisterClick();

        _engine = new MacroEngine(mouseSimulator: _sim);
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