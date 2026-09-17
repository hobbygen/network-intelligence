using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.Infrastructure;

public sealed class Diagnostics
{
    public async Task<DiagnosticResult> RunAsync(string target, CancellationToken token)
    {
        if (Uri.CheckHostName(target) == UriHostNameType.Unknown) throw new ArgumentException("Enter a hostname or IP address.");
        double? dns = null; var messages = new List<string>(); IPAddress? address = null;
        var watch = Stopwatch.StartNew();
        try { var addresses = await Dns.GetHostAddressesAsync(target, token).WaitAsync(TimeSpan.FromSeconds(5), token); address = addresses.FirstOrDefault(); dns = watch.Elapsed.TotalMilliseconds; }
        catch (Exception ex) when (ex is SocketException or TimeoutException) { messages.Add("DNS resolution failed: " + ex.GetType().Name); }
        if (address is null) return new(DateTimeOffset.UtcNow, target, dns, null, null, null, null, null, 0, 0, 0, "DNS unavailable", string.Join(" ", messages));
        var rtts = new List<double>(); var differences = new List<double>(); double? previous = null; int errors = 0;
        using var ping = new Ping();
        for (int i = 0; i < 10; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(address, TimeSpan.FromSeconds(2), cancellationToken: token);
                if (reply.Status == IPStatus.Success)
                {
                    rtts.Add(reply.RoundtripTime);
                    if (previous.HasValue) differences.Add(Math.Abs(reply.RoundtripTime - previous.Value));
                    previous = reply.RoundtripTime;
                }
                else { previous = null; messages.Add(reply.Status.ToString()); }
            }
            catch (PingException) { errors++; previous = null; }
            if (i < 9) await Task.Delay(250, token);
        }
        messages.Add("Source: OS DNS resolver (cache may apply) and 10 ICMP echo attempts. Failed ICMP is not proof of internet outage. Jitter uses consecutive successful replies. RTT resolution is 1 ms.");
        if (errors > 0) messages.Add($"{errors} local probe errors; loss unavailable.");
        return new(DateTimeOffset.UtcNow, target, dns, rtts.Count > 0 ? rtts.Average() : null, rtts.Count > 0 ? rtts.Min() : null, rtts.Count > 0 ? rtts.Max() : null,
            differences.Count > 0 ? differences.Average() : null, errors == 0 ? (10 - rtts.Count) * 10d : null, 10, rtts.Count, errors,
            rtts.Count > 0 ? "Target responded" : "No ICMP response", string.Join(" ", messages.Distinct()));
    }
}
