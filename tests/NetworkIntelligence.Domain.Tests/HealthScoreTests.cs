using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Domain.Tests;

public class HealthScoreCalculatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch + TimeSpan.FromDays(1);
    private static NetworkHealthScore Compute(bool hasAdapter = true, bool isUp = true, bool hasGateway = true,
        double? latencyAvgMs = null, double? packetLossPercent = null, double? jitterMs = null, double? dnsMs = null,
        DateTimeOffset? diagnosticTimestamp = null, bool isWifi = false, uint? wifiSignalPercent = null,
        long? adapterErrors = 0, long? adapterDiscards = 0, IReadOnlyList<DateTimeOffset>? disconnects = null) =>
        HealthScoreCalculator.Compute(Now, hasAdapter, isUp, hasGateway, latencyAvgMs, packetLossPercent, jitterMs, dnsMs,
            diagnosticTimestamp, isWifi, wifiSignalPercent, adapterErrors, adapterDiscards, disconnects ?? []);

    [Fact] public void NoAdapterIsInsufficientData()
    {
        var result = Compute(hasAdapter: false);
        Assert.Null(result.Score);
        Assert.Null(result.Band);
        Assert.Empty(result.Factors);
    }
    [Fact] public void ConnectivityAloneStillProducesAScore()
    {
        // No diagnostic ever run, not Wi-Fi: only connectivity, adapter errors and recent disconnects are available;
        // latency/loss/jitter/DNS (all sourced from a diagnostic) are unavailable.
        var result = Compute();
        Assert.NotNull(result.Score);
        Assert.Equal(3, result.Factors.Count(f => f.Availability == HealthFactorAvailability.Available));
        Assert.Equal(4, result.Factors.Count(f => f.Availability == HealthFactorAvailability.Unavailable));
    }
    [Fact] public void DownAdapterScoresZeroConnectivityAndLowOverall()
    {
        var up = Compute(isUp: true);
        var down = Compute(isUp: false);
        Assert.True(down.Score < up.Score);
    }
    [Fact] public void NoGatewayScoresBetweenDownAndFullyUp()
    {
        var down = Compute(isUp: false);
        var noGateway = Compute(isUp: true, hasGateway: false);
        var fullyUp = Compute(isUp: true, hasGateway: true);
        Assert.True(down.Score < noGateway.Score);
        Assert.True(noGateway.Score < fullyUp.Score);
    }
    [Fact] public void FreshDiagnosticMakesLatencyLossJitterDnsAvailable()
    {
        var result = Compute(latencyAvgMs: 20, packetLossPercent: 0, jitterMs: 2, dnsMs: 10, diagnosticTimestamp: Now);
        foreach (var name in new[] { "Latency", "Packet loss", "Jitter", "DNS response" })
            Assert.Equal(HealthFactorAvailability.Available, result.Factors.Single(f => f.Name == name).Availability);
    }
    [Fact] public void StaleDiagnosticIsTreatedAsUnavailable()
    {
        var stale = Now - HealthScoreCalculator.DiagnosticFreshness - TimeSpan.FromMinutes(1);
        var result = Compute(latencyAvgMs: 20, packetLossPercent: 0, jitterMs: 2, dnsMs: 10, diagnosticTimestamp: stale);
        foreach (var name in new[] { "Latency", "Packet loss", "Jitter", "DNS response" })
            Assert.Equal(HealthFactorAvailability.Unavailable, result.Factors.Single(f => f.Name == name).Availability);
    }
    [Fact] public void PerfectMeasurementsAcrossEveryFactorScoreNearMaximum()
    {
        var result = Compute(latencyAvgMs: 0, packetLossPercent: 0, jitterMs: 0, dnsMs: 0, diagnosticTimestamp: Now,
            isWifi: true, wifiSignalPercent: 100, adapterErrors: 0, adapterDiscards: 0);
        Assert.Equal(100, result.Score);
        Assert.Equal("Excellent", result.Band);
    }
    [Fact] public void WorstMeasurementsAcrossEveryFactorScoreNearMinimum()
    {
        var result = Compute(isUp: false, latencyAvgMs: 1000, packetLossPercent: 100, jitterMs: 1000, dnsMs: 1000,
            diagnosticTimestamp: Now, isWifi: true, wifiSignalPercent: 0, adapterErrors: 500, adapterDiscards: 500,
            disconnects: [Now, Now.AddHours(-1), Now.AddHours(-2), Now.AddHours(-3)]);
        Assert.NotNull(result.Score);
        Assert.True(result.Score <= 10);
        Assert.Equal("Critical", result.Band);
    }
    [Fact] public void WifiSignalOnlyContributesWhenOnWifi()
    {
        var wired = Compute(isWifi: false);
        Assert.DoesNotContain(wired.Factors, f => f.Name == "Wi-Fi signal");
        var wireless = Compute(isWifi: true, wifiSignalPercent: 40);
        Assert.Contains(wireless.Factors, f => f.Name == "Wi-Fi signal" && f.Score == 40);
    }
    [Fact] public void AdapterErrorsUnavailableWhenCountersNotReported()
    {
        var result = Compute(adapterErrors: null, adapterDiscards: null);
        Assert.Equal(HealthFactorAvailability.Unavailable, result.Factors.Single(f => f.Name == "Adapter errors").Availability);
    }
    [Fact] public void AdapterErrorsPresentReducesScoreVersusClean()
    {
        var clean = Compute(adapterErrors: 0, adapterDiscards: 0);
        var dirty = Compute(adapterErrors: 5, adapterDiscards: 0);
        Assert.True(dirty.Score < clean.Score);
    }
    [Fact] public void OnlyDisconnectsWithinTheLookbackWindowCount()
    {
        var recent = Compute(disconnects: [Now.AddHours(-1)]);
        var old = Compute(disconnects: [Now - HealthScoreCalculator.DisconnectLookback - TimeSpan.FromHours(1)]);
        var none = Compute(disconnects: []);
        Assert.True(recent.Score < none.Score);
        Assert.Equal(none.Score, old.Score);
    }
}
