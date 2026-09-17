using System.Runtime.InteropServices;
internal static class NativeProbe
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    public static object ReadTcpOwners()
    {
        uint size = 0;
        uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
        for (int attempt = 0; attempt < 3 && result == 122; attempt++)
        {
            if (size > 16 * 1024 * 1024) break;
            var buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, 2, 5, 0);
                if (result != 0) continue;
                int count = Marshal.ReadInt32(buffer);
                if (count < 0 || 4L + count * 24L > size) throw new InvalidDataException("Invalid TCP table size.");
                var owners = new Dictionary<int, int>();
                for (int i = 0; i < count; i++)
                {
                    int pid = Marshal.ReadInt32(buffer, 4 + i * 24 + 20);
                    owners[pid] = owners.GetValueOrDefault(pid) + 1;
                }
                return new { Kind = "ApplicationConnections", Timestamp = DateTimeOffset.UtcNow, Source = "GetExtendedTcpTable / IPv4 OWNER_PID", Availability = "Measured", Unit = "connections", Owners = owners,
                    ByteAccounting = "Unavailable", Detail = "Connection ownership is not byte accounting. IPv6 and UDP not investigated; PID alone is not stable identity." };
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return new { Kind = "ApplicationConnections", Timestamp = DateTimeOffset.UtcNow, Source = "GetExtendedTcpTable", Availability = result == 5 ? "PermissionDenied" : "Error", NativeError = result };
    }
    public static object ReadWifiCapability()
    {
        uint result = WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
        if (result != 0) return WifiResult(result, null);
        try
        {
            result = WlanEnumInterfaces(handle, IntPtr.Zero, out var list);
            try { return WifiResult(result, result == 0 ? Marshal.ReadInt32(list) : null); }
            finally { if (list != IntPtr.Zero) WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }
    private static object WifiResult(uint error, int? count) => new { Kind = "WifiCapability", Timestamp = DateTimeOffset.UtcNow,
        Source = "WlanOpenHandle / WlanEnumInterfaces", Unit = "interfaces", Availability = error == 0 ? "Measured" : error == 5 ? "PermissionDenied" : "Error",
        InterfaceCount = count, NativeError = error, Detail = "Capability enumeration only. SSID/signal/channel queries pending; no location permission requested." };
}
