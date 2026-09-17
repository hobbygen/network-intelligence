using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using NetworkIntelligence.Contracts;

/// <summary>
/// Validation-plan experiment 5: send an exact, independently-counted number of bytes over a real loopback
/// TCP socket from this process, then compare that reference total against what the elevated MonitoringService's
/// ETW-based collector attributed to this same process ID over the same interval. The MonitoringService must
/// already be running (elevated) — this probe never elevates itself.
/// </summary>
internal static class AccuracyProbe
{
    private const int ChunkSize = 64 * 1024;

    public static async Task<object> RunAsync(long targetBytes, CancellationToken token)
    {
        using var pipe = new NamedPipeClientStream(".", ServiceProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(TimeSpan.FromSeconds(5), token); }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return new { Kind = "AccuracyTest", Timestamp = DateTimeOffset.UtcNow, Availability = "Unavailable",
                Error = ex.GetType().Name, Detail = "Could not connect to the MonitoringService pipe. Start it elevated first (scripts/service-run-foreground.ps1)." };
        }
        await ServiceWireFormat.WriteAsync(pipe, ServiceEnvelope.MakeHello("AccuracyProbe", Environment.ProcessId), token);
        var hello = await ServiceWireFormat.ReadAsync(pipe, ServiceProtocol.MaxResponseBytes, token);
        if (hello is not { Type: ServiceMessageType.HelloAck, HelloAck.Accepted: true })
            return new { Kind = "AccuracyTest", Timestamp = DateTimeOffset.UtcNow, Availability = "Error", Detail = "Handshake with the service failed." };

        int pid = Environment.ProcessId;
        long referenceSent = 0, referenceReceived = 0;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var receiveTask = Task.Run(async () =>
        {
            using var server = await listener.AcceptTcpClientAsync(token);
            using var stream = server.GetStream();
            var buffer = new byte[ChunkSize];
            long remaining = targetBytes;
            while (remaining > 0)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(ChunkSize, remaining)), token);
                if (read == 0) break;
                Interlocked.Add(ref referenceReceived, read);
                remaining -= read;
            }
        }, token);

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port, token);
            using var stream = client.GetStream();
            var buffer = new byte[ChunkSize];
            Random.Shared.NextBytes(buffer);
            long remaining = targetBytes;
            var transferClock = System.Diagnostics.Stopwatch.StartNew();
            while (remaining > 0)
            {
                int chunk = (int)Math.Min(ChunkSize, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, chunk), token);
                Interlocked.Add(ref referenceSent, chunk);
                remaining -= chunk;
            }
            await stream.FlushAsync(token);
            await receiveTask;
            listener.Stop();
            var transferElapsed = transferClock.Elapsed;

            // ETW windows are 5s; poll well past two full rotations after the transfer so every event this
            // transfer generated has landed in a closed (non-partial) window before we sum what was attributed.
            var etwReceived = 0L; var etwSent = 0L; var lastCountedWindowEnd = DateTimeOffset.MinValue;
            long eventsLostTotal = 0;
            var pollUntil = DateTime.UtcNow.AddSeconds(13);
            while (DateTime.UtcNow < pollUntil)
            {
                await ServiceWireFormat.WriteAsync(pipe, ServiceEnvelope.MakeSnapshotRequest(), token);
                var response = await ServiceWireFormat.ReadAsync(pipe, ServiceProtocol.MaxResponseBytes, token);
                var sample = response?.Snapshot?.Applications.FirstOrDefault(a => a.Pid == pid);
                if (response?.Snapshot is { } snap) eventsLostTotal += snap.EventsLost;
                if (sample is { } s && s.WindowEnd > lastCountedWindowEnd)
                {
                    etwReceived += s.ReceivedBytesTotal;
                    etwSent += s.SentBytesTotal;
                    lastCountedWindowEnd = s.WindowEnd;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            double receivedDeltaPercent = referenceReceived == 0 ? 0 : 100.0 * (etwReceived - referenceReceived) / referenceReceived;
            double sentDeltaPercent = referenceSent == 0 ? 0 : 100.0 * (etwSent - referenceSent) / referenceSent;

            return new
            {
                Kind = "AccuracyTest",
                Timestamp = DateTimeOffset.UtcNow,
                Availability = "Measured",
                Pid = pid,
                TargetBytes = targetBytes,
                TransferElapsedSeconds = transferElapsed.TotalSeconds,
                ReferenceSentBytes = referenceSent,
                ReferenceReceivedBytes = referenceReceived,
                EtwAttributedSentBytes = etwSent,
                EtwAttributedReceivedBytes = etwReceived,
                SentDeltaPercent = sentDeltaPercent,
                ReceivedDeltaPercent = receivedDeltaPercent,
                EventsLostDuringPolling = eventsLostTotal,
                Detail = "Reference counts are application-layer bytes written/read on a loopback TCP socket by this " +
                         "process, tallied independently of ETW. ETW-attributed counts are summed from closed 5-second " +
                         "MonitoringService windows for this process's PID only. A nonzero delta may reflect TCP/IP " +
                         "framing the kernel provider counts differently from application payload, not necessarily an error."
            };
        }
    }
}
