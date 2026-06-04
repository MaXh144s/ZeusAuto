using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ZeusAuto.Engine.Core;

namespace ZeusAuto.App;

// ─────────────────────────────────────────────────────────────────────────────
//  MainForm  (arquivo original mantido intacto em aparência)
//
//  Adições mínimas em relação ao original:
//    1. _overlay  : CpsOverlayForm criado no construtor (não visível ainda)
//    2. ApplyProfile: cria EngineSlot por macro e chama _overlay.Apply()
//       passando os slots e o flag profile.Settings.CpsOverlay
//    3. OnFormClosing: descarta overlay
//
//  Nada mais foi alterado.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MainForm : Form
{
    private readonly WebView2 _webView = new();

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly List<EngineSlot> _engines = new();

    // Janela de CPS flutuante — criada uma vez, vive enquanto o app rodar
    private readonly CpsOverlayForm _overlay = new();



    // BUG FIX: guarda o último estado visível para o atalho poder alternar
    private bool _overlayVisible = false;

    public MainForm()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "ZeusAuto.ico");
        if (File.Exists(iconPath))
            Icon = new Icon(iconPath);

        Text          = "ZeusAuto";
        Width         = 1180;
        Height        = 760;
        MinimumSize   = new Size(960, 620);
        StartPosition = FormStartPosition.CenterScreen;

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        Load        += OnLoad;
        FormClosing += OnFormClosing;
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.WebMessageReceived              += OnWebMessageReceived;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled            = true;

        string htmlPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ZeusAuto.html"));
        if (!File.Exists(htmlPath))
            htmlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ZeusAuto.html"));

        _webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            NativeBridgeMessage? message = JsonSerializer.Deserialize<NativeBridgeMessage>(
                e.WebMessageAsJson, _jsonOptions);

            if (message is null) return;

            // BUG FIX: trata o comando de toggle do overlay via atalho do JS
            if (string.Equals(message.Type, "overlay:toggle", StringComparison.OrdinalIgnoreCase))
            {
                _overlayVisible = !_overlayVisible;
                _overlay.ApplyVisibility(_overlayVisible);
                return;
            }

            if (message.Profile is null ||
                !string.Equals(message.Type, "profile:update", StringComparison.OrdinalIgnoreCase))
                return;

            ApplyProfile(message.Profile);
            PostNativeStatus("Engine sincronizada com a interface.");
        }
        catch (Exception ex)
        {
            PostNativeStatus($"Erro ao sincronizar engine: {ex.Message}", isError: true);
        }
    }

    private void ApplyProfile(WebProfile profile)
    {
        DisposeAllEngines();

        bool overlayVisible = profile.Settings?.CpsOverlay ?? false;
        _overlayVisible = overlayVisible; // BUG FIX: mantém estado sincronizado

        if (!profile.Enabled || profile.Macros is null || profile.Macros.Count == 0)
        {
            // Sem macros ativos — overlay fica vazio e obedece ao flag
            _overlay.Apply(Array.Empty<EngineSlot>(), overlayVisible);
            return;
        }

        foreach (KeyValuePair<string, WebMacroConfig> entry in profile.Macros)
        {
            MacroConfig config = ToMacroConfig(profile, entry.Key, entry.Value);
            var slot = new EngineSlot(entry.Key, config);

            _engines.Add(slot);
        }

        // Passa slots ao overlay; visibilidade determinada pelo settings.cpsOverlay
        _overlay.Apply(_engines.AsReadOnly(), overlayVisible);
    }

    private void DisposeAllEngines()
    {
        foreach (var engine in _engines)
            engine.Dispose();
        _engines.Clear();
    }

    private static MacroConfig ToMacroConfig(WebProfile profile, string buttonKey, WebMacroConfig macro)
    {
        string triggerButton = NormalizeMouseButton(buttonKey);

        int clickIntervalMs;
        if (macro.Humanize)
        {
            double avgCps = (macro.CpsMin + macro.CpsMax) / 2.0;
            clickIntervalMs = avgCps > 0 ? (int)(1000.0 / avgCps) : 100;
        }
        else
            clickIntervalMs = macro.CpsBase > 0 ? 1000 / macro.CpsBase : 100;

        int randomMaxMs = 0;
        if (macro.Humanize && macro.CpsMin > 0 && macro.CpsMax > 0)
        {
            int msAtCpsMin = 1000 / macro.CpsMin;
            int msAtCpsMax = 1000 / macro.CpsMax;
            randomMaxMs = Math.Max(0, (msAtCpsMin - msAtCpsMax) / 2);
        }

        int beepHz = macro.BipHz > 0 ? Math.Clamp(macro.BipHz, 200, 1000) : 200;

        // Converte os arrays de teclas do JS (["Ctrl","F8"]) para a string esperada
        // pelo ParseHotkey do InputListener ("Ctrl+F8").
        // Só preenche se shortcuts estiver habilitado e o array não for vazio.
        string? cpsIncrementHotkey = null;
        string? cpsDecrementHotkey = null;
        if (macro.Shortcuts)
        {
            if (macro.CpsPlus is { Length: > 0 })
                cpsIncrementHotkey = string.Join("+", macro.CpsPlus);
            if (macro.CpsMinus is { Length: > 0 })
                cpsDecrementHotkey = string.Join("+", macro.CpsMinus);
        }

        return new MacroConfig
        {
            Enabled              = profile.Enabled,
            ProfileName          = profile.ProfileName ?? "Interface",
            TriggerButton        = triggerButton,
            ClickButton          = triggerButton,
            ActivationMode       = "DoubleClickHold",
            DoubleClickWindowMs  = macro.Interval > 0 ? macro.Interval : 200,
            IntervalMs           = Math.Max(1, clickIntervalMs),
            RandomizationEnabled = macro.Humanize,
            RandomMin            = 0,
            RandomMax            = randomMaxMs,
            BeepEnabled          = macro.Bip,
            BeepHz               = beepHz,
            CpsIncrementHotkey   = cpsIncrementHotkey,
            CpsDecrementHotkey   = cpsDecrementHotkey,
        };
    }

    private static string NormalizeMouseButton(string buttonName) =>
        buttonName.Trim().ToUpperInvariant() switch
        {
            "TECLA ESQUERDA" => "MouseLeft",
            "TECLA DIREITA"  => "MouseRight",
            "TECLA SCROLL"   => "MouseMiddle",
            "TECLA XBUTTON4" => "MouseX1",
            "TECLA XBUTTON5" => "MouseX2",
            "MOUSELEFT"      => "MouseLeft",
            "MOUSERIGHT"     => "MouseRight",
            "MOUSEMIDDLE"    => "MouseMiddle",
            "MOUSEX1"        => "MouseX1",
            "MOUSEX2"        => "MouseX2",
            _                => buttonName
        };

    private void PostNativeStatus(string message, bool isError = false)
    {
        if (_webView.CoreWebView2 is null) return;
        string script = $"window.ZeusNativeBridgeStatus?.({JsonSerializer.Serialize(message)}, {isError.ToString().ToLowerInvariant()});";
        _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        DisposeAllEngines();
        _overlay.Dispose();
        _webView.Dispose();
    }
}
