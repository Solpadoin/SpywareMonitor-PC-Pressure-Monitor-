using System.Diagnostics;
using System.Management;
using SpywareMonitor.Core;

namespace SpywareMonitor.Service;

public sealed class ProcessSampler
{
    private sealed record Previous(TimeSpan Cpu, ulong Read, ulong Write, DateTimeOffset At);
    private sealed record Metadata(string? Path, string? CommandLine, int? ParentId);
    private readonly Dictionary<int, Previous> _previous = new();
    private readonly Dictionary<int, Metadata> _metadata = new();
    private HashSet<int> _known = new();
    private DateTimeOffset _lastMetadataRefresh = DateTimeOffset.MinValue;
    private ulong _oldIdle, _oldKernel, _oldUser;

    public (SystemSnapshot Snapshot, IReadOnlyList<ProcessEvent> Events) Sample(MonitorSettings settings)
    {
        var now = DateTimeOffset.Now;
        var endpoints = settings.CaptureNetworkEndpoints ? NativeMethods.GetTcp4Endpoints() : new();
        var processes = new List<ProcessSnapshot>();
        var current = new HashSet<int>();
        var events = new List<ProcessEvent>();

        if (now - _lastMetadataRefresh > TimeSpan.FromSeconds(10)) RefreshMetadata(settings.CaptureCommandLines, now);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var pid = process.Id;
                    current.Add(pid);
                    if (!_metadata.TryGetValue(pid, out var meta)) meta = new(null, null, null);
                    var cpu = process.TotalProcessorTime;
                    NativeMethods.GetProcessIoCounters(process.Handle, out var io);
                    var cpuPercent = 0d; var read = 0L; var write = 0L;
                    if (_previous.TryGetValue(pid, out var old))
                    {
                        var seconds = Math.Max(.001, (now - old.At).TotalSeconds);
                        cpuPercent = Math.Clamp((cpu - old.Cpu).TotalSeconds / seconds / Environment.ProcessorCount * 100, 0, 100);
                        read = io.ReadTransferCount >= old.Read ? (long)((io.ReadTransferCount - old.Read) / seconds) : 0;
                        write = io.WriteTransferCount >= old.Write ? (long)((io.WriteTransferCount - old.Write) / seconds) : 0;
                    }
                    _previous[pid] = new(cpu, io.ReadTransferCount, io.WriteTransferCount, now);
                    var eps = endpoints.TryGetValue(pid, out var found) ? found : new List<NetworkEndpoint>();
                    bool? responding = process.MainWindowHandle == IntPtr.Zero ? null : process.Responding;
                    var score = Math.Min(100, cpuPercent * 1.4 + process.WorkingSet64 / 1024d / 1024 / 80 + (read + write) / 1024d / 1024 / 3);
                    processes.Add(new()
                    {
                        ProcessId = pid, ParentProcessId = meta.ParentId, Name = process.ProcessName,
                        Path = meta.Path, CommandLine = meta.CommandLine, StartedAt = Try(() => new DateTimeOffset(process.StartTime)),
                        CpuPercent = cpuPercent, WorkingSetBytes = process.WorkingSet64, PrivateMemoryBytes = process.PrivateMemorySize64,
                        ReadBytesPerSecond = read, WriteBytesPerSecond = write, ThreadCount = process.Threads.Count,
                        HandleCount = process.HandleCount, Responding = responding, WindowCount = process.MainWindowHandle == IntPtr.Zero ? 0 : 1,
                        NetworkEndpoints = eps, PressureScore = score
                    });
                    if (!_known.Contains(pid)) events.Add(new(now, "started", pid, process.ProcessName, meta.Path, meta.CommandLine, meta.ParentId));
                }
                catch { }
            }
        }

        foreach (var pid in _known.Except(current))
        {
            var meta = _metadata.GetValueOrDefault(pid);
            events.Add(new(now, "stopped", pid, "process", meta?.Path, meta?.CommandLine, meta?.ParentId));
            _previous.Remove(pid); _metadata.Remove(pid);
        }
        _known = current;
        var ordered = processes.OrderByDescending(x => x.PressureScore).Take(settings.MaxProcesses).ToArray();
        var alerts = BuildAlerts(ordered, settings, now);
        var memory = new NativeMethods.MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MemoryStatus>() };
        NativeMethods.GlobalMemoryStatusEx(ref memory);
        var snapshot = new SystemSnapshot
        {
            Timestamp = now, CpuPercent = GetSystemCpu(), MemoryPercent = memory.Load, UsedMemoryBytes = (long)(memory.TotalPhys - memory.AvailPhys),
            TotalMemoryBytes = (long)memory.TotalPhys, TotalReadBytesPerSecond = processes.Sum(x => x.ReadBytesPerSecond),
            TotalWriteBytesPerSecond = processes.Sum(x => x.WriteBytesPerSecond), ProcessCount = processes.Count,
            ThreadCount = processes.Sum(x => x.ThreadCount), Processes = ordered, Alerts = alerts
        };
        return (snapshot, events);
    }

    private double GetSystemCpu()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        if (_oldKernel == 0 && _oldUser == 0) { _oldIdle = idle.Value; _oldKernel = kernel.Value; _oldUser = user.Value; return 0; }
        var idleDelta = idle.Value - _oldIdle; var totalDelta = kernel.Value - _oldKernel + user.Value - _oldUser;
        _oldIdle = idle.Value; _oldKernel = kernel.Value; _oldUser = user.Value;
        return totalDelta == 0 ? 0 : Math.Clamp((1 - idleDelta / (double)totalDelta) * 100, 0, 100);
    }

    private void RefreshMetadata(bool commandLine, DateTimeOffset now)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId,ExecutablePath,CommandLine,ParentProcessId FROM Win32_Process");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                var pid = Convert.ToInt32(item["ProcessId"] ?? 0);
                _metadata[pid] = new(item["ExecutablePath"]?.ToString(), commandLine ? item["CommandLine"]?.ToString() : null, Convert.ToInt32(item["ParentProcessId"] ?? 0));
            }
        }
        catch { }
        _lastMetadataRefresh = now;
    }

    private static IReadOnlyList<MonitorAlert> BuildAlerts(IEnumerable<ProcessSnapshot> items, MonitorSettings settings, DateTimeOffset now)
    {
        var result = new List<MonitorAlert>();
        foreach (var p in items.Take(30))
        {
            if (p.CpuPercent >= settings.CpuAlertPercent) result.Add(new(now, "warning", $"Высокая загрузка CPU: {p.Name}", $"{p.CpuPercent:F1}% CPU", p.ProcessId));
            if (p.PrivateMemoryBytes >= settings.MemoryAlertBytes) result.Add(new(now, "warning", $"Много памяти: {p.Name}", $"{p.PrivateMemoryBytes / 1024d / 1024 / 1024:F2} ГБ private memory", p.ProcessId));
            if (p.ReadBytesPerSecond + p.WriteBytesPerSecond >= settings.IoAlertBytesPerSecond) result.Add(new(now, "warning", $"Интенсивный диск: {p.Name}", $"{(p.ReadBytesPerSecond + p.WriteBytesPerSecond) / 1024d / 1024:F1} МБ/с", p.ProcessId));
            if (p.Responding == false) result.Add(new(now, "critical", $"Окно не отвечает: {p.Name}", "Windows сообщает, что главное окно зависло", p.ProcessId));
        }
        return result;
    }

    private static T? Try<T>(Func<T> action) where T : struct { try { return action(); } catch { return null; } }
}
