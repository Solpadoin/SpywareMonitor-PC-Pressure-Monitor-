using System.Collections.Concurrent;
using System.Text.Json;
using SpywareMonitor.Core;

namespace SpywareMonitor.Service;

public sealed class HistoryStore
{
    private const int MemoryCapacity = 3600;
    private readonly ConcurrentQueue<SystemSnapshot> _snapshots = new();
    private readonly ConcurrentQueue<ProcessEvent> _events = new();
    private readonly SettingsStore _settings;
    private long _stored;
    private DateTimeOffset _lastPersisted = DateTimeOffset.MinValue;
    public long StoredCount => Interlocked.Read(ref _stored);

    public HistoryStore(SettingsStore settings) => _settings = settings;

    public async Task AddAsync(SystemSnapshot snapshot, MonitorSettings settings, CancellationToken ct)
    {
        _snapshots.Enqueue(snapshot);
        while (_snapshots.Count > MemoryCapacity) _snapshots.TryDequeue(out _);
        Interlocked.Increment(ref _stored);
        if (!settings.PersistHistory || snapshot.Timestamp - _lastPersisted < TimeSpan.FromSeconds(10)) return;
        _lastPersisted = snapshot.Timestamp;
        var persisted = snapshot with { Processes = snapshot.Processes.Take(100).ToArray() };
        Directory.CreateDirectory(settings.LogDirectory);
        var path = Path.Combine(settings.LogDirectory, $"metrics-{snapshot.Timestamp:yyyy-MM-dd}.jsonl");
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(persisted, MonitorJson.Options) + Environment.NewLine, ct);
    }

    public void AddEvent(ProcessEvent item)
    {
        _events.Enqueue(item);
        while (_events.Count > 2000) _events.TryDequeue(out _);
    }

    public IReadOnlyList<SystemSnapshot> GetRecent(int limit) => _snapshots.Reverse().Take(Math.Clamp(limit, 1, 3600)).Reverse().ToArray();
    public IReadOnlyList<ProcessEvent> GetEvents(int limit) => _events.Reverse().Take(Math.Clamp(limit, 1, 1000)).ToArray();

    public void Cleanup(int retentionDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(retentionDays, 1, 90));
        var logDirectory = _settings.Load().LogDirectory;
        if (!Directory.Exists(logDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(logDirectory, "metrics-*.jsonl"))
            if (File.GetLastWriteTimeUtc(file) < cutoff) try { File.Delete(file); } catch { }
    }
}
