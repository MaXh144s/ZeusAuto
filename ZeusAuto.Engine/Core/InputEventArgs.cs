namespace ZeusAuto.Engine.Core;

public sealed class InputEventArgs : EventArgs
{
    public InputEventArgs(string inputName)
    {
        InputName = inputName;
    }

    public string InputName { get; }
}
