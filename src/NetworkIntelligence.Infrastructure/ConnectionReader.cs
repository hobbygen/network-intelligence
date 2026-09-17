using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Infrastructure;

internal static class ConnectionReader
{
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr buffer, ref uint size, bool order, uint family, uint tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedUdpTable(IntPtr buffer, ref uint size, bool order, uint family, uint tableClass, uint reserved);
    public static IReadOnlyList<ProcessConnections> Read(bool names)
    {
        var counts = new Dictionary<int, (int Tcp, int Udp)>();
        var errors = new List<uint>();
        ReadTable(true, 2, 24, 20); ReadTable(true, 23, 56, 52);
        ReadTable(false, 2, 12, 8); ReadTable(false, 23, 28, 24);
        void ReadTable(bool tcp, uint family, int rowSize, int pidOffset)
        {
            uint size = 0;
            uint Query(IntPtr pointer) => tcp ? GetExtendedTcpTable(pointer, ref size, false, family, 5, 0) : GetExtendedUdpTable(pointer, ref size, false, family, 1, 0);
            var result = Query(IntPtr.Zero);
            for (int attempt = 0; result == 122 && attempt < 3; attempt++)
            {
                if (size > 16 * 1024 * 1024) { errors.Add(8); return; }
                var allocated = size;
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    result = Query(buffer);
                    if (result != 0) continue;
                    int count = Marshal.ReadInt32(buffer);
                    if (count < 0 || 4L + (long)count * rowSize > allocated) { errors.Add(13); return; }
                    for (int i = 0; i < count; i++)
                    {
                        int pid = Marshal.ReadInt32(buffer, 4 + i * rowSize + pidOffset);
                        var value = counts.GetValueOrDefault(pid);
                        counts[pid] = tcp ? (value.Tcp + 1, value.Udp) : (value.Tcp, value.Udp + 1);
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            if (result != 0) errors.Add(result);
        }
        var rows = new List<ProcessConnections>();
        foreach (var pair in counts)
        {
            var name = pair.Key == 0 ? "Unattributed / expired sockets" : $"Process {pair.Key}";
            DateTimeOffset? started = null;
            var state = Availability.Measured;
            var detail = errors.Count > 0 ? "Partial connection tables; bytes unavailable." : "IP Helper TCP/UDP IPv4/IPv6 ownership. Bytes unavailable.";
            try
            {
                using var process = Process.GetProcessById(pair.Key);
                if (names) name = process.ProcessName;
                started = process.StartTime.ToUniversalTime();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            { state = ex is Win32Exception ? Availability.PermissionDenied : Availability.Unavailable; detail += " Process identity unavailable; PID is not a stable app identity."; }
            rows.Add(new(pair.Key, started, name, pair.Value.Tcp, pair.Value.Udp, state, detail));
        }
        if (errors.Count > 0) rows.Add(new(-1, null, "Some connection tables unavailable", 0, 0, Availability.Error, string.Join(",", errors)));
        return rows.OrderByDescending(r => r.TcpConnections + r.UdpEndpoints).ToArray();
    }
}
