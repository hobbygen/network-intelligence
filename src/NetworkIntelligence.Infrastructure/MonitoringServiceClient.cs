using System.IO.Pipes;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.Infrastructure;

/// <summary>Unelevated client for the optional NetworkIntelligence.MonitoringService pipe. Opens a fresh
/// connection per call rather than holding one open — simpler than reconnect logic, and the service is polled
/// on a multi-second cadence anyway (see NetworkIntelligence.Application.MonitoringService).</summary>
public sealed class MonitoringServiceClient : IApplicationTrafficClient
{
    public async Task<ServiceSnapshot> GetSnapshotAsync(CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var pipe = new NamedPipeClientStream(".", ServiceProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cts.Token);
            await ServiceWireFormat.WriteAsync(pipe, ServiceEnvelope.MakeHello("NetworkIntelligence.App", Environment.ProcessId), cts.Token);
            var hello = await ServiceWireFormat.ReadAsync(pipe, ServiceProtocol.MaxResponseBytes, cts.Token);
            if (hello is not { Type: ServiceMessageType.HelloAck, HelloAck.Accepted: true })
                return ServiceSnapshot.Unavailable("Monitoring service handshake failed.");
            await ServiceWireFormat.WriteAsync(pipe, ServiceEnvelope.MakeSnapshotRequest(), cts.Token);
            var response = await ServiceWireFormat.ReadAsync(pipe, ServiceProtocol.MaxResponseBytes, cts.Token);
            return response?.Snapshot ?? ServiceSnapshot.Unavailable("Monitoring service returned no snapshot.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return ServiceSnapshot.Unavailable("Monitoring service not reachable — it may not be installed or not running.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ServiceSnapshot.Unavailable(ex is UnauthorizedAccessException
                ? "Access to the monitoring service was denied."
                : "Monitoring service unavailable: " + ex.GetType().Name);
        }
    }
}
