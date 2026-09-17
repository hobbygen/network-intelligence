using System.Diagnostics;
using System.Net.NetworkInformation;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Infrastructure;

public sealed class NetworkCollector : INetworkCollector
{
    private readonly Dictionary<string, CounterSample> previous = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private IReadOnlyList<WifiSnapshot> wifi = [];
    private IReadOnlyList<ProcessConnections> applications = [];
    public void Reset() { previous.Clear(); wifi = []; applications = []; }
    public MonitoringSnapshot Collect(AppSettings settings, bool includeDetails)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Network collection requires Windows.");
        var now = DateTimeOffset.UtcNow;
        var adapters = new List<AdapterSnapshot>();
        try
        {
            var all = NetworkInterface.GetAllNetworkInterfaces();
            var ids = all.Select(a => a.Id).ToHashSet();
            foreach (var removed in previous.Keys.Where(k => !ids.Contains(k)).ToArray()) previous.Remove(removed);
            foreach (var adapter in all.Where(a => a.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                try
                {
                    var stats = adapter.GetIPStatistics();
                    var ip = adapter.GetIPProperties();
                    var current = new CounterSample(adapter.Id, stats.BytesReceived, stats.BytesSent, clock.Elapsed.TotalSeconds);
                    previous.TryGetValue(adapter.Id, out var old);
                    var rate = RateCalculator.Calculate(old, current, now);
                    previous[adapter.Id] = current;
                    adapters.Add(new(adapter.Id, adapter.Name, adapter.Description, adapter.NetworkInterfaceType.ToString(),
                        adapter.OperationalStatus.ToString(), adapter.Speed > 0 ? adapter.Speed : null,
                        stats.BytesReceived, stats.BytesSent, stats.IncomingPacketsWithErrors + stats.OutgoingPacketsWithErrors,
                        stats.IncomingPacketsDiscarded + stats.OutgoingPacketsDiscarded, rate.Download.Value, rate.Upload.Value,
                        rate.Download.Value.HasValue && old is not null ? current.Received - old.Received : null,
                        rate.Upload.Value.HasValue && old is not null ? current.Sent - old.Sent : null,
                        old is null ? 0 : Math.Max(0, current.MonotonicSeconds - old.MonotonicSeconds),
                        ip.GatewayAddresses.Count > 0,
                        settings.CollectAddresses ? ip.UnicastAddresses.Select(a => a.Address.ToString()).ToArray() : [],
                        settings.CollectAddresses ? ip.DnsAddresses.Select(a => a.ToString()).ToArray() : [],
                        settings.CollectAddresses ? ip.GatewayAddresses.Select(a => a.Address.ToString()).ToArray() : [],
                        now, Availability.Measured, rate.Download.Detail));
                }
                catch (Exception ex) when (ex is NetworkInformationException or UnauthorizedAccessException)
                {
                    previous.Remove(adapter.Id);
                    adapters.Add(new(adapter.Id, adapter.Name, adapter.Description, adapter.NetworkInterfaceType.ToString(), "Unknown",
                        null, null, null, null, null, null, null, null, null, 0, false, [], [], [], now,
                        ex is UnauthorizedAccessException ? Availability.PermissionDenied : Availability.Error, ex.GetType().Name));
                }
            }
            if (includeDetails && OperatingSystem.IsWindows())
            {
                wifi = WifiReader.Read(settings.CollectSsid);
                applications = ConnectionReader.Read(settings.CollectProcessNames);
            }
            return new(now, adapters, wifi, applications, null);
        }
        catch (NetworkInformationException ex) { return new(now, adapters, wifi, applications, "Adapter discovery failed: " + ex.ErrorCode); }
    }
}
