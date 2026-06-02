using System.Text.Json.Serialization;

namespace ZeusAuto.App;

internal sealed class NativeBridgeMessage
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("profile")]
    public WebProfile? Profile { get; set; }
}

internal sealed class WebProfile
{
    [JsonPropertyName("profileName")]
    public string? ProfileName { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("activeMacro")]
    public string? ActiveMacro { get; set; }

    [JsonPropertyName("macros")]
    public Dictionary<string, WebMacroConfig>? Macros { get; set; }

    [JsonPropertyName("atalhos")]
    public Dictionary<string, WebShortcutConfig>? Atalhos { get; set; }
}

internal sealed class WebMacroConfig
{
    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("humanize")]
    public bool Humanize { get; set; }

    [JsonPropertyName("cpsMin")]
    public int CpsMin { get; set; }

    [JsonPropertyName("cpsMax")]
    public int CpsMax { get; set; }
}

internal sealed class WebShortcutConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("keys")]
    public string[]? Keys { get; set; }
}
