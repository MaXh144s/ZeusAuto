using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeusAuto.Engine.Core;

public sealed class MacroConfig
{
    public bool Enabled { get; set; }

    public string? ProfileName { get; set; }

    public string? TriggerButton { get; set; }

    public string? ClickButton { get; set; }

    public string? ActivationMode { get; set; }

    public int IntervalMs { get; set; }

    public bool RandomizationEnabled { get; set; }

    public int RandomMin { get; set; }

    public int RandomMax { get; set; }

    public string? StartHotkey { get; set; }

    public string? StopHotkey { get; set; }

    public int? DoubleClickWindowMs { get; set; }

    /// <summary>Emite bip sonoro ao iniciar o macro.</summary>
    public bool BeepEnabled { get; set; }

    /// <summary>Frequência do bip em Hz (200–1000). Padrão 200.</summary>
    public int BeepHz { get; set; } = 200;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraOptions { get; set; }
}