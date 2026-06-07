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

    /// <summary>
    /// Tempo em milissegundos entre o DOWN e o UP do clique.
    /// 0 = modo legado (down+up simultâneos). Recomendado: 8–12 ms.
    /// </summary>
    public int ClickHoldMs { get; set; } = 10;

    /// <summary>Aplica variação aleatória de ±2 ms ao ClickHoldMs para humanizar o padrão.</summary>
    public bool ClickHoldRandomize { get; set; } = true;

    // ── Atalhos de CPS ───────────────────────────────────────────────────────

    /// <summary>
    /// Hotkey para incrementar o CPS em <see cref="CpsStep"/> por pressão.
    /// Formato idêntico ao StartHotkey: "F7", "Ctrl+Up", "Alt+Shift+F9", etc.
    /// </summary>
    public string? CpsIncrementHotkey { get; set; }

    /// <summary>
    /// Hotkey para decrementar o CPS em <see cref="CpsStep"/> por pressão.
    /// </summary>
    public string? CpsDecrementHotkey { get; set; }

    /// <summary>
    /// Variação de CPS por pressão do atalho. Padrão 0.5.
    /// O valor é convertido internamente para IntervalMs via 1000 / CPS.
    /// </summary>
    public double CpsStep { get; set; } = 0.5;

    /// <summary>
    /// Tempo em ms que a tecla deve ficar pressionada antes de iniciar a repetição
    /// automática acelerada. Padrão 500 ms.
    /// </summary>
    public int CpsHoldThresholdMs { get; set; } = 500;

    /// <summary>
    /// Intervalo inicial entre repetições automáticas de CPS ao segurar a tecla.
    /// Padrão 200 ms.
    /// </summary>
    public int CpsInitialRepeatIntervalMs { get; set; } = 200;

    /// <summary>
    /// Intervalo mínimo (mais rápido) entre repetições automáticas de CPS.
    /// A aceleração progressiva converge para este valor. Padrão 25 ms.
    /// </summary>
    public int CpsMinimumRepeatIntervalMs { get; set; } = 25;

    // ── Atalhos globais ──────────────────────────────────────────────────────

    /// <summary>
    /// Hotkey global para pausar/despausar todos os macros.
    /// Formato idêntico ao StartHotkey: "F7", "Ctrl+Shift+\\", etc.
    /// </summary>
    public string? PauseHotkey { get; set; }

    /// <summary>
    /// Hotkey global para alternar a visibilidade da janela CPS overlay.
    /// </summary>
    public string? OverlayHotkey { get; set; }

    /// <summary>
    /// Hotkey global para ligar/desligar o bip sonoro de todos os macros.
    /// </summary>
    public string? BipToggleHotkey { get; set; }

    /// <summary>
    /// Hotkey global para encerrar o programa (exibe confirmação antes).
    /// </summary>
    public string? EncerrarHotkey { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraOptions { get; set; }
}