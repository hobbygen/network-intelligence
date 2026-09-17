using System.Runtime.InteropServices;
using System.Text;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Infrastructure;

internal static class WifiReader
{
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, int opcode, IntPtr reserved, out uint size, out IntPtr data, out int valueType);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr pointer);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    public static IReadOnlyList<WifiSnapshot> Read(bool ssid)
    {
        var rows = new List<WifiSnapshot>();
        uint result = WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
        var now = DateTimeOffset.UtcNow;
        if (result != 0) return [Failure("", result, now)];
        try
        {
            result = WlanEnumInterfaces(handle, IntPtr.Zero, out var list);
            if (result != 0) return [Failure("", result, now)];
            try
            {
                var count = Marshal.ReadInt32(list);
                if (count is < 0 or > 256) return [Failure("", 13, now)];
                for (int i = 0; i < count; i++)
                {
                    var start = IntPtr.Add(list, 8 + i * 532);
                    var id = Marshal.PtrToStructure<Guid>(start);
                    var state = Marshal.ReadInt32(start, 528);
                    if (state != 1)
                    { rows.Add(new(id.ToString("B"), "Disconnected", null, null, null, null, null, Availability.Unavailable, "No connected Wi-Fi association.", now)); continue; }
                    result = WlanQueryInterface(handle, ref id, 7, IntPtr.Zero, out var size, out var data, out _);
                    try
                    {
                        if (result != 0 || size < 604) { rows.Add(Failure(id.ToString("B"), result == 0 ? 13u : result, now)); continue; }
                        int ssidLength = Marshal.ReadInt32(data, 520);
                        string? network = null;
                        if (ssid && ssidLength is >= 0 and <= 32)
                        { var bytes = new byte[ssidLength]; Marshal.Copy(IntPtr.Add(data, 524), bytes, 0, bytes.Length); network = Encoding.UTF8.GetString(bytes); }
                        rows.Add(new(id.ToString("B"), "Connected", network, (uint)Marshal.ReadInt32(data, 576),
                            (uint)Marshal.ReadInt32(data, 580), (uint)Marshal.ReadInt32(data, 584),
                            "DOT11 auth " + Marshal.ReadInt32(data, 596), Availability.Measured,
                            "Native WLAN current connection. Signal is driver quality %, not dBm. Channel/frequency unavailable.", now));
                    }
                    finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
                }
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
        return rows;
    }
    private static WifiSnapshot Failure(string id, uint code, DateTimeOffset timestamp) => new(id, "Unavailable", null, null, null, null, null,
        code == 5 ? Availability.PermissionDenied : Availability.Error,
        code == 5 ? "Windows denied Wi-Fi details. Review Windows location privacy permissions; no permission is changed automatically." : $"Native WLAN error {code}.", timestamp);
}
