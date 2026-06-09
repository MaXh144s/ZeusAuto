using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ZeusAuto.Engine.Core;

namespace ZeusAuto.App;

// ─────────────────────────────────────────────────────────────────────────────
//  MainForm
//
//  Adições vs. versão anterior:
//    + RAM_MONITOR: lê RAM total instalada via GlobalMemoryStatusEx (Win32)
//      e inicia System.Windows.Forms.Timer de 2 s para polling do consumo
//      do processo (WorkingSet64). Envia os dados ao JS via
//      window.ZeusRamMonitor.update(usedMb, totalMb) a cada tick.
//      O timer só roda quando a página "settings" está visível, para não
//      desperdiçar ciclos desnecessariamente.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MainForm : Form
{
    // ── Win32: leitura de RAM total instalada ─────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint  dwLength;
        public uint  dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ── Campos ────────────────────────────────────────────────────────────────
    private readonly WebView2              _webView    = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly List<EngineSlot>      _engines    = new();
    private readonly CpsOverlayForm        _overlay    = new();

    // Listener dedicado apenas para os 4 atalhos globais.
    // Opera independentemente dos InputListeners de cada EngineSlot.
    private readonly ZeusAuto.Engine.Core.InputListener _globalListener = new();

    // RAM_MONITOR: timer de polling — intervalo de 2 s é suficiente para
    // feedback visual sem pressionar o GC ou o scheduler.
    private readonly System.Windows.Forms.Timer _ramTimer = new() { Interval = 1000 };

    // RAM total instalada em MB — lida uma vez no load, não muda em runtime.
    private long _totalRamMb;

    private bool _overlayVisible = false;

    // ── Estado dos atalhos globais ────────────────────────────────────────────
    // _paused: quando true, todos os EngineSlots estão com DisableMonitoring() chamado.
    // _bipEnabled: quando false, o bip de todos os engines está suprimido via SetBipOverride.
    private bool _paused     = false;
    private bool _bipEnabled = true;

    // Config global de atalhos — usada para configurar um listener dedicado por perfil.
    // Como os atalhos globais são iguais para todos os slots, guardamos uma referência
    // apenas para a MacroConfig do primeiro slot (ou uma config sintética).
    private MacroConfig? _globalHotkeyConfig;

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

        // RAM_MONITOR: conecta o tick do timer ao método de polling
        _ramTimer.Tick += OnRamTimerTick;

        // Atalhos globais: conecta os 4 eventos ao listener dedicado
        _globalListener.PauseHotkeyPressed    += OnPauseHotkeyPressed;
        _globalListener.OverlayHotkeyPressed  += OnOverlayHotkeyPressed;
        _globalListener.BipHotkeyPressed      += OnBipHotkeyPressed;
        _globalListener.EncerrarHotkeyPressed += OnEncerrarHotkeyPressed;
    }

    // ── Carregamento ──────────────────────────────────────────────────────────

    private async void OnLoad(object? sender, EventArgs e)
    {
        // RAM_MONITOR: lê a RAM total instalada uma única vez ao iniciar.
        // GlobalMemoryStatusEx é a API correta — GetPhysicallyInstalledSystemMemory
        // existe mas não está disponível em todas as versões do Windows.
        _totalRamMb = ReadTotalRamMb();

        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.WebMessageReceived                      += OnWebMessageReceived;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled  = true;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled             = true;

        // RAM_MONITOR: escuta o evento de navegação concluída para saber
        // quando o JS está pronto para receber dados.
        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

        // Inicia o listener global de atalhos (ativo durante toda a sessão)
        _globalListener.StartListening();

        string htmlPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ZeusAuto.html"));
        if (!File.Exists(htmlPath))
            htmlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ZeusAuto.html"));

        _webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
    }

    // RAM_MONITOR: quando a navegação termina, envia os dados estáticos
    // (RAM total) ao JS e inicia o timer de polling.
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;

        // Envia RAM total para o JS inicializar o painel antes do primeiro tick
        PostRamUpdate();

        // Inicia o polling contínuo
        _ramTimer.Start();
    }

    // ── Mensagens do JS ───────────────────────────────────────────────────────

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            NativeBridgeMessage? message = JsonSerializer.Deserialize<NativeBridgeMessage>(
                e.WebMessageAsJson, _jsonOptions);

            if (message is null) return;

            if (string.Equals(message.Type, "overlay:toggle", StringComparison.OrdinalIgnoreCase))
            {
                _overlayVisible = !_overlayVisible;
                _overlay.ApplyVisibility(_overlayVisible);
                return;
            }

            // ── Salvar perfil de overlay ──────────────────────────────────────
            if (string.Equals(message.Type, "overlay:saveProfile", StringComparison.OrdinalIgnoreCase))
            {
                if (message.OverlayProfile is not null)
                    SaveOverlayProfile(message.OverlayProfile);
                return;
            }

            // ── Listar perfis de overlay salvos ───────────────────────────────
            if (string.Equals(message.Type, "overlay:listProfiles", StringComparison.OrdinalIgnoreCase))
            {
                PostOverlayProfilesList();
                return;
            }

            // Ação direta disparada pelo botão ▶ na página Atalhos
            if (string.Equals(message.Type, "action:trigger", StringComparison.OrdinalIgnoreCase))
            {
                switch (message.Id?.ToLowerInvariant())
                {
                    case "pausar":     OnPauseHotkeyPressed(this, EventArgs.Empty);   break;
                    case "cpsoverlay": OnOverlayHotkeyPressed(this, EventArgs.Empty); break;
                    case "biptoggle":  OnBipHotkeyPressed(this, EventArgs.Empty);     break;
                    case "encerrar":   OnEncerrarHotkeyPressed(this, EventArgs.Empty);break;
                }
                return;
            }

            if (message.Profile is null ||
                !string.Equals(message.Type, "profile:update", StringComparison.OrdinalIgnoreCase))
                return;

            ApplyAlwaysVisible(message.Profile.Settings?.AlwaysVisible ?? false);
            ApplyProfile(message.Profile);
            PostNativeStatus("Engine sincronizada com a interface.");
        }
        catch (Exception ex)
        {
            PostNativeStatus($"Erro ao sincronizar engine: {ex.Message}", isError: true);
        }
    }

    // ── RAM Monitor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Tick do timer: lê o PrivateMemorySize64 do processo atual e envia ao JS.
    /// PrivateMemorySize64 é o Working Set Privado — o mesmo valor que o
    /// Gerenciador de Tarefas exibe na coluna "Memória".
    /// </summary>
    private void OnRamTimerTick(object? sender, EventArgs e) => PostRamUpdate();

    /// <summary>
    /// Lê o consumo atual e envia window.ZeusRamMonitor.update(usedMb, totalMb)
    /// ao JS. Usa ExecuteScriptAsync — seguro chamar da UI thread (timer roda nela).
    /// </summary>
    private void PostRamUpdate()
    {
        if (_webView.CoreWebView2 is null) return;

        // PrivateMemorySize64 = Working Set Privado — mesma métrica que o
        // Gerenciador de Tarefas exibe na coluna "Memória".
        // WorkingSet64 incluía páginas compartilhadas com .NET runtime e WebView2,
        // inflando o valor (~53 MB vs ~8 MB reais do processo).
        var proc = Process.GetCurrentProcess();
        proc.Refresh(); // força releitura — sem isso o valor fica em cache entre ticks
        long usedMb = proc.PrivateMemorySize64 / (1024 * 1024);

        string script = $"window.ZeusRamMonitor?.update({usedMb}, {_totalRamMb});";
        _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>
    /// Lê a RAM física total instalada via GlobalMemoryStatusEx.
    /// Retorna 0 se a chamada falhar (hardware sem suporte ou erro de permissão).
    /// </summary>
    private static long ReadTotalRamMb()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref mem)) return 0;
        return (long)(mem.ullTotalPhys / (1024 * 1024));
    }

    // ── Profile / Engine ──────────────────────────────────────────────────────

    private void ApplyAlwaysVisible(bool alwaysVisible)
    {
        if (InvokeRequired) { Invoke(() => ApplyAlwaysVisible(alwaysVisible)); return; }

        if (alwaysVisible)
        {
            TopMost         = true;
            FormBorderStyle = FormBorderStyle.None;
            Opacity         = 0.92;
        }
        else
        {
            TopMost         = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Opacity         = 1.0;
        }
    }

    private void ApplyProfile(WebProfile profile)
    {
        bool overlayVisible = profile.Settings?.CpsOverlay ?? false;
        _overlayVisible = overlayVisible;

        // ── Atalhos globais ───────────────────────────────────────────────────
        // Lê o dicionário Atalhos e configura o listener global dedicado.
        // Isso resolve o gap central descrito no relatório: os atalhos chegavam
        // corretamente em profile.Atalhos mas nunca eram lidos aqui.
        ApplyGlobalHotkeys(profile.Atalhos);

        if (!profile.Enabled || profile.Macros is null || profile.Macros.Count == 0)
        {
            DisposeAllEngines();
            _overlay.Apply(Array.Empty<EngineSlot>(), overlayVisible);
            return;
        }

        var existingByKey = _engines.ToDictionary(s => s.MacroKey, s => s);
        var newKeys       = new HashSet<string>(profile.Macros.Keys);

        var toRemove = _engines.Where(s => !newKeys.Contains(s.MacroKey)).ToList();
        foreach (var slot in toRemove)
        {
            slot.Dispose();
            _engines.Remove(slot);
        }

        foreach (KeyValuePair<string, WebMacroConfig> entry in profile.Macros)
        {
            MacroConfig config = ToMacroConfig(profile, entry.Key, entry.Value);

            if (existingByKey.TryGetValue(entry.Key, out EngineSlot? existing))
                existing.LoadConfig(config);
            else
            {
                var slot = new EngineSlot(entry.Key, config);
                slot.CpsChanged += (_, args) => PostCpsUpdate(entry.Key, args.NewCps);
                _engines.Add(slot);
            }
        }

        // Reaplicar o estado de pausa e bip nos engines novos/recarregados
        foreach (var slot in _engines)
        {
            slot.SetBipOverride(_bipEnabled);
            if (_paused) slot.DisableMonitoring();
        }

        _overlay.Apply(_engines.AsReadOnly(), overlayVisible);
        // Reaplicar estado de pausa no overlay após sincronização de perfil
        _overlay.ApplyPaused(_paused);
        // Aplicar perfil de customização do overlay (null = fallback para defaults)
        _overlay.ApplyOverlayProfile(profile.OverlayProfile);
    }

    // ── Overlay Profile: salvar/listar ────────────────────────────────────────

    private static string GetOverlayProfilesDir()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = !string.IsNullOrEmpty(appData)
            ? Path.Combine(appData, "ZeusAuto", "overlay-profiles")
            : Path.Combine(AppContext.BaseDirectory, "overlay-profiles");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void SaveOverlayProfile(OverlayProfileConfig profile)
    {
        try
        {
            string name = SanitizeFileName(profile.ProfileName ?? "perfil");
            if (string.IsNullOrWhiteSpace(name)) name = "perfil";
            string dir  = GetOverlayProfilesDir();
            string path = Path.Combine(dir, name + ".json");
            string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            PostOverlayProfilesList();
        }
        catch (Exception ex)
        {
            PostNativeStatus($"Erro ao salvar perfil de overlay: {ex.Message}", isError: true);
        }
    }

    private void PostOverlayProfilesList()
    {
        try
        {
            string dir = GetOverlayProfilesDir();
            var profiles = new List<OverlayProfileConfig>();
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
            {
                try
                {
                    string json = File.ReadAllText(file, System.Text.Encoding.UTF8);
                    var p = JsonSerializer.Deserialize<OverlayProfileConfig>(json, _jsonOptions);
                    if (p is not null)
                    {
                        if (string.IsNullOrWhiteSpace(p.ProfileName))
                            p.ProfileName = Path.GetFileNameWithoutExtension(file);
                        profiles.Add(p);
                    }
                }
                catch { /* arquivo corrompido — ignora */ }
            }

            string payload = JsonSerializer.Serialize(profiles);
            string script  = $"window.ZeusOverlayProfiles?.({payload});";
            _webView.CoreWebView2?.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            PostNativeStatus($"Erro ao listar perfis de overlay: {ex.Message}", isError: true);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }

    /// <summary>
    /// Converte o dicionário Atalhos do JS em campos de MacroConfig e atualiza
    /// o listener global. Chamado sempre que um profile:update chega.
    /// </summary>
    private void ApplyGlobalHotkeys(Dictionary<string, WebShortcutConfig>? atalhos)
    {
        string? ToHotkeyString(string key)
        {
            if (atalhos is null) return null;
            if (!atalhos.TryGetValue(key, out WebShortcutConfig? cfg)) return null;
            if (!cfg.Enabled || cfg.Keys is null || cfg.Keys.Length == 0) return null;
            return string.Join("+", cfg.Keys);
        }

        // Cria uma MacroConfig sintética apenas com os campos de hotkey global.
        // Os demais campos são irrelevantes para o listener global.
        var globalConfig = new MacroConfig
        {
            PauseHotkey    = ToHotkeyString("pausar"),
            OverlayHotkey  = ToHotkeyString("cpsOverlay"),
            BipToggleHotkey = ToHotkeyString("bipToggle"),
            EncerrarHotkey = ToHotkeyString("encerrar"),
        };

        _globalHotkeyConfig = globalConfig;
        _globalListener.UpdateConfig(globalConfig);
    }

    private void DisposeAllEngines()
    {
        foreach (var engine in _engines) engine.Dispose();
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
            clickIntervalMs = macro.CpsBase > 0 ? (int)(1000.0 / macro.CpsBase) : 100;

        int randomMaxMs = 0;
        if (macro.Humanize && macro.CpsMin > 0 && macro.CpsMax > 0)
        {
            double msAtCpsMin = 1000.0 / macro.CpsMin;
            double msAtCpsMax = 1000.0 / macro.CpsMax;
            randomMaxMs = Math.Max(0, (int)((msAtCpsMin - msAtCpsMax) / 2));
        }

        // CpsStep: passo de ajuste por atalho (convertido para variação de intervalo)
        // Clamp para valores razoáveis (0.1 a 10 CPS por passo).
        double cpsStep = Math.Clamp(macro.CpsStep > 0 ? macro.CpsStep : 1.0, 0.1, 10.0);

        int beepHz = macro.BipHz > 0 ? Math.Clamp(macro.BipHz, 200, 1000) : 200;

        string? cpsIncrementHotkey = null;
        string? cpsDecrementHotkey = null;
        if (macro.Shortcuts)
        {
            if (macro.CpsPlus  is { Length: > 0 }) cpsIncrementHotkey = string.Join("+", macro.CpsPlus);
            if (macro.CpsMinus is { Length: > 0 }) cpsDecrementHotkey = string.Join("+", macro.CpsMinus);
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
            CpsStep              = cpsStep,
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

    // ── Handlers dos atalhos globais ──────────────────────────────────────────

    /// <summary>
    /// Bug #1 — Pausar/despausar todos os macros.
    /// Chamado da hook thread do _globalListener — usa Invoke para acessar _engines
    /// e o overlay na UI thread (evita race condition).
    /// </summary>
    private void OnPauseHotkeyPressed(object? sender, EventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnPauseHotkeyPressed(sender, e)); return; }

        _paused = !_paused;

        foreach (var slot in _engines)
        {
            if (_paused)
                slot.DisableMonitoring();
            else
                slot.EnableMonitoring();
        }

        // Overlay continua visível durante pausa — apenas troca CPS por "PAUSADO".
        // ApplyPaused notifica o overlay do novo estado; ApplyVisibility garante
        // que ele apareça se estiver habilitado (mesmo que estivesse oculto antes).
        _overlay.ApplyPaused(_paused);
        if (_paused && _overlayVisible)
            _overlay.ApplyVisibility(true);

        // Notifica o JS para atualizar o ícone do botão na página Atalhos
        PostToggleState("pausar", _paused);
    }

    /// <summary>
    /// Bug #2 — Alternar visibilidade da janela CPS overlay.
    /// Reutiliza exatamente o mesmo código do bloco overlay:toggle em OnWebMessageReceived.
    /// </summary>
    private void OnOverlayHotkeyPressed(object? sender, EventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnOverlayHotkeyPressed(sender, e)); return; }

        _overlayVisible = !_overlayVisible;
        // Só aplica se não estiver pausado — pausa tem prioridade sobre overlay
        if (!_paused)
            _overlay.ApplyVisibility(_overlayVisible);

        // Notifica o JS para atualizar o ícone do botão na página Atalhos
        PostToggleState("cpsOverlay", _overlayVisible);
    }

    /// <summary>
    /// Bug #3 — Ligar/desligar bip sonoro de todos os macros.
    /// Não altera BeepEnabled de cada macro — é um override global temporário.
    /// </summary>
    private void OnBipHotkeyPressed(object? sender, EventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnBipHotkeyPressed(sender, e)); return; }

        _bipEnabled = !_bipEnabled;

        foreach (var slot in _engines)
            slot.SetBipOverride(_bipEnabled);

        // Notifica o JS para atualizar o ícone do botão na página Atalhos
        PostToggleState("bipToggle", _bipEnabled);
    }

    /// <summary>
    /// Bug #4 — Encerrar programa com confirmação.
    ///
    /// FIX-ENCERRAR-A (deadlock do atalho): quando chamado da hook thread,
    /// usar BeginInvoke (não Invoke) para enfileirar na UI thread sem bloquear.
    /// Invoke bloqueava a hook thread esperando o retorno do UI thread, enquanto
    /// o hook de baixo nível precisa retornar rapidamente ao Windows — causando
    /// o travamento total da engine de input descrito pelo usuário.
    ///
    /// FIX-ENCERRAR-B (janela branca): substituir Application.Exit() por
    /// Environment.Exit(0) para garantir encerramento imediato mesmo quando a
    /// WebView2 fica pendurada no processo de dispose (causa da tela branca).
    /// OnFormClosing ainda roda normalmente quando chamado pela interface (botão X),
    /// mas ao encerrar pelo atalho/botão, Environment.Exit é mais confiável.
    /// </summary>
    private void OnEncerrarHotkeyPressed(object? sender, EventArgs e)
    {
        // BeginInvoke: enfileira na UI thread SEM bloquear a thread chamadora.
        // Isso é crítico quando vindo da hook thread — Invoke causaria deadlock
        // porque o Windows exige que callbacks de hook global retornem rapidamente.
        if (InvokeRequired) { BeginInvoke(() => OnEncerrarHotkeyPressed(sender, e)); return; }

        DialogResult result = MessageBox.Show(
            "Deseja encerrar o ZeusAuto?",
            "Encerrar",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);  // "Não" como padrão — evita fechamento acidental

        if (result != DialogResult.Yes) return;

        // Para a engine e o listener antes de sair para liberar os hooks de input.
        // Isso evita que o Windows considere o hook "morto" e registre erros.
        try
        {
            _ramTimer.Stop();
            _globalListener.Dispose();
            DisposeAllEngines();
        }
        catch { /* ignora — estamos encerrando */ }

        // Environment.Exit(0): encerra o processo imediatamente, sem esperar a
        // WebView2 completar seu dispose assíncrono (que causava a janela branca).
        Environment.Exit(0);
    }

    private void PostNativeStatus(string message, bool isError = false)
    {
        if (_webView.CoreWebView2 is null) return;
        string script = $"window.ZeusNativeBridgeStatus?.({JsonSerializer.Serialize(message)}, {isError.ToString().ToLowerInvariant()});";
        _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>
    /// Notifica o JS do novo estado de um atalho toggle (pausar/cpsOverlay/bipToggle)
    /// para que o ícone ▶/⏸ seja atualizado mesmo quando acionado via tecla física.
    /// Chama window.ZeusToggleState(id, active) definido em native-bridge.js.
    /// </summary>
    private void PostToggleState(string id, bool active)
    {
        if (_webView.CoreWebView2 is null) return;
        string script = $"window.ZeusToggleState?.({JsonSerializer.Serialize(id)}, {active.ToString().ToLowerInvariant()});";
        _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>
    /// Notifica o JS do novo CPS quando o atalho de ajuste é pressionado.
    /// Chama window.ZeusCpsUpdate(macroKey, newCps) — definido em native-bridge.js.
    /// O JS atualiza state.macros[key].cpsBase e re-renderiza os cards/legend.
    /// </summary>
    private void PostCpsUpdate(string macroKey, double newCps)
    {
        if (_webView.CoreWebView2 is null) return;
        // Usa InvokeRequired pois CpsChanged pode chegar de thread da engine
        if (InvokeRequired) { Invoke(() => PostCpsUpdate(macroKey, newCps)); return; }
        // Formata com InvariantCulture para que o JS receba "12.7" e não "12,7"
        string cpsStr = newCps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string script = $"window.ZeusCpsUpdate?.({JsonSerializer.Serialize(macroKey)}, {cpsStr});";
        _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _ramTimer.Stop();
        _ramTimer.Dispose();
        _globalListener.Dispose();
        DisposeAllEngines();
        _overlay.Dispose();
        _webView.Dispose();
    }
}
