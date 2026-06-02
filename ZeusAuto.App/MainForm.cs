using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ZeusAuto.Engine.Core;

namespace ZeusAuto.App;

public sealed class MainForm : Form
{
    private readonly WebView2 _webView = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Um engine independente por botão de gatilho configurado
    private readonly List<MacroEngine> _engines = new();

    public MainForm()
    {
        Text = "ZeusAuto";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(960, 620);
        StartPosition = FormStartPosition.CenterScreen;

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        Load += OnLoad;
        FormClosing += OnFormClosing;
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

        string htmlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ZeusAuto.html"));
        if (!File.Exists(htmlPath))
        {
            htmlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ZeusAuto.html"));
        }

        _webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            NativeBridgeMessage? message = JsonSerializer.Deserialize<NativeBridgeMessage>(e.WebMessageAsJson, _jsonOptions);
            if (message is not { Profile: not null } ||
                !string.Equals(message.Type, "profile:update", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ApplyProfile(message.Profile);
            PostNativeStatus("Engine sincronizada com a interface.");
        }
        catch (Exception ex)
        {
            PostNativeStatus($"Erro ao sincronizar engine: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Reconstrói a lista de engines para corresponder a cada macro do perfil.
    /// Cria um MacroEngine independente por entrada no dicionário de macros.
    /// </summary>
    private void ApplyProfile(WebProfile profile)
    {
        // Para e descarta todos os engines anteriores
        DisposeAllEngines();

        if (!profile.Enabled || profile.Macros is null || profile.Macros.Count == 0)
        {
            return;
        }

        // Cria um engine para cada macro configurado no perfil
        foreach (KeyValuePair<string, WebMacroConfig> entry in profile.Macros)
        {
            MacroConfig config = ToMacroConfig(profile, entry.Key, entry.Value);
            MacroEngine engine = new MacroEngine();
            engine.LoadConfig(config);
            engine.StartListening();
            engine.EnableMonitoring();
            _engines.Add(engine);
        }
    }

    private void DisposeAllEngines()
    {
        foreach (MacroEngine engine in _engines)
        {
            engine.Dispose();
        }
        _engines.Clear();
    }

    private static MacroConfig ToMacroConfig(WebProfile profile, string buttonKey, WebMacroConfig macro)
    {
        string triggerButton = NormalizeMouseButton(buttonKey);

        // --- Delay de clique: converte CPS → ms ---
        int clickIntervalMs;
        if (macro.Humanize)
        {
            double avgCps = (macro.CpsMin + macro.CpsMax) / 2.0;
            clickIntervalMs = avgCps > 0 ? (int)(1000.0 / avgCps) : 100;
        }
        else
        {
            clickIntervalMs = macro.CpsBase > 0 ? 1000 / macro.CpsBase : 100;
        }

        // --- Humanize: offset de variação em ms ---
        int randomMaxMs = 0;
        if (macro.Humanize && macro.CpsMin > 0 && macro.CpsMax > 0)
        {
            int msAtCpsMin = 1000 / macro.CpsMin;
            int msAtCpsMax = 1000 / macro.CpsMax;
            randomMaxMs = Math.Max(0, (msAtCpsMin - msAtCpsMax) / 2);
        }

        // --- Frequência do bip: clamp ao range 200–1000 Hz ---
        int beepHz = macro.BipHz > 0 ? Math.Clamp(macro.BipHz, 200, 1000) : 870;

        return new MacroConfig
        {
            Enabled = profile.Enabled,
            ProfileName = profile.ProfileName ?? "Interface",
            TriggerButton = triggerButton,
            ClickButton = triggerButton,
            ActivationMode = "DoubleClickHold",
            DoubleClickWindowMs = macro.Interval > 0 ? macro.Interval : 200,
            IntervalMs = Math.Max(1, clickIntervalMs),
            RandomizationEnabled = macro.Humanize,
            RandomMin = 0,
            RandomMax = randomMaxMs,
            BeepEnabled = macro.Bip,
            BeepHz = beepHz
        };
    }

    private static string NormalizeMouseButton(string buttonName)
    {
        return buttonName.Trim().ToUpperInvariant() switch
        {
            "TECLA ESQUERDA" => "MouseLeft",
            "TECLA DIREITA" => "MouseRight",
            "TECLA SCROLL" => "MouseMiddle",
            "TECLA XBUTTON4" => "MouseX1",
            "TECLA XBUTTON5" => "MouseX2",
            "MOUSELEFT" => "MouseLeft",
            "MOUSERIGHT" => "MouseRight",
            "MOUSEMIDDLE" => "MouseMiddle",
            "MOUSEX1" => "MouseX1",
            "MOUSEX2" => "MouseX2",
            _ => buttonName
        };
    }

    private void PostNativeStatus(string message, bool isError = false)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        string script = $"window.ZeusNativeBridgeStatus?.({JsonSerializer.Serialize(message)}, {isError.ToString().ToLowerInvariant()});";
        _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        DisposeAllEngines();
        _webView.Dispose();
    }
}