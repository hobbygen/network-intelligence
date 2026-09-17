using System.IO.Pipes;
using NetworkIntelligence.Contracts;

/// <summary>Standalone client for the (optional, elevated) MonitoringService pipe — validates the IPC end to end
/// without needing the WinUI app. Connects as whichever user runs this probe; the service enforces authorization.</summary>
internal static class ServiceStatusProbe
{
    public static async Task<object> RunAsync(int timeoutSeconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var pipe = new NamedPipeClientStream(".", ServiceProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(TimeSpan.FromSeconds(timeoutSeconds), cts.Token);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return new { Kind = "ServiceStatus", Timestamp = DateTimeOffset.UtcNow, Availability = "Unavailable",
                Error = ex.GetType().Name, Detail = "Could not connect to the monitoring service pipe. It may not be installed, not running, or this account may lack access." };
        }

        await ServiceWireFormat.WriteAsync(pipe, ServiceEnvelope.MakeHello("TelemetryProbe", Environment.ProcessId), cts.Token);
        var helloResponse = await ServiceWireFormat.ReadAsync(pipe, ServiceProtocol.MaxResponseBytes, cts.Token);
        if (helloResponse is not { Type: ServiceMessageType.HelloAck })
            return new { Kind = "ServiceStatus", Timestamp = DateTimeOffset.UtcNow, Availability = "Error", Detail = "Handshake failed.", Response = helloResponse };

        await ServiceWireFormat.WriteAsync(pipe, ServiceEnvelope.MakeSnapshotRequest(), cts.Token);
        var snapshotResponse = await ServiceWireFormat.ReadAsync(pipe, ServiceProtocol.MaxResponseBytes, cts.Token);
        return new { Kind = "ServiceStatus", Timestamp = DateTimeOffset.UtcNow, Availability = "Measured",
            HelloAck = helloResponse.HelloAck, Snapshot = snapshotResponse?.Snapshot };
    }
}
