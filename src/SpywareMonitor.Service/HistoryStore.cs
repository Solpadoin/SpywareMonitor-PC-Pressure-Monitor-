using System.Collections.Concurrent;
using System.Text.Json;
using SpywareMonitor.Core;

namespace SpywareMonitor.Service;

public sealed class HistoryStore
{
    private const int MemoryCapacity = 300;
    private readonly ConcurrentQueue<SystemSnapshot> _snapshots = new();
    private readonly ConcurrentQueue<ProcessEvent> _events = new();
    private readonly SettingsStore _settings;
    private long _stored;
    private DateTimeOffset _lastPersisted = DateTimeOffset.MinValue;
    public long StoredCount => Interlocked.Read(ref _stored);

    public HistoryStore(SettingsStore settings) => _settings = settings;

    public async Task AddAsync(SystemSnapshot snapshot, MonitorSettings settings, CancellationToken ct)
    {
        var memorySnapshot = snapshot with
        {
            Processes = snapshot.Processes.Take(25).Select(p => p with
            {
                CommandLine = null,
                NetworkEndpoints = Array.Empty<NetworkEndpoint>()
            }).ToArray(),
            Alerts = snapshot.Alerts.Take(20).ToArray()
        };
        _snapshots.Enqueue(memorySnapshot);
        while (_snapshots.Count > MemoryCapacity) _snapshots.TryDequeue(out _);
        Interlocked.Increment(ref _stored);
        if (!settings.PersistHistory || snapshot.Timestamp - _lastPersisted < TimeSpan.FromSeconds(30)) return;
        _lastPersisted = snapshot.Timestamp;
        var persisted = new
        {
            snapshot.Timestamp,
            snapshot.SnapshotTime,
            snapshot.CpuPercent,
            snapshot.MemoryPercent,
            snapshot.UsedMemoryBytes,
            snapshot.TotalMemoryBytes,
            snapshot.TotalReadBytesPerSecond,
            snapshot.TotalWriteBytesPerSecond,
            snapshot.ProcessCount,
            snapshot.ThreadCount,
            TopProcesses = snapshot.Processes.Take(15).Select(p => new
            {
                p.ProcessId,
                p.Name,
                p.CpuPercent,
                p.WorkingSetBytes,
                p.PrivateMemoryBytes,
                p.ReadBytesPerSecond,
                p.WriteBytesPerSecond,
                p.ThreadCount,
                p.HandleCount,
                p.Responding,
                p.PressureScore
            }),
            Alerts = snapshot.Alerts.Take(10)
        };
        Directory.CreateDirectory(settings.LogDirectory);
        var path = Path.Combine(settings.LogDirectory, $"snapshots-{snapshot.Timestamp:yyyy-MM-dd}.jsonl");
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
        var files = Directory.EnumerateFiles(logDirectory, "metrics-*.jsonl").Concat(Directory.EnumerateFiles(logDirectory, "snapshots-*.jsonl"));
        foreach (var file in files)
            if (File.GetLastWriteTimeUtc(file) < cutoff) try { File.Delete(file); } catch { }
    }
}
