using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Application;

/// <summary>Turns a stream of momentary <see cref="AnomalyEvaluator"/> verdicts into "should this actually fire an
/// alert" decisions: requires the deviation to persist continuously for a minimum duration (never alerts on a
/// single brief spike), then gates repeat firings with a per-(process,direction) cooldown. Deterministic given an
/// explicit clock (no internal DateTimeOffset.UtcNow reads) so it is fully unit-testable without real delays.</summary>
public sealed class AnomalyTracker(TimeSpan minimumSustained, TimeSpan cooldown)
{
    private readonly Dictionary<(string ProcessName, AnomalyDirection Direction), (DateTimeOffset FirstAnomalousAt, DateTimeOffset? LastAlertAt)> state = [];

    public bool ShouldFire(string processName, AnomalyDirection direction, bool isAnomalousNow, DateTimeOffset now)
    {
        var key = (processName, direction);
        if (!isAnomalousNow) { state.Remove(key); return false; }
        if (!state.TryGetValue(key, out var entry)) { state[key] = (now, null); return false; }
        bool sustained = now - entry.FirstAnomalousAt >= minimumSustained;
        bool cooledDown = entry.LastAlertAt is null || now - entry.LastAlertAt >= cooldown;
        if (sustained && cooledDown) { state[key] = (entry.FirstAnomalousAt, now); return true; }
        return false;
    }
}
