using System.Net;
using System.Runtime.InteropServices;

namespace SpywareMonitor.Service;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime { public uint Low; public uint High; public ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatus { public uint Length; public uint Load; public ulong TotalPhys; public ulong AvailPhys; public ulong TotalPageFile; public ulong AvailPageFile; public ulong TotalVirtual; public ulong AvailVirtual; public ulong AvailExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
    [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int ipVersion, int tableClass, uint reserved);

    internal static Dictionary<int, List<SpywareMonitor.Core.NetworkEndpoint>> GetTcp4Endpoints()
    {
        var result = new Dictionary<int, List<SpywareMonitor.Core.NetworkEndpoint>>();
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, 5, 0);
        if (size <= 0) return result;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, true, 2, 5, 0) != 0) return result;
            var count = Marshal.ReadInt32(buffer);
            var row = IntPtr.Add(buffer, 4);
            for (var i = 0; i < count; i++, row = IntPtr.Add(row, 24))
            {
                var state = Marshal.ReadInt32(row);
                var localAddr = Marshal.ReadInt32(row, 4);
                var localPort = Port(Marshal.ReadInt32(row, 8));
                var remoteAddr = Marshal.ReadInt32(row, 12);
                var remotePort = Port(Marshal.ReadInt32(row, 16));
                var pid = Marshal.ReadInt32(row, 20);
                if (!result.TryGetValue(pid, out var list)) result[pid] = list = new();
                list.Add(new("TCP", new IPAddress((uint)localAddr).ToString(), localPort, new IPAddress((uint)remoteAddr).ToString(), remotePort, TcpState(state)));
            }
        }
        catch { }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    private static int Port(int value) => (ushort)IPAddress.NetworkToHostOrder((short)(value & 0xffff));
    private static string TcpState(int state) => state switch { 2 => "LISTEN", 5 => "ESTABLISHED", 6 => "FIN_WAIT1", 7 => "FIN_WAIT2", 8 => "CLOSE_WAIT", 11 => "TIME_WAIT", _ => state.ToString() };
}
