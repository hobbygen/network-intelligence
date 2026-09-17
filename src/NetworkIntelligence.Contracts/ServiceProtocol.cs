using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NetworkIntelligence.Contracts;

public static class ServiceProtocol
{
    public const int Version = 1;
    public const string PipeName = "NetworkIntelligence.MonitoringService.v1";
    public const string WindowsServiceName = "NetworkIntelligenceMonitoring";
    public const int MaxProcesses = 200;
    public const int MaxRequestBytes = 4 * 1024;
    public const int MaxResponseBytes = 1024 * 1024;
    public const int MaxConcurrentClients = 4;
}

public enum ServiceMessageType { Hello = 1, HelloAck = 2, SnapshotRequest = 3, SnapshotResponse = 4, Error = 255 }

public sealed record ServiceHello(string ClientName, int ClientProcessId);
public sealed record ServiceHelloAck(bool Accepted, string ServiceVersion, string Detail);
public sealed record ServiceErrorPayload(string Code, string Message);
public sealed record ApplicationTrafficSample(int Pid, string ProcessName, long ReceivedBytesPerSecond, long SentBytesPerSecond,
    long ReceivedBytesTotal, long SentBytesTotal, int Events, DateTimeOffset WindowStart, DateTimeOffset WindowEnd);
public sealed record ServiceSnapshot(DateTimeOffset Timestamp, TimeSpan WindowDuration, long EventsLost,
    IReadOnlyList<ApplicationTrafficSample> Applications, string Availability, string Detail)
{
    public static ServiceSnapshot Unavailable(string detail) => new(DateTimeOffset.UtcNow, TimeSpan.Zero, 0, [], "Unavailable", detail);
}
public sealed record ServiceEnvelope(ServiceMessageType Type, int ProtocolVersion,
    ServiceHello? Hello = null, ServiceHelloAck? HelloAck = null, ServiceSnapshot? Snapshot = null, ServiceErrorPayload? Error = null)
{
    public static ServiceEnvelope MakeHello(string clientName, int pid) => new(ServiceMessageType.Hello, ServiceProtocol.Version, Hello: new(clientName, pid));
    public static ServiceEnvelope MakeSnapshotRequest() => new(ServiceMessageType.SnapshotRequest, ServiceProtocol.Version);
    public static ServiceEnvelope MakeError(string code, string message) => new(ServiceMessageType.Error, ServiceProtocol.Version, Error: new(code, message));
}

/// <summary>Length-prefixed JSON framing shared by the service and its clients. Never trusts a declared length past <paramref name="maxBytes"/>.</summary>
public static class ServiceWireFormat
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    public static async Task WriteAsync(Stream stream, ServiceEnvelope envelope, CancellationToken token)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header.ToArray(), token);
        await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);
    }

    /// <returns>The decoded envelope, or null if the stream ended before a full message arrived (orderly disconnect).</returns>
    public static async Task<ServiceEnvelope?> ReadAsync(Stream stream, int maxBytes, CancellationToken token)
    {
        byte[] header = new byte[4];
        if (!await ReadExactAsync(stream, header, token)) return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maxBytes) throw new InvalidDataException($"Declared message length {length} is out of bounds (max {maxBytes}).");
        byte[] payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, token)) return null;
        return JsonSerializer.Deserialize<ServiceEnvelope>(payload, Options)
            ?? throw new InvalidDataException("Message body deserialized to null.");
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), token);
            if (read == 0) return offset == 0 ? false : throw new EndOfStreamException("Connection closed mid-message.");
            offset += read;
        }
        return true;
    }
}
