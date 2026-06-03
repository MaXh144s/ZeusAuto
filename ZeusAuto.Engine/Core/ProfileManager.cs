namespace ZeusAuto.Engine.Core;

public sealed class ProfileManager : IDisposable
{
    // Limite de tentativas consecutivas de reload para evitar loop infinito
    // quando o arquivo está corrompido ou sendo escrito continuamente.
    private const int MaxReloadAttempts = 5;

    private readonly object          _sync = new();
    private readonly JsonConfigLoader _loader;
    private readonly Timer           _reloadTimer;
    private FileSystemWatcher? _watcher;
    private string?            _activeProfilePath;
    private int                _reloadAttempts;   // contador de tentativas consecutivas
    private bool               _disposed;

    public ProfileManager(JsonConfigLoader? loader = null)
    {
        _loader      = loader ?? new JsonConfigLoader();
        _reloadTimer = new Timer(ReloadFromTimer);
    }

    public event EventHandler<ProfileChangedEventArgs>? ProfileChanged;

    public string? ActiveProfilePath
    {
        get { lock (_sync) { return _activeProfilePath; } }
    }

    public MacroConfig LoadProfile(string profilePath)
    {
        ThrowIfDisposed();

        MacroConfig config = _loader.Load(profilePath);
        lock (_sync)
        {
            _activeProfilePath = Path.GetFullPath(profilePath);
            _reloadAttempts    = 0;
            ConfigureWatcher(_activeProfilePath);
        }

        ProfileChanged?.Invoke(this, new ProfileChangedEventArgs(_activeProfilePath, config));
        return config;
    }

    public MacroConfig SwitchProfile(string profilePath) => LoadProfile(profilePath);

    /// <summary>
    /// Serializa <paramref name="config"/> e sobrescreve o arquivo do perfil ativo.
    /// Chamado pelo EngineSlot quando um ajuste de CPS por hotkey precisa ser persistido.
    ///
    /// A escrita usa um arquivo temporário + rename atômico para evitar corrupção
    /// caso o processo seja encerrado no meio da gravação.
    /// O FileSystemWatcher vai disparar um Changed, mas o debounce de 150 ms e o
    /// contador de tentativas do ProfileManager absorvem sem recarregar em loop.
    /// </summary>
    public void SaveConfig(MacroConfig config)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);

        string? path;
        lock (_sync) { path = _activeProfilePath; }

        if (string.IsNullOrWhiteSpace(path))
            return;

        string json = System.Text.Json.JsonSerializer.Serialize(config, _saveOptions);

        // Grava em temp e renomeia — operação atômica no mesmo volume
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }

    private static readonly System.Text.Json.JsonSerializerOptions _saveOptions = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public IReadOnlyList<string> ListProfiles(string profilesDirectory)
    {
        if (!Directory.Exists(profilesDirectory))
            return [];

        return Directory
            .EnumerateFiles(profilesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _reloadTimer.Dispose();
        _watcher?.Dispose();
        _disposed = true;
    }

    private void ConfigureWatcher(string profilePath)
    {
        _watcher?.Dispose();

        string? directory = Path.GetDirectoryName(profilePath);
        string  fileName  = Path.GetFileName(profilePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            return;

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter        = NotifyFilters.LastWrite | NotifyFilters.Size
                                | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += ScheduleReload;
        _watcher.Created += ScheduleReload;
        _watcher.Renamed += ScheduleReload;
    }

    private void ScheduleReload(object sender, FileSystemEventArgs e)
    {
        // O FileSystemWatcher pode disparar múltiplos eventos Changed seguidos
        // para a mesma escrita (comportamento documentado do Windows).
        // O debounce de 150 ms colapsa a rajada em um único reload.
        _reloadTimer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
    }

    private void ReloadFromTimer(object? state)
    {
        string? path;
        int     attempts;

        lock (_sync)
        {
            path     = _activeProfilePath;
            attempts = _reloadAttempts;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        // Proteção contra loop de reloads em arquivo corrompido ou travado.
        // Se MaxReloadAttempts forem esgotadas em sequência, o reload é abandonado.
        // O contador é zerado quando um LoadProfile() manual bem-sucedido ocorre.
        if (attempts >= MaxReloadAttempts)
            return;

        try
        {
            MacroConfig config = _loader.Load(path);

            lock (_sync) { _reloadAttempts = 0; }

            ProfileChanged?.Invoke(this, new ProfileChangedEventArgs(path, config));
        }
        catch (IOException)
        {
            // Arquivo ainda sendo escrito — reagenda e incrementa o contador
            lock (_sync) { _reloadAttempts++; }
            _reloadTimer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
        }
        catch (UnauthorizedAccessException)
        {
            lock (_sync) { _reloadAttempts++; }
            _reloadTimer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
