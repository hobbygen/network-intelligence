using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.MonitoringService;

/// <summary>
/// Serves the snapshot the elevated <see cref="EtwCollector"/> maintains to unelevated local clients (the desktop
/// app) over a named pipe. Authorization is enforced by the OS at connect time via a pipe ACL that allows only
/// interactively logged-on local users and Administrators, and explicitly denies anonymous and network logons —
/// this process never accepts remote connections. Messages are a fixed, versioned, length-bounded envelope; there
/// is no free-form command channel.
/// </summary>
internal sealed class PipeServer(EtwCollector collector, ILogger<PipeServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) return;
        var security = BuildPipeSecurity();
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(ServiceProtocol.PipeName, PipeDirection.InOut,
                    ServiceProtocol.MaxConcurrentClients, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                    inBufferSize: 4096, outBufferSize: 65536, pipeSecurity: security);
                await pipe.WaitForConnectionAsync(stoppingToken);
                var accepted = pipe;
                pipe = null; // ownership moves to the handler below
                _ = HandleClientAsync(accepted, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException ex) { logger.LogWarning(ex, "Pipe instance limit reached or transient I/O error; retrying."); await Task.Delay(1000, stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Pipe listener error."); await Task.Delay(1000, stoppingToken); }
            finally { pipe?.Dispose(); }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken outerToken)
    {
        using var _ = pipe;
        try
        {
            // The pipe ACL (Administrators + interactive logon only, network/anonymous denied) is the actual
            // authorization boundary, enforced by the OS before WaitForConnectionAsync ever completes. Identity
            // name resolution below is best-effort audit logging only, and Windows requires at least one read
            // from the pipe before it can report the client's identity — so it happens after the first message.
            string identity = "unresolved";
            bool identityLogged = false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
            cts.CancelAfter(TimeSpan.FromMinutes(10));
            while (!cts.IsCancellationRequested && pipe.IsConnected)
            {
                ServiceEnvelope? request;
                try { request = await ServiceWireFormat.ReadAsync(pipe, ServiceProtocol.MaxRequestBytes, cts.Token); }
                catch (InvalidDataException ex) { logger.LogWarning(ex, "Rejected malformed message from {Identity}.", identity); break; }
                if (request is null) break;
                if (!identityLogged)
                {
                    try { identity = pipe.GetImpersonationUserName(); }
                    catch (IOException ex) { logger.LogWarning(ex, "Client identity name could not be resolved; serving anyway (ACL already authorized the connection)."); }
                    logger.LogInformation("Pipe client connected: {Identity}", identity);
                    identityLogged = true;
                }
                var response = Handle(request);
                await ServiceWireFormat.WriteAsync(pipe, response, cts.Token);
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogError(ex, "Pipe client handling failed."); }
    }

    private ServiceEnvelope Handle(ServiceEnvelope request)
    {
        if (request.ProtocolVersion != ServiceProtocol.Version)
            return ServiceEnvelope.MakeError("VersionMismatch", $"Service supports protocol {ServiceProtocol.Version}.");
        return request.Type switch
        {
            ServiceMessageType.Hello => new ServiceEnvelope(ServiceMessageType.HelloAck, ServiceProtocol.Version,
                HelloAck: new ServiceHelloAck(true, ServiceProtocol.Version.ToString(), "Connected.")),
            ServiceMessageType.SnapshotRequest => new ServiceEnvelope(ServiceMessageType.SnapshotResponse, ServiceProtocol.Version,
                Snapshot: collector.Latest),
            _ => ServiceEnvelope.MakeError("UnknownMessageType", "Unsupported message type."),
        };
    }

    private static PipeSecurity BuildPipeSecurity()
    {
        var security = new PipeSecurity();
        security.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AnonymousSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }
}
