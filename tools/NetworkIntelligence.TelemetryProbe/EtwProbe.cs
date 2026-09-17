using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

internal static class EtwProbe
{
    // Section 7.2 experiment: can traffic be attributed to individual processes without elevation,
    // and what does per-application byte accounting actually cost via the kernel network provider?
    // Collects only event headers (pid, byte count, protocol, direction) — never packet payload.
    public static object Run(int seconds)
    {
        const string sessionName = "NetworkIntelligence-EtwProbe";
        if (!(TraceEventSession.IsElevated() ?? false))
        {
            return new
            {
                Kind = "EtwCapability",
                Timestamp = DateTimeOffset.UtcNow,
                Source = "TraceEventSession.IsElevated",
                Availability = "PermissionDenied",
                Elevated = false,
                Detail = "Process is not elevated. Starting a real-time ETW trace session requires Administrator " +
                          "or Performance Log Users membership; the session was not attempted. This is the expected " +
                          "standard-user result and drives the Tier-3 elevated-collector decision."
            };
        }

        try { using var stale = new TraceEventSession(sessionName); stale.Stop(); } catch { /* clear any stale session left by a prior crashed run */ }

        var clock = Stopwatch.StartNew();
        using var session = new TraceEventSession(sessionName) { StopOnDispose = true };

        var perProcess = new Dictionary<int, ProcessTotals>();
        int tcpSend = 0, tcpReceive = 0, udpSend = 0, udpReceive = 0, connect = 0, disconnect = 0, v6Events = 0;

        try
        {
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
        }
        catch (Exception ex)
        {
            return new
            {
                Kind = "EtwCapability",
                Timestamp = DateTimeOffset.UtcNow,
                Source = "TraceEventSession.EnableKernelProvider(NetworkTCPIP)",
                Availability = "Error",
                Elevated = true,
                Error = ex.GetType().Name,
                Message = ex.Message,
                Detail = "Elevated session creation itself failed; another kernel logger may already be active."
            };
        }

        void Attribute(int pid, int size, bool isTcp, bool isSend, bool isV6)
        {
            if (isV6) v6Events++;
            if (isTcp && isSend) tcpSend++;
            else if (isTcp && !isSend) tcpReceive++;
            else if (!isTcp && isSend) udpSend++;
            else udpReceive++;
            var totals = perProcess.TryGetValue(pid, out var existing) ? existing : new ProcessTotals();
            perProcess[pid] = isSend ? totals with { SentBytes = totals.SentBytes + size, Events = totals.Events + 1 }
                                       : totals with { ReceivedBytes = totals.ReceivedBytes + size, Events = totals.Events + 1 };
        }

        session.Source.Kernel.TcpIpSend += d => Attribute(d.ProcessID, d.size, isTcp: true, isSend: true, isV6: false);
        session.Source.Kernel.TcpIpRecv += d => Attribute(d.ProcessID, d.size, isTcp: true, isSend: false, isV6: false);
        session.Source.Kernel.TcpIpSendIPV6 += d => Attribute(d.ProcessID, d.size, isTcp: true, isSend: true, isV6: true);
        session.Source.Kernel.TcpIpRecvIPV6 += d => Attribute(d.ProcessID, d.size, isTcp: true, isSend: false, isV6: true);
        session.Source.Kernel.UdpIpSend += d => Attribute(d.ProcessID, d.size, isTcp: false, isSend: true, isV6: false);
        session.Source.Kernel.UdpIpRecv += d => Attribute(d.ProcessID, d.size, isTcp: false, isSend: false, isV6: false);
        session.Source.Kernel.UdpIpSendIPV6 += d => Attribute(d.ProcessID, d.size, isTcp: false, isSend: true, isV6: true);
        session.Source.Kernel.UdpIpRecvIPV6 += d => Attribute(d.ProcessID, d.size, isTcp: false, isSend: false, isV6: true);
        session.Source.Kernel.TcpIpConnect += _ => connect++;
        session.Source.Kernel.TcpIpDisconnect += _ => disconnect++;

        var processTask = Task.Run(() => session.Source.Process());
        Thread.Sleep(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60)));
        long eventsLost = session.EventsLost;
        session.Stop();
        processTask.Wait(TimeSpan.FromSeconds(5));

        var byProcess = perProcess.Select(pair =>
        {
            string identity = "Process " + pair.Key;
            try { using var process = Process.GetProcessById(pair.Key); identity = process.ProcessName; }
            catch (ArgumentException) { identity = pair.Key == 0 ? "System / unattributed" : "Process " + pair.Key + " (exited before resolution)"; }
            return new
            {
                Pid = pair.Key,
                ProcessName = identity,
                ReceivedBytes = pair.Value.ReceivedBytes,
                SentBytes = pair.Value.SentBytes,
                Events = pair.Value.Events
            };
        }).OrderByDescending(p => p.ReceivedBytes + p.SentBytes).ToArray();

        return new
        {
            Kind = "EtwCapability",
            Timestamp = DateTimeOffset.UtcNow,
            Source = "Kernel Network provider (Microsoft-Windows-Kernel-Network) via TraceEventSession",
            Availability = "Measured",
            Elevated = true,
            DurationSeconds = clock.Elapsed.TotalSeconds,
            EventsLost = eventsLost,
            TcpSendEvents = tcpSend,
            TcpReceiveEvents = tcpReceive,
            UdpSendEvents = udpSend,
            UdpReceiveEvents = udpReceive,
            IPv6Events = v6Events,
            ConnectEvents = connect,
            DisconnectEvents = disconnect,
            ProcessCount = byProcess.Length,
            ByProcess = byProcess,
            Detail = "Per-event size and owning PID from kernel network provider headers only; no packet payload read. " +
                     "PID reuse/exit during the window is not de-duplicated in this probe. Requires elevation to start."
        };
    }

    private readonly record struct ProcessTotals(long ReceivedBytes = 0, long SentBytes = 0, int Events = 0);
}
