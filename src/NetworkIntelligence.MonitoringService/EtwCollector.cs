using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.MonitoringService;

/// <summary>
/// Owns the elevated kernel Network ETW session validated in docs/ETW_VALIDATION.md and continuously aggregates
/// per-process send/receive byte totals into fixed-length windows. Reads only event headers (PID, size, direction,
/// address family) — never packet payload. Requires elevation; degrades to Unavailable without it rather than throwing.
/// Also enables the kernel Process provider so names are captured from process start/rundown events rather than a
/// post-hoc <see cref="Process.GetProcessById(int)"/> lookup at window-flush time — the previous approach lost the
/// name of any process that had already exited within the up-to-5-second window, which fell back to a PID-embedded
/// placeholder that never matched across restarts, so restart-prone short-lived processes could never accumulate
/// enough same-name history to leave the anomaly baseline's "insufficient history" state (docs/DECISIONS.md ADR-011).
/// </summary>
internal sealed class EtwCollector(ILogger<EtwCollector> logger) : BackgroundService
{
    private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(5);
    /// <summary>How long a stopped process's resolved name is kept after its stop event, so a window that has not
    /// flushed yet can still resolve the name of a process that exited mid-window. Bounds the table's memory growth
    /// under long-running process churn (spec section 14) without needing the name at the exact moment of exit.</summary>
    private static readonly TimeSpan StoppedProcessNameRetention = TimeSpan.FromMinutes(2);
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
        try { session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP | KernelTraceEventParser.Keywords.Process); }
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

        // Populated from process start/rundown events, not a live lookup at flush time — see class summary.
        // ProcessDCStart is the rundown of processes already running when the session starts; ProcessDCStop (the
        // mirror rundown at session end) is deliberately not subscribed — it only fires during session.Stop() in
        // the finally block below, after the flush loop has already exited, so it could never be observed.
        var processNames = new ConcurrentDictionary<int, ProcessNameEntry>();
        void RecordStart(ProcessTraceData d) => processNames[d.ProcessID] = new ProcessNameEntry(ResolveImageName(d), null);
        void RecordStop(ProcessTraceData d) => processNames.AddOrUpdate(d.ProcessID,
            _ => new ProcessNameEntry(ResolveImageName(d), DateTimeOffset.UtcNow),
            (_, existing) => existing with { StoppedAt = DateTimeOffset.UtcNow });
        session.Source.Kernel.ProcessStart += RecordStart;
        session.Source.Kernel.ProcessDCStart += RecordStart;
        session.Source.Kernel.ProcessStop += RecordStop;

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
                    samples.Add(ToSample(pid, ResolveName(pid, processNames), totals, windowStart, windowEnd));
                }
                var cutoff = windowEnd - StoppedProcessNameRetention;
                foreach (var (pid, entry) in processNames)
                {
                    if (entry.StoppedAt is { } stoppedAt && stoppedAt < cutoff) processNames.TryRemove(pid, out _);
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

    /// <summary>Resolves a process name for a window's totals, preferring the name captured at that process's own
    /// start/rundown event (accurate even after the process has since exited) over a live lookup, which races the
    /// process's exit and — for anything that had already exited by flush time — always lost.</summary>
    private static string ResolveName(int pid, ConcurrentDictionary<int, ProcessNameEntry> processNames)
    {
        if (pid == 0) return "System / unattributed";
        if (processNames.TryGetValue(pid, out var entry)) return entry.Name;
        try { using var process = Process.GetProcessById(pid); return process.ProcessName; }
        catch (ArgumentException) { return "Process " + pid + " (exited)"; }
    }

    private static string ResolveImageName(ProcessTraceData d)
    {
        var imageFileName = d.ImageFileName;
        if (string.IsNullOrEmpty(imageFileName)) return "Process " + d.ProcessID;
        try { return Path.GetFileNameWithoutExtension(imageFileName); }
        catch (ArgumentException) { return imageFileName; }
    }

    private static ApplicationTrafficSample ToSample(int pid, string name, WindowAccumulator totals, DateTimeOffset start, DateTimeOffset end)
    {
        double seconds = (end - start).TotalSeconds;
        return new ApplicationTrafficSample(pid, name,
            seconds > 0 ? (long)(totals.Received / seconds) : 0, seconds > 0 ? (long)(totals.Sent / seconds) : 0,
            totals.Received, totals.Sent, totals.Events, start, end);
    }

    private readonly record struct WindowAccumulator(long Received, long Sent, int Events);
    /// <summary><paramref name="StoppedAt"/> is null while the process is believed still running; once set, the
    /// entry is pruned after <see cref="StoppedProcessNameRetention"/> to bound the table under long-running churn.</summary>
    private readonly record struct ProcessNameEntry(string Name, DateTimeOffset? StoppedAt);
}
