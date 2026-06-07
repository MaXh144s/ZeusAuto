using System.Text.Json.Serialization;

namespace ZeusAuto.App;

internal sealed class NativeBridgeMessage
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("profile")]
    public WebProfile? Profile { get; set; }

    /// <summary>
    /// Payload de action:trigger — contém o id do atalho a executar
    /// ('pausar', 'cpsOverlay', 'bipToggle', 'encerrar').
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Payload de overlay:saveProfile — perfil de customização do overlay a salvar.
    /// </summary>
    [JsonPropertyName("overlayProfile")]
    public OverlayProfileConfig? OverlayProfile { get; set; }
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

    [JsonPropertyName("settings")]
    public WebSettings? Settings { get; set; }

    [JsonPropertyName("overlayProfile")]
    public OverlayProfileConfig? OverlayProfile { get; set; }
}

internal sealed class WebSettings
{
    /// <summary>
    /// Espelha state.settings.cpsOverlay do JS.
    /// Quando true, a janela CPS deve ser exibida.
    /// </summary>
    [JsonPropertyName("cpsOverlay")]
    public bool CpsOverlay { get; set; }

    [JsonPropertyName("showCpsChange")]
    public bool ShowCpsChange { get; set; }

    [JsonPropertyName("alwaysVisible")]
    public bool AlwaysVisible { get; set; }

    [JsonPropertyName("animate")]
    public bool Animate { get; set; }
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
    public int BipHz { get; set; } = 200;

    /// <summary>
    /// Teclas do atalho de incremento de CPS (ex: ["Ctrl","F8"]).
    /// Enviadas pelo JS como array — convertidas para string com "+" no ToMacroConfig.
    /// </summary>
    [JsonPropertyName("cpsPlus")]
    public string[]? CpsPlus { get; set; }

    /// <summary>
    /// Teclas do atalho de decremento de CPS (ex: ["Ctrl","F9"]).
    /// </summary>
    [JsonPropertyName("cpsMinus")]
    public string[]? CpsMinus { get; set; }

    /// <summary>Liga/desliga os atalhos de CPS.</summary>
    [JsonPropertyName("shortcuts")]
    public bool Shortcuts { get; set; }
}

internal sealed class WebShortcutConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("keys")]
    public string[]? Keys { get; set; }
}
// ─── Overlay Profile ───────────────────────────────────────────────────────────

internal sealed class OverlayProfileConfig
{
    [JsonPropertyName("profileName")]
    public string? ProfileName { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("background")]
    public OverlayBackground? Background { get; set; }

    [JsonPropertyName("border")]
    public OverlayBorder? Border { get; set; }

    [JsonPropertyName("elements")]
    public List<OverlayElement>? Elements { get; set; }
}

internal sealed class OverlayBackground
{
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#12141C";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 0.92;
}

internal sealed class OverlayBorder
{
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#466EFF";

    [JsonPropertyName("glowEnabled")]
    public bool GlowEnabled { get; set; } = true;

    [JsonPropertyName("glowIntensity")]
    public int GlowIntensity { get; set; } = 20;

    [JsonPropertyName("glowColor")]
    public string GlowColor { get; set; } = "#466EFF";
}

internal sealed class OverlayElement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 12.0;

    [JsonPropertyName("colorActive")]
    public string ColorActive { get; set; } = "#3CE18C";

    [JsonPropertyName("colorIdle")]
    public string ColorIdle { get; set; } = "#78788C";

    [JsonPropertyName("colorPaused")]
    public string ColorPaused { get; set; } = "#FFA032";

    [JsonPropertyName("responsiveRules")]
    public List<OverlayResponsiveRule>? ResponsiveRules { get; set; }
}

internal sealed class OverlayResponsiveRule
{
    [JsonPropertyName("widthMin")]  public int WidthMin  { get; set; }
    [JsonPropertyName("widthMax")]  public int WidthMax  { get; set; }
    [JsonPropertyName("heightMin")] public int HeightMin { get; set; }
    [JsonPropertyName("heightMax")] public int HeightMax { get; set; }

    /// <summary>"scaleFont" | "relocate" | "hide"</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "scaleFont";

    [JsonPropertyName("newPosition")]
    public string? NewPosition { get; set; }
}
