using System.Text.Json;

namespace ZeusAuto.Engine.Core;

public sealed class JsonConfigLoader
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public MacroConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Config path cannot be empty.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Config file was not found.", path);
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        return ParseConfig(document.RootElement, path);
    }

    public async Task<MacroConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Config path cannot be empty.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Config file was not found.", path);
        }

        await using FileStream stream = File.OpenRead(path);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        }, cancellationToken);

        return ParseConfig(document.RootElement, path);
    }

    private MacroConfig ParseConfig(JsonElement root, string path)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Config root must be a JSON object.");
        }

        if (TryGetProperty(root, "macros", out JsonElement macros) && macros.ValueKind == JsonValueKind.Object)
        {
            return ParseInterfaceProfile(root, macros, path);
        }

        MacroConfig? config = root.Deserialize<MacroConfig>(_jsonOptions);
        return config ?? throw new InvalidOperationException("Config file is empty or invalid.");
    }

    private static MacroConfig ParseInterfaceProfile(JsonElement root, JsonElement macros, string path)
    {
        JsonProperty selectedMacro = default;
        bool hasMacro = false;

        if (TryGetProperty(root, "activeMacro", out JsonElement activeMacroElement) &&
            activeMacroElement.ValueKind == JsonValueKind.String)
        {
            string? activeMacro = activeMacroElement.GetString();
            foreach (JsonProperty macro in macros.EnumerateObject())
            {
                if (string.Equals(macro.Name, activeMacro, StringComparison.OrdinalIgnoreCase))
                {
                    selectedMacro = macro;
                    hasMacro = true;
                    break;
                }
            }
        }

        if (!hasMacro)
        {
            foreach (JsonProperty macro in macros.EnumerateObject())
            {
                selectedMacro = macro;
                hasMacro = true;
                break;
            }
        }

        if (!hasMacro)
        {
            return new MacroConfig
            {
                Enabled = false,
                ProfileName = Path.GetFileNameWithoutExtension(path)
            };
        }

        JsonElement macroValue = selectedMacro.Value;
        int intervalMs = TryGetProperty(macroValue, "interval", out JsonElement intervalElement) &&
            intervalElement.TryGetInt32(out int interval)
                ? interval
                : 0;

        return new MacroConfig
        {
            Enabled = true,
            ProfileName = Path.GetFileNameWithoutExtension(path),
            TriggerButton = selectedMacro.Name,
            ClickButton = selectedMacro.Name,
            ActivationMode = "DoubleClickHold",
            IntervalMs = intervalMs,
            RandomizationEnabled = TryGetBoolean(macroValue, "randomizationEnabled"),
            RandomMin = TryGetInt32(macroValue, "randomMin"),
            RandomMax = TryGetInt32(macroValue, "randomMax"),
            StartHotkey = TryGetString(root, "startHotkey"),
            StopHotkey = TryGetString(root, "stopHotkey")
        };
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int TryGetInt32(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : 0;
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out JsonElement value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               value.GetBoolean();
    }
}
