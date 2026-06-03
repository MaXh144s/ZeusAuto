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
    private readonly Queue<long> _ticks = new();
    private readonly object      _lock  = new();
    private const long           WindowMs = 1000;

    /// <summary>
    /// Registra o timestamp de um clique. Deve ser chamado pelo evento
    /// Clicked do CountingMouseSimulator.
    /// </summary>
    public void RegisterClick()
    {
        lock (_lock)
        {
            _ticks.Enqueue(Environment.TickCount64);
            Prune();
        }
    }

    /// <summary>
    /// CPS real medido nos últimos 1000 ms.
    /// Retorna 0.0 quando nenhum clique ocorreu nessa janela.
    /// </summary>
    public double RealCps
    {
        get
        {
            lock (_lock)
            {
                Prune();
                return _ticks.Count;
            }
        }
    }

    private void Prune()
    {
        long cutoff = Environment.TickCount64 - WindowMs;
        while (_ticks.Count > 0 && _ticks.Peek() < cutoff)
            _ticks.Dequeue();
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