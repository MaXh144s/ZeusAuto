namespace ZeusAuto.Engine.Core.Interfaces;

public interface IInputListener : IDisposable
{
    event EventHandler<InputEventArgs>? InputDown;

    event EventHandler<InputEventArgs>? InputUp;

    event EventHandler? StartHotkeyPressed;

    event EventHandler? StopHotkeyPressed;

    void StartListening();

    void StopListening();

    void UpdateConfig(MacroConfig config);
}
