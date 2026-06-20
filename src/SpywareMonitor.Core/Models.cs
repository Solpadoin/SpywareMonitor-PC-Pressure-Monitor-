using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpywareMonitor.Core;

public sealed record MonitorSettings
{
    public int SampleIntervalMs { get; init; } = 1000;
    public int RetentionDays { get; init; } = 7;
    public int MaxProcesses { get; init; } = 300;
    public double CpuAlertPercent { get; init; } = 35;
    public long MemoryAlertBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public long IoAlertBytesPerSecond { get; init; } = 80L * 1024 * 1024;
    public bool CaptureCommandLines { get; init; } = true;
    public bool CaptureNetworkEndpoints { get; init; } = true;
    public bool PersistHistory { get; init; } = true;
}

public sealed record NetworkEndpoint(
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State);

public sealed record ProcessSnapshot
{
    public int ProcessId { get; init; }
    public int? ParentProcessId { get; init; }
    public string Name { get; init; } = "";
    public string? Path { get; init; }
    public string? CommandLine { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public double CpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public long ReadBytesPerSecond { get; init; }
    public long WriteBytesPerSecond { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public bool? Responding { get; init; }
    public int WindowCount { get; init; }
    public IReadOnlyList<NetworkEndpoint> NetworkEndpoints { get; init; } = Array.Empty<NetworkEndpoint>();
    public double PressureScore { get; init; }
}

public sealed record MonitorAlert(
    DateTimeOffset Timestamp,
    string Severity,
    string Title,
    string Details,
    int? ProcessId = null);

public sealed record SystemSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public double CpuPercent { get; init; }
    public double MemoryPercent { get; init; }
    public long UsedMemoryBytes { get; init; }
    public long TotalMemoryBytes { get; init; }
    public long TotalReadBytesPerSecond { get; init; }
    public long TotalWriteBytesPerSecond { get; init; }
    public int ProcessCount { get; init; }
    public int ThreadCount { get; init; }
    public IReadOnlyList<ProcessSnapshot> Processes { get; init; } = Array.Empty<ProcessSnapshot>();
    public IReadOnlyList<MonitorAlert> Alerts { get; init; } = Array.Empty<MonitorAlert>();
}

public sealed record ProcessEvent(
    DateTimeOffset Timestamp,
    string Kind,
    int ProcessId,
    string Name,
    string? Path,
    string? CommandLine,
    int? ParentProcessId);

public sealed record ServiceStatus(
    bool Running,
    DateTimeOffset StartedAt,
    string Version,
    MonitorSettings Settings,
    string DataDirectory,
    long StoredSnapshotCount,
    string? Error = null);

public sealed record PipeRequest(string Command, int Limit = 120, MonitorSettings? Settings = null);
public sealed record PipeResponse<T>(bool Success, T? Data, string? Error = null);

public static class MonitorJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}
