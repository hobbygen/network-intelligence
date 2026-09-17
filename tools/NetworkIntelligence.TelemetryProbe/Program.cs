using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetworkIntelligence.Domain;

if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Windows required."); return 1; }
if (args.Contains("--help"))
{
    Console.WriteLine("--samples 2..60 (default 5) --target hostname-or-IP (optional) --etw-seconds 1..60 (optional)\nPassive by default. --target resolves DNS and sends five ICMP probes. --etw-seconds attempts a kernel network ETW session.\nJSON lines to stdout. No IP addresses, MACs, SSIDs, executable paths or payloads are saved.");
    return 0;
}
int samples = 5;
string? target = null;
int etwSeconds = 0;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--samples" && i + 1 < args.Length && int.TryParse(args[++i], out var count) && count is >= 2 and <= 60) samples = count;
    else if (args[i] == "--target" && i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1])) target = args[++i];
    else if (args[i] == "--etw-seconds" && i + 1 < args.Length && int.TryParse(args[++i], out var etw) && etw is >= 1 and <= 60) etwSeconds = etw;
    else { Console.Error.WriteLine("Invalid arguments. Use --help."); return 2; }
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value, options));
var clock = Stopwatch.StartNew();
using var process = Process.GetCurrentProcess();
var cpuStart = process.TotalProcessorTime;
var previous = new Dictionary<string, CounterSample>();
try
{
    for (int iteration = 0; iteration < samples; iteration++)
    {
        cancellation.Token.ThrowIfCancellationRequested();
        var present = new HashSet<string>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            present.Add(adapter.Id);
            var now = DateTimeOffset.UtcNow;
            try
            {
                var stats = adapter.GetIPStatistics();
                var properties = adapter.GetIPProperties();
                var current = new CounterSample(adapter.Id, stats.BytesReceived, stats.BytesSent, clock.Elapsed.TotalSeconds);
                previous.TryGetValue(adapter.Id, out var last);
                var rate = RateCalculator.Calculate(last, current, now);
                previous[adapter.Id] = current;
                Measurement<long> Counter(long value, string unit) => new(value, unit, "NetworkInterface.GetIPStatistics", now, Availability.Measured);
                Emit(new { Kind = "Adapter", Timestamp = now, Source = "System.Net.NetworkInformation", Availability = "Measured",
                    AdapterId = adapter.Id, Type = adapter.NetworkInterfaceType.ToString(), State = adapter.OperationalStatus.ToString(),
                    LinkSpeed = new Measurement<long>(adapter.Speed > 0 ? adapter.Speed : null, "bits/s", "NetworkInterface.Speed", now,
                        adapter.Speed > 0 ? Availability.Measured : Availability.Unavailable),
                    HasIp = properties.UnicastAddresses.Count > 0, HasGateway = properties.GatewayAddresses.Count > 0, HasDns = properties.DnsAddresses.Count > 0,
                    Received = Counter(stats.BytesReceived, "bytes"), Sent = Counter(stats.BytesSent, "bytes"),
                    ReceiveErrors = Counter(stats.IncomingPacketsWithErrors, "packets"), SendErrors = Counter(stats.OutgoingPacketsWithErrors, "packets"),
                    ReceiveDiscards = Counter(stats.IncomingPacketsDiscarded, "packets"), SendDiscards = Counter(stats.OutgoingPacketsDiscarded, "packets"), Rate = rate });
            }
            catch (Exception ex) when (ex is NetworkInformationException or UnauthorizedAccessException)
            {
                previous.Remove(adapter.Id);
                Emit(new { Kind = "Adapter", AdapterId = adapter.Id, Timestamp = now, Availability = ex is UnauthorizedAccessException ? "PermissionDenied" : "Error", Error = ex.GetType().Name });
            }
        }
        foreach (var removed in previous.Keys.Where(id => !present.Contains(id)).ToArray()) previous.Remove(removed);
        if (iteration < samples - 1) await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
    }
    Emit(NativeProbe.ReadTcpOwners());
    Emit(NativeProbe.ReadWifiCapability());
    if (etwSeconds > 0) Emit(EtwProbe.Run(etwSeconds));
    if (target is not null)
    {
        var dnsClock = Stopwatch.StartNew();
        try
        {
            await Dns.GetHostAddressesAsync(target, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);
            Emit(new { Kind = "Dns", Duration = new Measurement<double>(dnsClock.Elapsed.TotalMilliseconds, "ms", "Dns.GetHostAddressesAsync (cache may apply)", DateTimeOffset.UtcNow, Availability.Measured) });
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException)
        { Emit(new { Kind = "Dns", Availability = "Error", Timestamp = DateTimeOffset.UtcNow, Error = ex.GetType().Name }); }
        var latencies = new List<long>();
        int replies = 0;
        using var ping = new Ping();
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(target, TimeSpan.FromSeconds(2), cancellationToken: cancellation.Token);
                if (reply.Status == IPStatus.Success) { replies++; latencies.Add(reply.RoundtripTime); }
            }
            catch (PingException) { }
        }
        var now = DateTimeOffset.UtcNow;
        Emit(new { Kind = "Performance", Source = "ICMP Echo; failure does not prove internet outage", Timestamp = now,
            Latency = new Measurement<double>(latencies.Count > 0 ? latencies.Average() : null, "ms", "Ping", now, latencies.Count > 0 ? Availability.Measured : Availability.Unavailable),
            Loss = new Measurement<double>((5 - replies) * 20d, "%", "Five ICMP attempts", now, Availability.Measured),
            Jitter = new Measurement<double>(latencies.Count > 1 ? latencies.Zip(latencies.Skip(1), (a,b) => (double)Math.Abs(b-a)).Average() : null, "ms", "Mean successive successful RTT difference", now, latencies.Count > 1 ? Availability.Measured : Availability.Unavailable) });
    }
    process.Refresh();
    Emit(new { Kind = "ProbeOverhead", Timestamp = DateTimeOffset.UtcNow, Source = "Process / Stopwatch", Availability = "Measured",
        ElapsedSeconds = clock.Elapsed.TotalSeconds, CpuSeconds = (process.TotalProcessorTime - cpuStart).TotalSeconds, WorkingSetBytes = process.WorkingSet64,
        Detail = "Short probe only; not a long-running benchmark. Includes serialization." });
    return 0;
}
catch (OperationCanceledException) { return 130; }
