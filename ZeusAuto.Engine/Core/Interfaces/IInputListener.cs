namespace ZeusAuto.Engine.Core.Interfaces;

public interface IInputListener : IDisposable
{
    event EventHandler<InputEventArgs>? InputDown;

    event EventHandler<InputEventArgs>? InputUp;

    event EventHandler? StartHotkeyPressed;

    event EventHandler? StopHotkeyPressed;

    /// <summary>Disparado quando o hotkey de incremento de CPS é pressionado.</summary>
    event EventHandler? CpsIncrementPressed;

    /// <summary>Disparado quando o hotkey de decremento de CPS é pressionado.</summary>
    event EventHandler? CpsDecrementPressed;

    void StartListening();

    void StopListening();

    void UpdateConfig(MacroConfig config);
}
