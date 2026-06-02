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
    /// <summary>Janela de tempo do double-click em ms (ex: 200).</summary>
    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    /// <summary>Velocidade fixa em CPS quando Humanize está desligado (ex: 13).</summary>
    [JsonPropertyName("cpsBase")]
    public int CpsBase { get; set; }

    /// <summary>Liga/desliga o modo de variação de velocidade.</summary>
    [JsonPropertyName("humanize")]
    public bool Humanize { get; set; }

    /// <summary>CPS mínimo do range humanize (ex: 10).</summary>
    [JsonPropertyName("cpsMin")]
    public int CpsMin { get; set; }

    /// <summary>CPS máximo do range humanize (ex: 16).</summary>
    [JsonPropertyName("cpsMax")]
    public int CpsMax { get; set; }

    /// <summary>Emite bip sonoro ao iniciar o macro.</summary>
    [JsonPropertyName("bip")]
    public bool Bip { get; set; }

    /// <summary>Frequência do bip em Hz (200–1000).</summary>
    [JsonPropertyName("bipHz")]
    public int BipHz { get; set; } = 870;
}

internal sealed class WebShortcutConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("keys")]
    public string[]? Keys { get; set; }
}