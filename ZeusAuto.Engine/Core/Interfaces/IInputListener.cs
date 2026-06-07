namespace ZeusAuto.Engine.Core.Interfaces;

public interface IInputListener : IDisposable
{
    event EventHandler<InputEventArgs>? InputDown;

    event EventHandler<InputEventArgs>? InputUp;

    event EventHandler? StartHotkeyPressed;

    event EventHandler? StopHotkeyPressed;

    /// <summary>Disparado quando o hotkey de incremento de CPS é pressionado.</summary>
    event EventHandler? CpsIncrementPressed;

    /// <summary>Disparado quando o hotkey de incremento de CPS é solto.</summary>
    event EventHandler? CpsIncrementReleased;

    /// <summary>Disparado quando o hotkey de decremento de CPS é pressionado.</summary>
    event EventHandler? CpsDecrementPressed;

    /// <summary>Disparado quando o hotkey de decremento de CPS é solto.</summary>
    event EventHandler? CpsDecrementReleased;

    /// <summary>Disparado quando o hotkey global de pausa é pressionado.</summary>
    event EventHandler? PauseHotkeyPressed;

    /// <summary>Disparado quando o hotkey global de overlay CPS é pressionado.</summary>
    event EventHandler? OverlayHotkeyPressed;

    /// <summary>Disparado quando o hotkey global de bip toggle é pressionado.</summary>
    event EventHandler? BipHotkeyPressed;

    /// <summary>Disparado quando o hotkey global de encerrar programa é pressionado.</summary>
    event EventHandler? EncerrarHotkeyPressed;

    void StartListening();

    void StopListening();

    void UpdateConfig(MacroConfig config);
}
