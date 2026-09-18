namespace NetworkIntelligence.Domain;

public enum HealthFactorAvailability { Available, Unavailable }

/// <summary>One contributing input to the overall score (requirements section 12.4's "display contributing
/// factors"). <paramref name="Score"/> is 0-100, higher is better, and is null exactly when <paramref
/// name="Availability"/> is Unavailable — an unavailable factor contributes nothing to the overall score rather
/// than being assumed good, bad, or defaulted to some filler value.</summary>
public sealed record HealthFactor(string Name, int Weight, HealthFactorAvailability Availability, double? Score, string Detail);

/// <summary>The transparent network health score (requirements section 12.4). <paramref name="Score"/> and
/// <paramref name="Band"/> are null when there isn't enough measured data yet — section 4.2's "no fabricated
/// telemetry" applies to this derived score exactly as it does to a raw measurement.</summary>
public sealed record NetworkHealthScore(int? Score, string? Band, IReadOnlyList<HealthFactor> Factors, string Detail)
{
    public static NetworkHealthScore Insufficient(string detail) => new(null, null, [], detail);
}

/// <summary>Computes <see cref="NetworkHealthScore"/> from measurements already collected elsewhere in the app —
/// pure, no I/O, no hidden clock reads (the caller supplies <paramref name="now"/> explicitly, matching
/// AnomalyTracker's clock-injection pattern so this stays deterministic and unit-testable). Each factor is scored
/// independently on a documented 0-100 scale, then combined as a weighted average over only the AVAILABLE
/// factors: an unmeasured factor is excluded entirely rather than assumed good, bad, or given a filler value, and
/// the weights of missing factors are not silently redistributed onto some hidden default — the overall score
/// simply reflects fewer inputs, disclosed via <see cref="NetworkHealthScore.Detail"/>.
///
/// The per-factor scoring curves and thresholds below (latency/loss/jitter/DNS breakpoints, the disconnect-count
/// tiers, the 0-100 weights) are a first, deliberately simple pass — round numbers chosen to be easy to explain,
/// not derived from measured user-perceived-quality data. Like <see cref="AnomalySettings.Balanced"/>, they are
/// provisional and not validated against real usage; docs/DECISIONS.md tracks this.</summary>
public static class HealthScoreCalculator
{
    /// <summary>A diagnostic result older than this is treated as stale, not a current measurement — diagnostics
    /// are only ever user-initiated (never run automatically), so network conditions may have changed since.</summary>
    public static readonly TimeSpan DiagnosticFreshness = TimeSpan.FromMinutes(30);
    /// <summary>Lookback window for the "recent disconnects" factor (requirements section 12.4).</summary>
    public static readonly TimeSpan DisconnectLookback = TimeSpan.FromHours(24);

    public static NetworkHealthScore Compute(DateTimeOffset now, bool hasAdapter, bool isUp, bool hasGateway,
        double? latencyAvgMs, double? packetLossPercent, double? jitterMs, double? dnsMs, DateTimeOffset? diagnosticTimestamp,
        bool isWifi, uint? wifiSignalPercent, long? adapterErrors, long? adapterDiscards,
        IReadOnlyList<DateTimeOffset> disconnectTimestamps)
    {
        if (!hasAdapter) return NetworkHealthScore.Insufficient("Waiting for the first adapter measurement.");
        bool diagnosticFresh = diagnosticTimestamp is { } ts && now - ts <= DiagnosticFreshness;
        var factors = new List<HealthFactor> { Connectivity(isUp, hasGateway) };
        factors.Add(Latency(latencyAvgMs, diagnosticFresh));
        factors.Add(PacketLoss(packetLossPercent, diagnosticFresh));
        factors.Add(Jitter(jitterMs, diagnosticFresh));
        factors.Add(DnsResponse(dnsMs, diagnosticFresh));
        if (isWifi) factors.Add(WifiSignal(wifiSignalPercent));
        factors.Add(AdapterErrors(adapterErrors, adapterDiscards));
        factors.Add(RecentDisconnects(disconnectTimestamps, now));

        var available = factors.Where(f => f.Availability == HealthFactorAvailability.Available).ToArray();
        if (available.Length == 0) return NetworkHealthScore.Insufficient("No measurements available yet.");
        double totalWeight = available.Sum(f => f.Weight);
        int score = (int)Math.Round(available.Sum(f => f.Score!.Value * f.Weight) / totalWeight);
        string band = score switch { >= 85 => "Excellent", >= 70 => "Good", >= 50 => "Fair", >= 25 => "Poor", _ => "Critical" };
        return new(score, band, factors, $"{available.Length} of {factors.Count} factors available.");
    }

    private static HealthFactor Connectivity(bool isUp, bool hasGateway) => !isUp
        ? new("Connectivity", 30, HealthFactorAvailability.Available, 0, "Adapter is not connected.")
        : hasGateway
            ? new("Connectivity", 30, HealthFactorAvailability.Available, 100, "Adapter up with a configured gateway.")
            : new("Connectivity", 30, HealthFactorAvailability.Available, 50, "Adapter up; no gateway configured.");

    private static HealthFactor Latency(double? avgMs, bool fresh) => avgMs is { } ms && fresh
        ? new("Latency", 15, HealthFactorAvailability.Available, Math.Clamp(100 - ms / 3, 0, 100), $"Average round-trip {ms:0} ms (last diagnostic).")
        : new("Latency", 15, HealthFactorAvailability.Unavailable, null, "Run Diagnostics for a current measurement.");

    private static HealthFactor PacketLoss(double? lossPercent, bool fresh) => lossPercent is { } loss && fresh
        ? new("Packet loss", 15, HealthFactorAvailability.Available, Math.Clamp(100 - loss * 4, 0, 100), $"{loss:0.0}% loss (last diagnostic).")
        : new("Packet loss", 15, HealthFactorAvailability.Unavailable, null, "Run Diagnostics for a current measurement.");

    private static HealthFactor Jitter(double? jitterMs, bool fresh) => jitterMs is { } ms && fresh
        ? new("Jitter", 10, HealthFactorAvailability.Available, Math.Clamp(100 - ms * 2, 0, 100), $"{ms:0.0} ms jitter (last diagnostic).")
        : new("Jitter", 10, HealthFactorAvailability.Unavailable, null, "Run Diagnostics for a current measurement.");

    private static HealthFactor DnsResponse(double? dnsMs, bool fresh) => dnsMs is { } ms && fresh
        ? new("DNS response", 10, HealthFactorAvailability.Available, Math.Clamp(100 - ms / 2, 0, 100), $"{ms:0} ms DNS response (last diagnostic).")
        : new("DNS response", 10, HealthFactorAvailability.Unavailable, null, "Run Diagnostics for a current measurement.");

    private static HealthFactor WifiSignal(uint? signalPercent) => signalPercent is { } signal
        ? new("Wi-Fi signal", 10, HealthFactorAvailability.Available, signal, $"{signal}% signal.")
        : new("Wi-Fi signal", 10, HealthFactorAvailability.Unavailable, null, "Signal strength not reported.");

    private static HealthFactor AdapterErrors(long? errors, long? discards)
    {
        if (errors is null || discards is null) return new("Adapter errors", 5, HealthFactorAvailability.Unavailable, null, "Error/discard counters not reported.");
        bool clean = errors == 0 && discards == 0;
        return new("Adapter errors", 5, HealthFactorAvailability.Available, clean ? 100 : 60,
            clean ? "No errors or discards since adapter counters began." : $"{errors} errors, {discards} discards since adapter counters began.");
    }

    private static HealthFactor RecentDisconnects(IReadOnlyList<DateTimeOffset> timestamps, DateTimeOffset now)
    {
        int count = timestamps.Count(t => now - t <= DisconnectLookback);
        double score = count switch { 0 => 100, 1 => 70, <= 3 => 40, _ => 10 };
        return new("Recent disconnects", 5, HealthFactorAvailability.Available, score, $"{count} disconnect(s) in the last 24 hours.");
    }
}
