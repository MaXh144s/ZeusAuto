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
//
//  Correções de robustez (bugs estruturais):
//
//  FIX-1: Beep movido para antes de Thread.Start() — elimina falsa sensação
//          de ativação quando o loop ainda não começou a rodar.
//
//  FIX-2: Debounce de InputUp após StartMacro — impede que o InputUp do
//          segundo clique (double-click rápido) cancele o loop antes da
//          primeira iteração. Guard de LoopStartDebounceMs (padrão 80 ms).
//
//  FIX-3: Volatile.Write(ref _loopCancelled, false) — barreira de memória
//          correta ao resetar o flag; evita que thread veja valor residual
//          (relevante em arquiteturas ARM/x86 com reordenação de memória).
//
//  FIX-4: Join(LoopJoinTimeoutMs) em StartMacro — garante que thread anterior
//          terminou antes de criar nova; elimina estado inconsistente em
//          stop/start rápido.
//
//  FIX-5: PlayBeep em thread dedicada — isola Beep() bloqueante do
//          ThreadPool compartilhado, evitando dessincronização do feedback
//          sonoro sob carga.
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

    // FIX-2: janela de debounce após StartMacro durante a qual InputUp é ignorado.
    // 80 ms cobre o tempo de escalonamento da thread + hold mínimo intencional.
    // Ajuste se o seu double-click window for menor que este valor.
    private const int LoopStartDebounceMs = 80;

    // FIX-4: tempo máximo de espera pela thread anterior antes de desistir.
    private const int LoopJoinTimeoutMs = 80;

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
    private readonly object          _sync                 = new();
    private          MacroState      _state                = MacroState.Idle;
    private          DateTimeOffset? _firstClickReleasedAt;
    private          bool            _listening;
    private          bool            _disposed;

    // _bipOverride: quando false, suprime o beep independente do BeepEnabled da config.
    // Alterado pelo MainForm via SetBipOverride — é um override global temporário,
    // não uma alteração de configuração persistente.
    private volatile bool _bipOverride = true;

    // ── Loop de clique ────────────────────────────────────────────────────────
    private Thread?       _loopThread;
    // Não usar volatile aqui — Volatile.Write/Read são usados em todos os acessos,
    // que fornecem as mesmas barreiras de memória sem o aviso CS0420
    // ("reference to volatile field will not be treated as volatile").
    private bool _loopCancelled;

    // FIX-2: timestamp de quando StartMacro foi chamado, usado pelo debounce de InputUp.
    // Protegido por _sync — escrito apenas em StartMacro, lido apenas em HandleInputUp.
    private DateTimeOffset _loopRequestedAt;

    // FIX-6: sinaliza que o loop foi iniciado por HandleInputDown (double-click hold) e
    // deve parar no próximo InputUp, independente do debounce. Sem este flag, o Up do
    // segundo clique cai dentro da janela de 80 ms e é descartado pelo debounce do FIX-2,
    // fazendo o loop rodar indefinidamente até o próximo clique válido.
    // Protegido por _sync.
    private bool _stopOnNextUp;

    // ── Random por instância: sem contenção com Random.Shared ────────────────
    private readonly Random _rng = new();

    // ── Estado de aceleração CPS ──────────────────────────────────────────────
    // Protegido por _cpsAccelLock — acessado apenas fora do hot path do loop.
    private readonly object _cpsAccelLock    = new();
    private System.Threading.Timer? _cpsHoldTimer;    // timer de hold threshold
    private System.Threading.Timer? _cpsRepeatTimer;  // timer de repetição acelerada
    private int    _cpsRepeatDirection;   // +1 ou -1 da tecla atualmente pressionada
    private int    _cpsCurrentRepeatIntervalMs; // intervalo atual (diminui progressivamente)
    private bool   _cpsKeyHeld;           // true enquanto a tecla estiver pressionada

    // Acumulador de CPS em double — evita perda de precisão pelo arredondamento
    // de IntervalMs. Ex: 32.0 + 0.5 = 32.5, mesmo que round(1000/32.0)=31 ms
    // e round(1000/32.5)=31 ms também, o acumulador avança corretamente.
    // Protegido por _cpsAccelLock.
    // double.NaN = não inicializado (usa IntervalMs da config como base na 1ª chamada).
    private double _cpsAccumulated = double.NaN;

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

        _inputListener.InputDown           += OnInputDown;
        _inputListener.InputUp             += OnInputUp;
        _inputListener.StartHotkeyPressed  += OnStartHotkeyPressed;
        _inputListener.StopHotkeyPressed   += OnStopHotkeyPressed;
        _inputListener.CpsIncrementPressed += OnCpsIncrementPressed;
        _inputListener.CpsDecrementPressed += OnCpsDecrementPressed;
        _inputListener.CpsIncrementReleased += OnCpsIncrementReleased;
        _inputListener.CpsDecrementReleased += OnCpsDecrementReleased;

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

    public void LoadConfig(string configPath)   => ApplyConfig(_loader.Load(configPath));
    public void LoadConfig(MacroConfig config)  => ApplyConfig(config);
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
            // FIX-4: aguarda a thread anterior terminar antes de criar uma nova.
            // Sem este Join, um stop/start rápido pode encontrar a thread antiga
            // ainda viva, retornar sem criar nova thread, e deixar o estado em
            // Running com zero cliques sendo disparados.
            if (_loopThread is { IsAlive: true })
            {
                _loopThread.Join(LoopJoinTimeoutMs);
                if (_loopThread.IsAlive) return; // thread travada — não inicia novo loop
            }

            if (!_config.Enabled) { _state = MacroState.Idle; return; }

            _state = MacroState.Running;
            snapshot = _config;

            // FIX-3: Volatile.Write garante barreira de memória completa ao resetar
            // o flag. Sem isso, em arquiteturas ARM a thread nova pode ler o valor
            // residual true de uma execução anterior e sair imediatamente.
            Volatile.Write(ref _loopCancelled, false);

            // FIX-2: registra o momento exato do start para o debounce de InputUp.
            _loopRequestedAt = DateTimeOffset.UtcNow;

            // FIX-1: beep emitido ANTES de Thread.Start(), enquanto ainda estamos
            // dentro do lock com _state = Running confirmado. Isso garante que o
            // feedback sonoro só acontece quando o estado já foi commitado, e não
            // depois de Start() onde um InputUp poderia cancelar o loop antes do
            // beep ser enfileirado.
            if (snapshot.BeepEnabled && _bipOverride)
                PlayBeep(snapshot.BeepHz);

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
    }

    public void StopMacro() => StopLoop();

    /// <summary>
    /// Override global de bip. Quando <c>false</c>, o beep é suprimido em todos
    /// os acionamentos, independente do <c>BeepEnabled</c> de cada macro.
    /// Chamado pelo MainForm ao receber o evento BipHotkeyPressed.
    /// </summary>
    public void SetBipOverride(bool enabled)
    {
        _bipOverride = enabled;
    }

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
                {
                    shouldStart    = true;
                    _stopOnNextUp  = true;  // FIX-6: o Up que encerra o hold já está a caminho
                }
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
            {
                // FIX-6: loop iniciado por double-click hold — o Up que chega agora
                // é o soltar do segundo clique, que é exatamente o sinal de parada.
                // Deve parar imediatamente, sem checar o debounce.
                if (_stopOnNextUp)
                {
                    _stopOnNextUp = false;
                    shouldStop    = true;
                }
                // FIX-2: debounce — ignora InputUp que chegue dentro de LoopStartDebounceMs
                // após o StartMacro. Isso evita que o InputUp do segundo clique de um
                // double-click rápido cancele o loop antes da primeira iteração ser executada,
                // que era o principal responsável pelo sintoma "bipa mas não clica".
                else
                {
                    bool withinDebounce =
                        (DateTimeOffset.UtcNow - _loopRequestedAt).TotalMilliseconds < LoopStartDebounceMs;

                    if (!withinDebounce)
                        shouldStop = true;
                }
            }
        }

        if (shouldStop) StopLoop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopListening();
        _inputListener.InputDown            -= OnInputDown;
        _inputListener.InputUp              -= OnInputUp;
        _inputListener.StartHotkeyPressed   -= OnStartHotkeyPressed;
        _inputListener.StopHotkeyPressed    -= OnStopHotkeyPressed;
        _inputListener.CpsIncrementPressed  -= OnCpsIncrementPressed;
        _inputListener.CpsDecrementPressed  -= OnCpsDecrementPressed;
        _inputListener.CpsIncrementReleased -= OnCpsIncrementReleased;
        _inputListener.CpsDecrementReleased -= OnCpsDecrementReleased;
        lock (_cpsAccelLock) { _cpsKeyHeld = false; StopCpsTimers(); }
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

        while (!Volatile.Read(ref _loopCancelled))
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
                if (sleepMs >= 5)
                    Thread.Sleep(sleepMs - 1);

                // Spin-wait final: preciso ao tick, sem overhead de kernel
                while (sw.ElapsedTicks < nextTick && !Volatile.Read(ref _loopCancelled))
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
        int    capturedHold   = holdMs;
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
        Volatile.Write(ref _loopCancelled, true);
        lock (_sync)
        {
            _state                = MacroState.Idle;
            _firstClickReleasedAt = null;
            _stopOnNextUp         = false;  // FIX-6: reseta flag para não vazar ao próximo ciclo
        }
        // Não faz Join aqui: o loop para sozinho na próxima verificação de _loopCancelled.
        // Isso evita deadlock se StopLoop for chamado da UI thread enquanto o loop
        // está aguardando no spin-wait. O Join necessário está em StartMacro (FIX-4).
    }

    private int CalculateDelay(MacroConfig cfg)
    {
        int interval = Math.Max(1, cfg.IntervalMs);
        if (!cfg.RandomizationEnabled) return interval;

        int maxOffset = Math.Max(0, cfg.RandomMax);
        int offset    = maxOffset > 0 ? _rng.Next(-maxOffset, maxOffset + 1) : 0;
        return Math.Max(1, interval + offset);
    }

    /// <summary>
    /// FIX-5: Beep em thread dedicada em vez do ThreadPool compartilhado.
    /// Beep() da WinAPI é bloqueante (dorme BeepDurationMs na thread chamante).
    /// No ThreadPool isso bloqueava uma thread do pool por 80 ms, causando
    /// dessincronização do feedback sonoro sob carga (pool ocupado com ReleaseButton).
    /// Thread dedicada isola completamente o beep de qualquer contenção externa.
    /// </summary>
    private static void PlayBeep(int hz)
    {
        uint freq = (uint)Math.Clamp(hz, 200, 1000);
        new Thread(() =>
        {
            try { Beep(freq, BeepDurationMs); } catch { /* hardware sem suporte a bip */ }
        }) { IsBackground = true, Name = "ZeusAuto.Beep" }.Start();
    }

    private void ApplyConfig(MacroConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Volatile.Write garante que a referência seja visível a todas as threads
        // imediatamente, sem lock. O loop lê _config no início de cada ciclo.
        Volatile.Write(ref _config, config);

        lock (_sync) { _firstClickReleasedAt = null; }

        // Reseta o acumulador de CPS para forçar releitura do IntervalMs da nova config
        lock (_cpsAccelLock) { _cpsAccumulated = double.NaN; }

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

    private void OnCpsIncrementPressed(object? _, EventArgs __) => BeginCpsPress(+1);
    private void OnCpsDecrementPressed(object? _, EventArgs __) => BeginCpsPress(-1);
    private void OnCpsIncrementReleased(object? _, EventArgs __) => EndCpsPress(+1);
    private void OnCpsDecrementReleased(object? _, EventArgs __) => EndCpsPress(-1);

    // ─────────────────────────────────────────────────────────────────────────
    //  Sistema de aceleração CPS
    //
    //  Comportamento:
    //    - Pressionar e soltar rápido → ajuste único de ±CpsStep CPS
    //    - Segurar ≥ CpsHoldThresholdMs → inicia repetição a CpsInitialRepeatIntervalMs
    //    - Cada repetição reduz o intervalo progressivamente até CpsMinimumRepeatIntervalMs
    //    - Soltar a tecla → para tudo e reseta o estado
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Chamado no primeiro KeyDown do hotkey CPS (latch garante que só dispara uma vez por press).
    /// Realiza o ajuste imediato e agenda o timer de hold.
    /// </summary>
    private void BeginCpsPress(int direction)
    {
        lock (_cpsAccelLock)
        {
            // Cancela estado anterior (segurança contra eventos fora de ordem)
            StopCpsTimers();

            _cpsKeyHeld        = true;
            _cpsRepeatDirection = direction;

            MacroConfig cfg = Volatile.Read(ref _config);
            _cpsCurrentRepeatIntervalMs = cfg.CpsInitialRepeatIntervalMs > 0
                ? cfg.CpsInitialRepeatIntervalMs : 200;
        }

        // Ajuste imediato ao pressionar
        AdjustCps(direction);

        // Agenda o timer de hold threshold
        MacroConfig current = Volatile.Read(ref _config);
        int holdMs = current.CpsHoldThresholdMs > 0 ? current.CpsHoldThresholdMs : 500;

        lock (_cpsAccelLock)
        {
            if (!_cpsKeyHeld) return; // tecla solta antes do timer iniciar
            _cpsHoldTimer = new System.Threading.Timer(_ => OnCpsHoldThresholdReached(), null, holdMs, Timeout.Infinite);
        }
    }

    /// <summary>Chamado quando o hold threshold expira — inicia a repetição acelerada.</summary>
    private void OnCpsHoldThresholdReached()
    {
        int intervalMs;
        int direction;

        lock (_cpsAccelLock)
        {
            if (!_cpsKeyHeld) return;
            intervalMs = _cpsCurrentRepeatIntervalMs;
            direction  = _cpsRepeatDirection;

            // Agenda a primeira repetição
            _cpsRepeatTimer = new System.Threading.Timer(_ => OnCpsRepeatTick(), null, intervalMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Tick de repetição acelerada.
    /// Realiza um ajuste, diminui o intervalo progressivamente e reagenda.
    /// </summary>
    private void OnCpsRepeatTick()
    {
        int nextIntervalMs;
        int direction;

        lock (_cpsAccelLock)
        {
            if (!_cpsKeyHeld) return;
            direction = _cpsRepeatDirection;

            MacroConfig cfg = Volatile.Read(ref _config);
            int minInterval = cfg.CpsMinimumRepeatIntervalMs > 0 ? cfg.CpsMinimumRepeatIntervalMs : 25;

            // Redução progressiva: diminui 20% por tick, convergindo para o mínimo
            int reduced = (int)(_cpsCurrentRepeatIntervalMs * 0.80);
            _cpsCurrentRepeatIntervalMs = Math.Max(minInterval, reduced);
            nextIntervalMs = _cpsCurrentRepeatIntervalMs;

            // Reagenda antes de ajustar (se AdjustCps demorar, o próximo tick já está agendado)
            _cpsRepeatTimer?.Dispose();
            _cpsRepeatTimer = new System.Threading.Timer(_ => OnCpsRepeatTick(), null, nextIntervalMs, Timeout.Infinite);
        }

        AdjustCps(direction);
    }

    /// <summary>Chamado no KeyUp de qualquer tecla do hotkey CPS — para tudo.</summary>
    private void EndCpsPress(int direction)
    {
        lock (_cpsAccelLock)
        {
            // Só reseta se a direção bate (evita que release de inc afete dec e vice-versa)
            if (_cpsRepeatDirection != direction && _cpsKeyHeld) return;
            _cpsKeyHeld = false;
            StopCpsTimers();
        }
    }

    /// <summary>Cancela ambos os timers de aceleração CPS. Deve ser chamado com _cpsAccelLock.</summary>
    private void StopCpsTimers()
    {
        _cpsHoldTimer?.Dispose();
        _cpsHoldTimer = null;
        _cpsRepeatTimer?.Dispose();
        _cpsRepeatTimer = null;
    }

    /// <summary>
    /// Ajusta o CPS em <c>direction * CpsStep</c> passos, clampado entre 1–50 CPS.
    /// Usa um acumulador em double para evitar a perda de precisão causada pelo
    /// arredondamento de IntervalMs (ex: 32.0 + 0.5 → 32.5, mesmo que ambos
    /// arredondem para o mesmo IntervalMs de 31 ms).
    /// Recalcula IntervalMs, troca a config atomicamente e dispara <see cref="CpsChanged"/>.
    /// </summary>
    /// <param name="direction">+1 para incremento, -1 para decremento.</param>
    private void AdjustCps(int direction)
    {
        MacroConfig current = Volatile.Read(ref _config);
        double step = current.CpsStep > 0 ? current.CpsStep : 0.5;

        // Inicializa o acumulador a partir do IntervalMs atual se ainda não foi usado.
        // A partir daí, os steps se acumulam no double sem arredondamento.
        double baseCps;
        lock (_cpsAccelLock)
        {
            if (double.IsNaN(_cpsAccumulated))
            {
                _cpsAccumulated = current.IntervalMs > 0
                    ? 1000.0 / current.IntervalMs
                    : 10.0;
            }
            _cpsAccumulated = Math.Clamp(_cpsAccumulated + direction * step, CpsMin, CpsMax);
            baseCps = _cpsAccumulated;
        }

        int newInterval = (int)Math.Round(1000.0 / baseCps);
        newInterval     = Math.Max(1, newInterval);

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
            CpsHoldThresholdMs        = current.CpsHoldThresholdMs,
            CpsInitialRepeatIntervalMs = current.CpsInitialRepeatIntervalMs,
            CpsMinimumRepeatIntervalMs = current.CpsMinimumRepeatIntervalMs,
            ExtraOptions         = current.ExtraOptions
        };

        // Troca atomicamente — o loop pega o novo IntervalMs no próximo ciclo
        Volatile.Write(ref _config, updated);

        // Notifica o EngineSlot para persistir e exibir o toast
        CpsChanged?.Invoke(this, new CpsChangedEventArgs(baseCps, newInterval, direction * step, step));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}