using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.Domain.Tests;

public class ServiceProtocolTests
{
    [Fact]
    public async Task HelloRoundTripsThroughTheWireFormat()
    {
        using var stream = new MemoryStream();
        var sent = ServiceEnvelope.MakeHello("TestClient", 1234);
        await ServiceWireFormat.WriteAsync(stream, sent, CancellationToken.None);
        stream.Position = 0;
        var received = await ServiceWireFormat.ReadAsync(stream, ServiceProtocol.MaxResponseBytes, CancellationToken.None);
        Assert.NotNull(received);
        Assert.Equal(ServiceMessageType.Hello, received!.Type);
        Assert.Equal(sent.Hello!.ClientName, received.Hello!.ClientName);
        Assert.Equal(sent.Hello.ClientProcessId, received.Hello.ClientProcessId);
    }

    [Fact]
    public async Task SnapshotRoundTripsWithApplicationSamples()
    {
        using var stream = new MemoryStream();
        var now = DateTimeOffset.UnixEpoch;
        var snapshot = new ServiceSnapshot(now, TimeSpan.FromSeconds(5), EventsLost: 0,
            [new ApplicationTrafficSample(4242, "test", 100, 200, 500, 1000, 7, now.AddSeconds(-5), now)],
            "Measured", "test detail");
        var sent = new ServiceEnvelope(ServiceMessageType.SnapshotResponse, ServiceProtocol.Version, Snapshot: snapshot);
        await ServiceWireFormat.WriteAsync(stream, sent, CancellationToken.None);
        stream.Position = 0;
        var received = await ServiceWireFormat.ReadAsync(stream, ServiceProtocol.MaxResponseBytes, CancellationToken.None);
        Assert.Single(received!.Snapshot!.Applications);
        Assert.Equal(4242, received.Snapshot.Applications[0].Pid);
        Assert.Equal(0, received.Snapshot.EventsLost);
    }

    [Fact]
    public async Task ReadReturnsNullOnOrderlyEmptyDisconnect()
    {
        using var stream = new MemoryStream();
        Assert.Null(await ServiceWireFormat.ReadAsync(stream, ServiceProtocol.MaxRequestBytes, CancellationToken.None));
    }

    [Fact]
    public async Task ReadThrowsOnTruncatedMidMessageStream()
    {
        using var stream = new MemoryStream();
        await ServiceWireFormat.WriteAsync(stream, ServiceEnvelope.MakeSnapshotRequest(), CancellationToken.None);
        stream.SetLength(stream.Length - 2); // truncate after the length header, before the body finishes
        stream.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() => ServiceWireFormat.ReadAsync(stream, ServiceProtocol.MaxRequestBytes, CancellationToken.None));
    }

    [Fact]
    public async Task ReadRejectsADeclaredLengthPastTheBound()
    {
        using var stream = new MemoryStream();
        var header = BitConverter.GetBytes(ServiceProtocol.MaxRequestBytes + 1);
        await stream.WriteAsync(header);
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => ServiceWireFormat.ReadAsync(stream, ServiceProtocol.MaxRequestBytes, CancellationToken.None));
    }

    [Fact]
    public void UnavailableSnapshotCarriesNoApplications() => Assert.Empty(ServiceSnapshot.Unavailable("not elevated").Applications);
}
