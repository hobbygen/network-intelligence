using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.MonitoringService;

/// <summary>
/// Owns the elevated kernel Network ETW session validated in docs/ETW_VALIDATION.md and continuously aggregates
/// per-process send/receive byte totals into fixed-length windows. Reads only event headers (PID, size, direction,
/// address family) — never packet payload. Requires elevation; degrades to Unavailable without it rather than throwing.
/// </summary>
internal sealed class EtwCollector(ILogger<EtwCollector> logger) : BackgroundService
{
    private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(5);
    private volatile ServiceSnapshot latest = ServiceSnapshot.Unavailable("Collector has not completed a window yet.");
    public ServiceSnapshot Latest => latest;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            latest = ServiceSnapshot.Unavailable("Windows required.");
            return;
        }
        if (!(TraceEventSession.IsElevated() ?? false))
        {
            logger.LogError("Monitoring service process is not elevated; per-application ETW accounting cannot start.");
            latest = ServiceSnapshot.Unavailable("Service process is not elevated.");
            return;
        }

        const string sessionName = "NetworkIntelligence-MonitoringService";
        try { using var stale = new TraceEventSession(sessionName); stale.Stop(); } catch { /* no prior session */ }

        using var session = new TraceEventSession(sessionName) { StopOnDispose = true };
        try { session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enable the kernel Network ETW provider.");
            latest = ServiceSnapshot.Unavailable("Kernel provider could not be enabled: " + ex.GetType().Name);
            return;
        }

        var window = new ConcurrentDictionary<int, WindowAccumulator>();
        void Attribute(int pid, int size, bool isSend) => window.AddOrUpdate(pid,
            _ => isSend ? new WindowAccumulator(0, size, 1) : new WindowAccumulator(size, 0, 1),
            (_, existing) => isSend
                ? existing with { Sent = existing.Sent + size, Events = existing.Events + 1 }
                : existing with { Received = existing.Received + size, Events = existing.Events + 1 });

        session.Source.Kernel.TcpIpSend += d => Attribute(d.ProcessID, d.size, true);
        session.Source.Kernel.TcpIpRecv += d => Attribute(d.ProcessID, d.size, false);
        session.Source.Kernel.TcpIpSendIPV6 += d => Attribute(d.ProcessID, d.size, true);
        session.Source.Kernel.TcpIpRecvIPV6 += d => Attribute(d.ProcessID, d.size, false);
        session.Source.Kernel.UdpIpSend += d => Attribute(d.ProcessID, d.size, true);
        session.Source.Kernel.UdpIpRecv += d => Attribute(d.ProcessID, d.size, false);
        session.Source.Kernel.UdpIpSendIPV6 += d => Attribute(d.ProcessID, d.size, true);
        session.Source.Kernel.UdpIpRecvIPV6 += d => Attribute(d.ProcessID, d.size, false);

        var processingTask = Task.Run(() =>
        {
            try { session.Source.Process(); }
            catch (Exception ex) { logger.LogError(ex, "ETW processing loop ended unexpectedly."); }
        }, CancellationToken.None);
        logger.LogInformation("Kernel Network ETW session started.");

        try
        {
            using var timer = new PeriodicTimer(WindowDuration);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var windowEnd = DateTimeOffset.UtcNow;
                var windowStart = windowEnd - WindowDuration;
                var keys = window.Keys.ToArray();
                var samples = new List<ApplicationTrafficSample>(keys.Length);
                foreach (var pid in keys)
                {
                    if (!window.TryRemove(pid, out var totals)) continue;
                    samples.Add(ToSample(pid, totals, windowStart, windowEnd));
                }
                long lost = session.EventsLost;
                latest = new ServiceSnapshot(windowEnd, WindowDuration, lost,
                    samples.OrderByDescending(s => s.ReceivedBytesTotal + s.SentBytesTotal).Take(ServiceProtocol.MaxProcesses).ToArray(),
                    "Measured",
                    "Kernel network provider window; PID reuse across windows is not de-duplicated; adapter identity is not attributed to events.");
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            session.Stop();
            await Task.WhenAny(processingTask, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
            logger.LogInformation("Kernel Network ETW session stopped.");
        }
    }

    private static ApplicationTrafficSample ToSample(int pid, WindowAccumulator totals, DateTimeOffset start, DateTimeOffset end)
    {
        string name = pid == 0 ? "System / unattributed" : "Process " + pid;
        try { using var process = Process.GetProcessById(pid); name = process.ProcessName; }
        catch (ArgumentException) { name = "Process " + pid + " (exited)"; }
        double seconds = (end - start).TotalSeconds;
        return new ApplicationTrafficSample(pid, name,
            seconds > 0 ? (long)(totals.Received / seconds) : 0, seconds > 0 ? (long)(totals.Sent / seconds) : 0,
            totals.Received, totals.Sent, totals.Events, start, end);
    }

    private readonly record struct WindowAccumulator(long Received, long Sent, int Events);
}
