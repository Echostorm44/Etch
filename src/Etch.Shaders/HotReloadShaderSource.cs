namespace Etch.Shaders;

#if DEBUG && ETCH_HOT_RELOAD

public sealed class HotReloadShaderSource : IShaderSource, IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly EmbeddedShaderSource _fallback = new();
    private readonly string _rootDirectory;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _lastModified = new();
    private readonly System.Threading.Timer _debounceTimer;
    private string? _pendingShader;
    private readonly object _lock = new();
    private bool _disposed;

    public HotReloadShaderSource(string rootDirectory = "shaders/")
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _watcher = new FileSystemWatcher(_rootDirectory, "*.wgsl")
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;
        _debounceTimer = new System.Threading.Timer(DebounceCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    public ReadOnlySpan<byte> GetSource(string name)
    {
        string filePath = GetFilePath(name);
        if (File.Exists(filePath))
        {
            try
            {
                return File.ReadAllBytes(filePath).AsSpan();
            }
            catch
            {
            }
        }
        return _fallback.GetSource(name);
    }

    public bool TryGetVersion(string name, out ulong version)
    {
        string filePath = GetFilePath(name);
        if (File.Exists(filePath))
        {
            try
            {
                var info = new FileInfo(filePath);
                version = (ulong)info.LastWriteTimeUtc.Ticks;
                return true;
            }
            catch
            {
            }
        }
        return _fallback.TryGetVersion(name, out version);
    }

    public event EventHandler<string>? Changed;

    private string GetFilePath(string name)
    {
        string slug = name.Replace("_", "-").Replace("\\", "/");
        return Path.Combine(_rootDirectory, slug + ".wgsl");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _pendingShader = Path.GetFileNameWithoutExtension(e.FullPath);
            _debounceTimer.Change(100, Timeout.Infinite);
        }
    }

    private void DebounceCallback(object? state)
    {
        string? shader;
        lock (_lock)
        {
            shader = _pendingShader;
            _pendingShader = null;
        }

        if (shader != null)
        {
            Changed?.Invoke(this, shader);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _debounceTimer.Dispose();
        }
    }
}

#else

public sealed class HotReloadShaderSource : IShaderSource, IDisposable
{
    private readonly EmbeddedShaderSource _fallback = new();

    public HotReloadShaderSource(string rootDirectory = "shaders/")
    {
    }

    public ReadOnlySpan<byte> GetSource(string name) => _fallback.GetSource(name);

    public bool TryGetVersion(string name, out ulong version) => _fallback.TryGetVersion(name, out version);

    public event EventHandler<string>? Changed;

    public void Dispose()
    {
    }
}

#endif