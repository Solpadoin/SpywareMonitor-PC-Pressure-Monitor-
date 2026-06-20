using SpywareMonitor.Core;

namespace SpywareMonitor.Service;

public sealed class MonitorState
{
    private readonly object _gate = new();
    private SystemSnapshot? _latest;
    private MonitorSettings _settings = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    public SystemSnapshot? Latest { get { lock (_gate) return _latest; } }
    public MonitorSettings Settings { get { lock (_gate) return _settings; } }
    public DateTimeOffset StartedAt => _startedAt;
    public void SetLatest(SystemSnapshot value) { lock (_gate) _latest = value; }
    public void SetSettings(MonitorSettings value) { lock (_gate) _settings = value; }
}
