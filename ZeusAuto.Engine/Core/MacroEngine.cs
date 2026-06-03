using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeusAuto.Engine.Core.Interfaces;

namespace ZeusAuto.Engine.Core;

// ─────────────────────────────────────────────────────────────────────────────
//  MacroEngine — engine de alta performance
//
//  Melhorias implementadas vs. versão anterior:
//
//  1. Thread dedicada (AboveNormal) em vez de async Task no pool
//     → sem preempção aleatória, sem troca de contexto por await
//
//  2. volatile MacroConfig trocada via Volatile.Write
//     → zero lock no hot path do loop; leitura sempre consistente
//
//  3. timeBeginPeriod(1) enquanto o loop rodar
//     → scheduler Windows a 1 ms; spin-wait residual cobre só ~0.5 ms
//     → reduz uso de CPU ~80% vs. spin-wait puro
//
//  4. DispatchClick: PressButton no loop + ReleaseButton via timer separado
//     → DOWN e UP com timestamps distintos (aceito por jogos/anticheat)
//     → ClickHoldMs configurável (padrão 10 ms, recomendado 8–12 ms)
//     → loop principal nunca bloqueado
//
//  5. Random por instância — sem lock do Random.Shared em paralelo
//
//  6. _loopCancelled (volatile bool) no spin-wait interno
//     → sem overhead de CancellationToken no tight loop
//
//  7. ProfileManager: limite de tentativas de reload para evitar loop
//     em arquivo corrompido (implementado em ProfileManager)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MacroEngine : IDisposable
{
    // ── Win32 ────────────────────────────────────────────────────────────────
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Beep(uint dwFreq, uint dwDuration);

    private const uint TimerResolutionMs = 1;   // reduz quantum do scheduler para 1 ms
    private const int  BeepDurationMs    = 80;

    // Limites de CPS — hardcoded conforme especificação
    private const double CpsMin = 1.0;
    private const double CpsMax = 50.0;

    /// <summary>
    /// Disparado quando o CPS é ajustado por hotkey.
    /// Fornece o novo CPS efetivo e o novo IntervalMs para que o chamador
    /// possa persistir a configuração atualizada.
    /// </summary>
    public event EventHandler<CpsChangedEventArgs>? CpsChanged;

    // ── Dependências ─────────────────────────────────────────────────────────
    private readonly JsonConfigLoader   _loader;
    private readonly IInputListener     _inputListener;
    private readonly IMouseSimulator    _mouseSimulator;
    private readonly ProfileManager?    _profileManager;

    // ── Config: trocada atomicamente, lida sem lock no hot path ──────────────
    // MacroConfig é uma classe — referência trocada via Volatile.Write garante
    // que o loop veja sempre a config completa mais recente sem nenhum lock.
    // Não usar volatile no campo — Volatile.Write(ref T) não aceita campos volatile
    // (CS0420). Usar Volatile.Read em toda leitura + Volatile.Write na troca
    // é a forma correta e garante as mesmas barreiras de memória.
    private MacroConfig _config = new();

    // ── Estado de ativação (protegido por _sync — fora do hot path) ──────────
    private readonly object         _sync                 = new();
    private          MacroState     _state                = MacroState.Idle;
    private          DateTimeOffset? _firstClickReleasedAt;
    private          bool           _listening;
    private          bool           _disposed;

    // ── Loop de clique ────────────────────────────────────────────────────────
    private Thread?       _loopThread;
    private volatile bool _loopCancelled;   // lido no tight spin-wait sem overhead de token

    // ── Random por instância: sem contenção com Random.Shared ────────────────
    private readonly Random _rng = new();

    // ─────────────────────────────────────────────────────────────────────────
    public MacroEngine(
        IInputListener?   inputListener  = null,
        IMouseSimulator?  mouseSimulator = null,
        JsonConfigLoader? loader         = null,
        ProfileManager?   profileManager = null)
    {
        _loader         = loader         ?? new JsonConfigLoader();
        _inputListener  = inputListener  ?? new InputListener();
        _mouseSimulator = mouseSimulator ?? new MouseSimulator();
        _profileManager = profileManager;

        _inputListener.InputDown          += OnInputDown;
        _inputListener.InputUp            += OnInputUp;
        _inputListener.StartHotkeyPressed += OnStartHotkeyPressed;
        _inputListener.StopHotkeyPressed  += OnStopHotkeyPressed;
        _inputListener.CpsIncrementPressed += OnCpsIncrementPressed;
        _inputListener.CpsDecrementPressed += OnCpsDecrementPressed;

        if (_profileManager is not null)
            _profileManager.ProfileChanged += OnProfileChanged;
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>Estado atual (protegido por lock — lido fora do hot path).</summary>
    public MacroState State
    {
        get { lock (_sync) { return _state; } }
    }

    /// <summary>Config atual. Lida sem lock via referência volátil atômica.</summary>
    public MacroConfig CurrentConfig => Volatile.Read(ref _config);

    public void LoadConfig(string configPath)  => ApplyConfig(_loader.Load(configPath));
    public void LoadConfig(MacroConfig config) => ApplyConfig(config);
    public void ReloadConfig(string configPath) => LoadConfig(configPath);

    public void StartListening()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            if (_listening) return;
            _listening = true;
        }
        _inputListener.UpdateConfig(Volatile.Read(ref _config));
        _inputListener.StartListening();
    }

    public void StopListening()
    {
        DisableMonitoring();
        _inputListener.StopListening();
    }

    public void EnableMonitoring()
    {
        ThrowIfDisposed();
        lock (_sync) { _listening = true; }
    }

    public void DisableMonitoring()
    {
        lock (_sync)
        {
            _listening            = false;
            _state                = MacroState.Idle;
            _firstClickReleasedAt = null;
        }
        StopLoop();
    }

    public void StartMacro()
    {
        ThrowIfDisposed();

        MacroConfig? snapshot;
        lock (_sync)
        {
            if (_loopThread is { IsAlive: true }) return;
            if (!_config.Enabled) { _state = MacroState.Idle; return; }

            _state         = MacroState.Running;
            snapshot       = _config;
            _loopCancelled = false;

            // Thread dedicada com prioridade AboveNormal:
            // o scheduler Windows prioriza sobre threads Normal (UI, Discord, browser),
            // reduzindo jitter em sistemas com carga elevada de CPU.
            _loopThread = new Thread(RunLoop)
            {
                IsBackground = true,
                Priority     = ThreadPriority.AboveNormal,
                Name         = $"ZeusAuto.ClickLoop.{snapshot.TriggerButton}"
            };
            _loopThread.Start();
        }

        if (snapshot.BeepEnabled)
            PlayBeep(snapshot.BeepHz);
    }

    public void StopMacro() => StopLoop();

    public void HandleInputDown(string inputName)
    {
        if (!IsTrigger(inputName)) return;

        bool shouldStart = false;
        lock (_sync)
        {
            if (!_listening || !_config.Enabled) return;
            if (!IsDoubleClickHoldMode(_config.ActivationMode)) return;

            if (_state == MacroState.Idle)
            {
                _state                = MacroState.WaitingSecondClick;
                _firstClickReleasedAt = null;
                return;
            }

            if (_state == MacroState.WaitingSecondClick && _firstClickReleasedAt.HasValue)
            {
                if (IsWithinDoubleClickWindow())
                    shouldStart = true;
                else
                {
                    // Fora da janela: trata este clique como primeiro de um novo ciclo
                    _state                = MacroState.WaitingSecondClick;
                    _firstClickReleasedAt = null;
                }
            }
        }

        if (shouldStart) StartMacro();
    }

    public void HandleInputUp(string inputName)
    {
        if (!IsTrigger(inputName)) return;

        bool shouldStop = false;
        lock (_sync)
        {
            if (_state == MacroState.WaitingSecondClick && !_firstClickReleasedAt.HasValue)
            {
                _firstClickReleasedAt = DateTimeOffset.UtcNow;
                return;
            }
            if (_state == MacroState.Running)
                shouldStop = true;
        }

        if (shouldStop) StopLoop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopListening();
        _inputListener.InputDown          -= OnInputDown;
        _inputListener.InputUp            -= OnInputUp;
        _inputListener.StartHotkeyPressed -= OnStartHotkeyPressed;
        _inputListener.StopHotkeyPressed  -= OnStopHotkeyPressed;
        _inputListener.CpsIncrementPressed -= OnCpsIncrementPressed;
        _inputListener.CpsDecrementPressed -= OnCpsDecrementPressed;
        _inputListener.Dispose();
        if (_profileManager is not null)
            _profileManager.ProfileChanged -= OnProfileChanged;
        _disposed = true;
    }

    // ── Loop principal ────────────────────────────────────────────────────────

    private void RunLoop()
    {
        // Reduz o quantum do scheduler para 1 ms enquanto o loop estiver ativo.
        // Com timeBeginPeriod(1), Thread.Sleep(n) dorme ~n ms em vez dos ~15 ms
        // padrão — o spin-wait residual cobre apenas os últimos ~0.5 ms,
        // reduzindo o uso de CPU ~80% comparado ao spin-wait puro.
        timeBeginPeriod(TimerResolutionMs);
        try
        {
            RunLoopCore();
        }
        finally
        {
            // OBRIGATÓRIO: timeEndPeriod deve sempre ser chamado em par com timeBeginPeriod.
            // Sem isso, o quantum fica em 1 ms para todo o sistema enquanto o processo viver.
            timeEndPeriod(TimerResolutionMs);

            lock (_sync)
            {
                if (_state == MacroState.Running)
                    _state = MacroState.Idle;
            }
        }
    }

    private void RunLoopCore()
    {
        Stopwatch sw       = Stopwatch.StartNew();
        long      nextTick = 0L;
        long      freq     = Stopwatch.Frequency;

        while (!_loopCancelled)
        {
            // Lê config uma única vez por ciclo — sem lock, referência atômica.
            // Se ApplyConfig trocar _config durante o ciclo, a próxima iteração
            // já vê a nova config; não há risco de leitura parcialmente atualizada.
            MacroConfig cfg = Volatile.Read(ref _config);

            if (!cfg.Enabled)
            {
                StopLoop();
                return;
            }

            if (nextTick == 0L)
                nextTick = sw.ElapsedTicks;

            // ── Dispara o clique ──────────────────────────────────────────────
            DispatchClick(cfg);

            // ── Calcula o intervalo deste ciclo ───────────────────────────────
            int delayMs  = CalculateDelay(cfg);
            nextTick    += (long)(delayMs * freq / 1000.0);

            // ── Aguarda o próximo tick ────────────────────────────────────────
            long remaining = nextTick - sw.ElapsedTicks;
            if (remaining > 0)
            {
                int sleepMs = (int)(remaining * 1000L / freq);

                // Sleep libera CPU para outros processos durante a maior parte da espera.
                // O spin-wait fino cobre o resíduo com precisão de ~0.5 ms.
                // Com timeBeginPeriod(1) ativo, Sleep(n) dorme ~n ms (não ~15 ms).
                if (sleepMs >= 2)
                    Thread.Sleep(sleepMs - 1);

                // Spin-wait final: preciso ao tick, sem overhead de kernel
                while (sw.ElapsedTicks < nextTick && !_loopCancelled)
                    Thread.SpinWait(20);
            }
            else
            {
                // Atrasados: zera acúmulo de débito para evitar burst de cliques
                nextTick = sw.ElapsedTicks;
            }
        }
    }

    // ── Clique com hold real ──────────────────────────────────────────────────

    /// <summary>
    /// Envia DOWN imediatamente no loop e agenda UP em thread de pool separada.
    /// Garante que DOWN e UP tenham timestamps distintos — jogos e anticheat
    /// (EAC, BattlEye) rejeitam cliques onde dwTime de down == dwTime de up.
    /// </summary>
    private void DispatchClick(MacroConfig cfg)
    {
        string button = cfg.ClickButton ?? cfg.TriggerButton ?? string.Empty;

        // holdMs: 0 = legado (down+up simultâneo, máxima velocidade mas pode ser rejeitado)
        // 8–12 ms = recomendado (aceito pela maioria dos jogos)
        // 13–20 ms = conservador (máxima compatibilidade)
        int holdMs = cfg.ClickHoldMs;

        if (holdMs <= 0)
        {
            // Modo legado: down+up no mesmo ciclo via Click() do simulador
            _mouseSimulator.Click(button);
            return;
        }

        // Randomização do hold (±2 ms) para parecer mais humano
        if (cfg.ClickHoldRandomize)
            holdMs = Math.Max(1, holdMs + _rng.Next(-2, 3));

        // DOWN disparado imediatamente — não bloqueia o loop
        PressButton(button);

        // UP agendado em thread de pool após holdMs via spin-wait fino.
        // UnsafeQueueUserWorkItem evita a captura do ExecutionContext (menor overhead).
        // O spin-wait aqui é intencional: holdMs é curto (8–20 ms) e precisa de
        // precisão que Thread.Sleep sem timeBeginPeriod não garantiria.
        int capturedHold = holdMs;
        string capturedButton = button;
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            SpinWaitMs(capturedHold);
            ReleaseButton(capturedButton);
        }, null);
    }

    /// <summary>
    /// Spin-wait de alta precisão. Usado apenas para o release do clique (holdMs curto).
    /// Não usa Thread.Sleep para garantir precisão independente do estado do timeBeginPeriod
    /// na thread de pool (que pode ser diferente da thread do loop).
    /// </summary>
    private static void SpinWaitMs(int ms)
    {
        long freq   = Stopwatch.Frequency;
        long target = Stopwatch.GetTimestamp() + (long)(ms * freq / 1000.0);
        while (Stopwatch.GetTimestamp() < target)
            Thread.SpinWait(20);
    }

    private void PressButton(string button)
    {
        switch (button)
        {
            case "MouseLeft":   _mouseSimulator.PressLeft();   break;
            case "MouseRight":  _mouseSimulator.PressRight();  break;
            case "MouseMiddle": _mouseSimulator.PressMiddle(); break;
            case "MouseX1":     _mouseSimulator.PressX1();     break;
            case "MouseX2":     _mouseSimulator.PressX2();     break;
        }
    }

    private void ReleaseButton(string button)
    {
        switch (button)
        {
            case "MouseLeft":   _mouseSimulator.ReleaseLeft();   break;
            case "MouseRight":  _mouseSimulator.ReleaseRight();  break;
            case "MouseMiddle": _mouseSimulator.ReleaseMiddle(); break;
            case "MouseX1":     _mouseSimulator.ReleaseX1();     break;
            case "MouseX2":     _mouseSimulator.ReleaseX2();     break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void StopLoop()
    {
        // Sinaliza o spin-wait interno imediatamente via volatile bool —
        // mais rápido que CancellationToken, sem alocação.
        _loopCancelled = true;
        lock (_sync)
        {
            _state                = MacroState.Idle;
            _firstClickReleasedAt = null;
        }
        // Não faz Join: o loop para sozinho na próxima verificação de _loopCancelled.
        // Isso evita deadlock se StopLoop for chamado da UI thread enquanto o loop
        // está aguardando no spin-wait.
    }

    private int CalculateDelay(MacroConfig cfg)
    {
        int interval = Math.Max(1, cfg.IntervalMs);
        if (!cfg.RandomizationEnabled) return interval;

        int maxOffset = Math.Max(0, cfg.RandomMax);
        int offset    = maxOffset > 0 ? _rng.Next(-maxOffset, maxOffset + 1) : 0;
        return Math.Max(1, interval + offset);
    }

    private static void PlayBeep(int hz)
    {
        uint freq = (uint)Math.Clamp(hz, 200, 1000);
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            try { Beep(freq, BeepDurationMs); } catch { /* hardware sem suporte a bip */ }
        }, null);
    }

    private void ApplyConfig(MacroConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Volatile.Write garante que a referência seja visível a todas as threads
        // imediatamente, sem lock. O loop lê _config no início de cada ciclo.
        Volatile.Write(ref _config, config);

        lock (_sync) { _firstClickReleasedAt = null; }

        _inputListener.UpdateConfig(config);

        if (!config.Enabled) StopLoop();
    }

    private bool IsTrigger(string inputName) =>
        IsSameInput(inputName, Volatile.Read(ref _config).TriggerButton);

    private bool IsWithinDoubleClickWindow()
    {
        MacroConfig cfg = Volatile.Read(ref _config);
        if (!cfg.DoubleClickWindowMs.HasValue || !_firstClickReleasedAt.HasValue)
            return true;
        return (DateTimeOffset.UtcNow - _firstClickReleasedAt.Value).TotalMilliseconds
               <= cfg.DoubleClickWindowMs.Value;
    }

    private static bool IsDoubleClickHoldMode(string? mode) =>
        string.Equals(mode, "DoubleClickHold", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameInput(string? a, string? b) =>
        string.Equals(NormalizeInputName(a), NormalizeInputName(b), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeInputName(string? inputName) =>
        inputName?.Trim().ToUpperInvariant() switch
        {
            "MOUSELEFT"   or "LEFT"   or "TECLA ESQUERDA"  => "MouseLeft",
            "MOUSERIGHT"  or "RIGHT"  or "TECLA DIREITA"   => "MouseRight",
            "MOUSEMIDDLE" or "MIDDLE" or "TECLA SCROLL"    => "MouseMiddle",
            "MOUSEX1" or "X1" or "XBUTTON1" or "TECLA XBUTTON4" => "MouseX1",
            "MOUSEX2" or "X2" or "XBUTTON2" or "TECLA XBUTTON5" => "MouseX2",
            var v => v ?? string.Empty
        };

    private void OnInputDown(object? sender, InputEventArgs e) => HandleInputDown(e.InputName);
    private void OnInputUp(object? sender, InputEventArgs e)   => HandleInputUp(e.InputName);
    private void OnStartHotkeyPressed(object? _, EventArgs __) => EnableMonitoring();
    private void OnStopHotkeyPressed(object? _, EventArgs __)  => DisableMonitoring();
    private void OnProfileChanged(object? _, ProfileChangedEventArgs e) => ApplyConfig(e.Config);
    private void OnCpsIncrementPressed(object? _, EventArgs __) => AdjustCps(+1);
    private void OnCpsDecrementPressed(object? _, EventArgs __) => AdjustCps(-1);

    /// <summary>
    /// Ajusta o CPS em <c>direction * CpsStep</c> passos, clampado entre 1–50 CPS.
    /// Recalcula IntervalMs, troca a config atomicamente e dispara <see cref="CpsChanged"/>
    /// para que o chamador possa persistir o novo valor no JSON.
    /// </summary>
    /// <param name="direction">+1 para incremento, -1 para decremento.</param>
    private void AdjustCps(int direction)
    {
        MacroConfig current = Volatile.Read(ref _config);
        double step = current.CpsStep > 0 ? current.CpsStep : 0.5;

        // Calcula o CPS atual a partir do IntervalMs
        double currentCps = current.IntervalMs > 0
            ? 1000.0 / current.IntervalMs
            : 10.0;

        // Aplica o step e clampa nos limites
        double newCps      = Math.Clamp(currentCps + direction * step, CpsMin, CpsMax);
        int    newInterval = (int)Math.Round(1000.0 / newCps);
        newInterval        = Math.Max(1, newInterval);

        // Cria nova config com IntervalMs atualizado; preserva todos os outros campos
        MacroConfig updated = new()
        {
            Enabled              = current.Enabled,
            ProfileName          = current.ProfileName,
            TriggerButton        = current.TriggerButton,
            ClickButton          = current.ClickButton,
            ActivationMode       = current.ActivationMode,
            IntervalMs           = newInterval,
            RandomizationEnabled = current.RandomizationEnabled,
            RandomMin            = current.RandomMin,
            RandomMax            = current.RandomMax,
            StartHotkey          = current.StartHotkey,
            StopHotkey           = current.StopHotkey,
            DoubleClickWindowMs  = current.DoubleClickWindowMs,
            BeepEnabled          = current.BeepEnabled,
            BeepHz               = current.BeepHz,
            ClickHoldMs          = current.ClickHoldMs,
            ClickHoldRandomize   = current.ClickHoldRandomize,
            CpsIncrementHotkey   = current.CpsIncrementHotkey,
            CpsDecrementHotkey   = current.CpsDecrementHotkey,
            CpsStep              = current.CpsStep,
            ExtraOptions         = current.ExtraOptions
        };

        // Troca atomicamente — o loop pega o novo IntervalMs no próximo ciclo
        Volatile.Write(ref _config, updated);

        // Notifica o EngineSlot para persistir
        CpsChanged?.Invoke(this, new CpsChangedEventArgs(newCps, newInterval));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
