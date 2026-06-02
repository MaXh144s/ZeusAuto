using ZeusAuto.Engine.Core.Interfaces;

namespace ZeusAuto.Engine.Core;

public sealed class MacroEngine : IDisposable
{
    private readonly object _sync = new();
    private readonly JsonConfigLoader _loader;
    private readonly IInputListener _inputListener;
    private readonly IMouseSimulator _mouseSimulator;
    private readonly ProfileManager? _profileManager;

    private MacroConfig _config = new();
    private MacroState _state = MacroState.Idle;
    private CancellationTokenSource? _macroCancellation;
    private Task? _macroTask;
    private DateTimeOffset? _firstClickReleasedAt;
    private bool _listening;
    private bool _disposed;

    public MacroEngine(
        IInputListener? inputListener = null,
        IMouseSimulator? mouseSimulator = null,
        JsonConfigLoader? loader = null,
        ProfileManager? profileManager = null)
    {
        _loader = loader ?? new JsonConfigLoader();
        _inputListener = inputListener ?? new InputListener();
        _mouseSimulator = mouseSimulator ?? new MouseSimulator();
        _profileManager = profileManager;

        _inputListener.InputDown += OnInputDown;
        _inputListener.InputUp += OnInputUp;
        _inputListener.StartHotkeyPressed += OnStartHotkeyPressed;
        _inputListener.StopHotkeyPressed += OnStopHotkeyPressed;

        if (_profileManager is not null)
        {
            _profileManager.ProfileChanged += OnProfileChanged;
        }
    }

    public MacroState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public MacroConfig CurrentConfig
    {
        get
        {
            lock (_sync)
            {
                return _config;
            }
        }
    }

    public void LoadConfig(string configPath)
    {
        MacroConfig config = _loader.Load(configPath);
        ApplyConfig(config);
    }

    public void LoadConfig(MacroConfig config)
    {
        ApplyConfig(config);
    }

    public void ReloadConfig(string configPath)
    {
        LoadConfig(configPath);
    }

    public void StartListening()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            if (_listening)
            {
                return;
            }

            _listening = true;
        }

        _inputListener.UpdateConfig(CurrentConfig);
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

        lock (_sync)
        {
            _listening = true;
        }
    }

    public void DisableMonitoring()
    {
        lock (_sync)
        {
            _listening = false;
            _state = MacroState.Idle;
            _firstClickReleasedAt = null;
        }

        StopMacro();
    }

    public void StartMacro()
    {
        ThrowIfDisposed();

        CancellationTokenSource? cts = null;
        lock (_sync)
        {
            if (_macroTask is { IsCompleted: false })
            {
                return;
            }

            if (!_config.Enabled)
            {
                _state = MacroState.Idle;
                return;
            }

            _state = MacroState.Running;
            cts = new CancellationTokenSource();
            _macroCancellation = cts;
            _macroTask = RunMacroAsync(cts.Token);
        }
    }

    public void StopMacro()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _macroCancellation;
            _macroCancellation = null;
            _state = MacroState.Idle;
            _firstClickReleasedAt = null;
        }

        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void HandleInputDown(string inputName)
    {
        if (!IsTrigger(inputName))
        {
            return;
        }

        bool shouldStart = false;
        lock (_sync)
        {
            if (!_listening || !_config.Enabled)
            {
                return;
            }

            if (!IsDoubleClickHoldMode(_config.ActivationMode))
            {
                return;
            }

            if (_state == MacroState.Idle)
            {
                _state = MacroState.WaitingSecondClick;
                _firstClickReleasedAt = null;
                return;
            }

            if (_state == MacroState.WaitingSecondClick && _firstClickReleasedAt.HasValue)
            {
                if (IsWithinDoubleClickWindow())
                {
                    shouldStart = true;
                }
                else
                {
                    _firstClickReleasedAt = null;
                }
            }
        }

        if (shouldStart)
        {
            StartMacro();
        }
    }

    public void HandleInputUp(string inputName)
    {
        if (!IsTrigger(inputName))
        {
            return;
        }

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
                shouldStop = true;
            }
        }

        if (shouldStop)
        {
            StopMacro();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopListening();
        _inputListener.InputDown -= OnInputDown;
        _inputListener.InputUp -= OnInputUp;
        _inputListener.StartHotkeyPressed -= OnStartHotkeyPressed;
        _inputListener.StopHotkeyPressed -= OnStopHotkeyPressed;
        _inputListener.Dispose();

        if (_profileManager is not null)
        {
            _profileManager.ProfileChanged -= OnProfileChanged;
        }

        _disposed = true;
    }

    private async Task RunMacroAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                MacroConfig snapshot = CurrentConfig;
                if (!snapshot.Enabled)
                {
                    StopMacro();
                    return;
                }

                _mouseSimulator.Click(snapshot.ClickButton ?? snapshot.TriggerButton ?? string.Empty);
                int delay = CalculateDelay(snapshot);
                await Task.Delay(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private int CalculateDelay(MacroConfig config)
    {
        int interval = Math.Max(1, config.IntervalMs);
        if (!config.RandomizationEnabled)
        {
            return interval;
        }

        int min = Math.Min(config.RandomMin, config.RandomMax);
        int max = Math.Max(config.RandomMin, config.RandomMax);
        int randomOffset = Random.Shared.Next(min, max + 1);
        return Math.Max(1, interval + Random.Shared.Next(-randomOffset, randomOffset + 1));
    }

    private void ApplyConfig(MacroConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        bool shouldStop;
        lock (_sync)
        {
            _config = config;
            _firstClickReleasedAt = null;
            shouldStop = !_config.Enabled;
        }

        _inputListener.UpdateConfig(config);

        if (shouldStop)
        {
            StopMacro();
        }
    }

    private bool IsTrigger(string inputName)
    {
        MacroConfig config = CurrentConfig;
        return IsSameInput(inputName, config.TriggerButton);
    }

    private bool IsWithinDoubleClickWindow()
    {
        MacroConfig config = CurrentConfig;
        if (!config.DoubleClickWindowMs.HasValue || !_firstClickReleasedAt.HasValue)
        {
            return true;
        }

        TimeSpan elapsed = DateTimeOffset.UtcNow - _firstClickReleasedAt.Value;
        return elapsed.TotalMilliseconds <= config.DoubleClickWindowMs.Value;
    }

    private static bool IsDoubleClickHoldMode(string? activationMode)
    {
        return string.Equals(activationMode, "DoubleClickHold", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameInput(string? left, string? right)
    {
        return string.Equals(NormalizeInputName(left), NormalizeInputName(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInputName(string? inputName)
    {
        return inputName?.Trim().ToUpperInvariant() switch
        {
            "MOUSELEFT" or "LEFT" or "TECLA ESQUERDA" => "MouseLeft",
            "MOUSERIGHT" or "RIGHT" or "TECLA DIREITA" => "MouseRight",
            "MOUSEMIDDLE" or "MIDDLE" or "TECLA SCROLL" => "MouseMiddle",
            "MOUSEX1" or "X1" or "XBUTTON1" or "TECLA XBUTTON4" => "MouseX1",
            "MOUSEX2" or "X2" or "XBUTTON2" or "TECLA XBUTTON5" => "MouseX2",
            var value => value ?? string.Empty
        };
    }

    private void OnInputDown(object? sender, InputEventArgs e)
    {
        HandleInputDown(e.InputName);
    }

    private void OnInputUp(object? sender, InputEventArgs e)
    {
        HandleInputUp(e.InputName);
    }

    private void OnStartHotkeyPressed(object? sender, EventArgs e)
    {
        EnableMonitoring();
    }

    private void OnStopHotkeyPressed(object? sender, EventArgs e)
    {
        DisableMonitoring();
    }

    private void OnProfileChanged(object? sender, ProfileChangedEventArgs e)
    {
        ApplyConfig(e.Config);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
