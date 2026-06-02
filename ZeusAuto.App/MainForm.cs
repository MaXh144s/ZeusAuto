using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ZeusAuto.Engine.Core;

namespace ZeusAuto.App;

public sealed class MainForm : Form
{
    private readonly WebView2 _webView = new();
    private readonly MacroEngine _engine = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

        _engine.StartListening();

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

            MacroConfig config = ToMacroConfig(message.Profile);
            _engine.LoadConfig(config);
            _engine.EnableMonitoring();
            PostNativeStatus("Engine sincronizada com a interface.");
        }
        catch (Exception ex)
        {
            PostNativeStatus($"Erro ao sincronizar engine: {ex.Message}", isError: true);
        }
    }

    private static MacroConfig ToMacroConfig(WebProfile profile)
    {
        KeyValuePair<string, WebMacroConfig>? selected = SelectMacro(profile);
        if (selected is null)
        {
            return new MacroConfig
            {
                Enabled = false,
                ProfileName = profile.ProfileName ?? "Interface"
            };
        }

        string triggerButton = NormalizeMouseButton(selected.Value.Key);
        WebMacroConfig macro = selected.Value.Value;

        // --- Delay de clique: converte CPS → ms ---
        // Humanize OFF: cpsBase fixo → 1000 / cpsBase
        // Humanize ON:  média de (cpsMin + cpsMax) / 2 → 1000 / avg
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
        // CPS maior (cpsMax) → menor delay em ms → limite inferior do range
        // CPS menor (cpsMin) → maior delay em ms → limite superior do range
        // O CalculateDelay usa: interval + Random(-offset, +offset)
        // Logo offset = (msAtCpsMin - msAtCpsMax) / 2 para cobrir o range completo
        int randomMaxMs = 0;
        if (macro.Humanize && macro.CpsMin > 0 && macro.CpsMax > 0)
        {
            int msAtCpsMin = 1000 / macro.CpsMin; // delay maior (CPS mais lento)
            int msAtCpsMax = 1000 / macro.CpsMax; // delay menor (CPS mais rápido)
            randomMaxMs = Math.Max(0, (msAtCpsMin - msAtCpsMax) / 2);
        }

        return new MacroConfig
        {
            Enabled = profile.Enabled,
            ProfileName = profile.ProfileName ?? "Interface",
            TriggerButton = triggerButton,
            ClickButton = triggerButton,
            ActivationMode = "DoubleClickHold",
            DoubleClickWindowMs = macro.Interval > 0 ? macro.Interval : 200, // janela do double-click
            IntervalMs = Math.Max(1, clickIntervalMs),                         // delay entre cliques (ms)
            RandomizationEnabled = macro.Humanize,
            RandomMin = 0,                                                     // offset mínimo (sempre 0)
            RandomMax = randomMaxMs                                             // offset máximo em ms
        };
    }

    private static KeyValuePair<string, WebMacroConfig>? SelectMacro(WebProfile profile)
    {
        if (profile.Macros is null || profile.Macros.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.ActiveMacro) &&
            profile.Macros.TryGetValue(profile.ActiveMacro, out WebMacroConfig? active))
        {
            return new KeyValuePair<string, WebMacroConfig>(profile.ActiveMacro, active);
        }

        return profile.Macros.First();
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
        _engine.Dispose();
        _webView.Dispose();
    }
}
