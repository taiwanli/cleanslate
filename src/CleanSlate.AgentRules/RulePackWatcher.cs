namespace CleanSlate.AgentRules;

/// <summary>
/// Watches rule pack files and reloads on change (hot update).
/// </summary>
public sealed class RulePackWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly string _filePath;
    private readonly AgentRuleLoader _loader = new();
    private readonly object _gate = new();
    private AgentRuleLoadResult? _current;

    public event Action<AgentRuleLoadResult>? Reloaded;

    public RulePackWatcher(string filePath)
    {
        _filePath = filePath;
        ReloadNow();

        var dir = Path.GetDirectoryName(filePath);
        var name = Path.GetFileName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => ReloadNow();
            _watcher.Created += (_, _) => ReloadNow();
            _watcher.Renamed += (_, _) => ReloadNow();
        }
    }

    public AgentRuleLoadResult? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void ReloadNow()
    {
        var result = _loader.LoadFile(_filePath);
        lock (_gate)
        {
            _current = result;
        }

        Reloaded?.Invoke(result);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
