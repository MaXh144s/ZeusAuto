namespace ZeusAuto.Engine.Core;

public sealed class ProfileChangedEventArgs : EventArgs
{
    public ProfileChangedEventArgs(string profilePath, MacroConfig config)
    {
        ProfilePath = profilePath;
        Config = config;
    }

    public string ProfilePath { get; }

    public MacroConfig Config { get; }
}
