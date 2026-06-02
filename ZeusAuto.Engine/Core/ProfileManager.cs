namespace ZeusAuto.Engine.Core;

public sealed class ProfileManager : IDisposable
{
    private readonly object _sync = new();
    private readonly JsonConfigLoader _loader;
    private readonly Timer _reloadTimer;
    private FileSystemWatcher? _watcher;
    private string? _activeProfilePath;
    private bool _disposed;

    public ProfileManager(JsonConfigLoader? loader = null)
    {
        _loader = loader ?? new JsonConfigLoader();
        _reloadTimer = new Timer(ReloadFromTimer);
    }

    public event EventHandler<ProfileChangedEventArgs>? ProfileChanged;

    public string? ActiveProfilePath
    {
        get
        {
            lock (_sync)
            {
                return _activeProfilePath;
            }
        }
    }

    public MacroConfig LoadProfile(string profilePath)
    {
        ThrowIfDisposed();

        MacroConfig config = _loader.Load(profilePath);
        lock (_sync)
        {
            _activeProfilePath = Path.GetFullPath(profilePath);
            ConfigureWatcher(_activeProfilePath);
        }

        ProfileChanged?.Invoke(this, new ProfileChangedEventArgs(_activeProfilePath, config));
        return config;
    }

    public MacroConfig SwitchProfile(string profilePath)
    {
        return LoadProfile(profilePath);
    }

    public IReadOnlyList<string> ListProfiles(string profilesDirectory)
    {
        if (!Directory.Exists(profilesDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(profilesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _reloadTimer.Dispose();
        _watcher?.Dispose();
        _disposed = true;
    }

    private void ConfigureWatcher(string profilePath)
    {
        _watcher?.Dispose();

        string? directory = Path.GetDirectoryName(profilePath);
        string fileName = Path.GetFileName(profilePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += ScheduleReload;
        _watcher.Created += ScheduleReload;
        _watcher.Renamed += ScheduleReload;
    }

    private void ScheduleReload(object sender, FileSystemEventArgs e)
    {
        _reloadTimer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
    }

    private void ReloadFromTimer(object? state)
    {
        string? path;
        lock (_sync)
        {
            path = _activeProfilePath;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            MacroConfig config = _loader.Load(path);
            ProfileChanged?.Invoke(this, new ProfileChangedEventArgs(path, config));
        }
        catch (IOException)
        {
            _reloadTimer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
        }
        catch (UnauthorizedAccessException)
        {
            _reloadTimer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
