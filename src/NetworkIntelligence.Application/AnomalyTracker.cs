using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Application;

/// <summary>Turns a stream of momentary <see cref="AnomalyEvaluator"/> verdicts into "should this actually fire an
/// alert" decisions: requires the deviation to persist continuously for a minimum duration (never alerts on a
/// single brief spike), then gates repeat firings with a per-(process,direction) cooldown. Deterministic given an
/// explicit clock (no internal DateTimeOffset.UtcNow reads) so it is fully unit-testable without real delays.</summary>
public sealed class AnomalyTracker(TimeSpan minimumSustained, TimeSpan cooldown)
{
    private readonly Dictionary<(string ProcessName, AnomalyDirection Direction), (DateTimeOffset FirstAnomalousAt, DateTimeOffset? LastAlertAt)> state = [];
    /// <summary>User-requested suppression (section 10.6's "snooze," distinct from the automatic <paramref
    /// name="cooldown"/> above and from a permanent trust exclusion). Not persisted — like the rest of this
    /// tracker's state, it resets on app restart.</summary>
    private readonly Dictionary<(string ProcessName, AnomalyDirection Direction), DateTimeOffset> snoozedUntil = [];

    public bool ShouldFire(string processName, AnomalyDirection direction, bool isAnomalousNow, DateTimeOffset now)
    {
        var key = (processName, direction);
        if (!isAnomalousNow) { state.Remove(key); return false; }
        if (!state.TryGetValue(key, out var entry)) { state[key] = (now, null); return false; }
        bool sustained = now - entry.FirstAnomalousAt >= minimumSustained;
        bool cooledDown = entry.LastAlertAt is null || now - entry.LastAlertAt >= cooldown;
        bool snoozed = snoozedUntil.TryGetValue(key, out var until) && now < until;
        // A snoozed firing doesn't update LastAlertAt — no alert happened — so the moment the snooze lapses,
        // an already-sustained anomaly can fire immediately rather than needing to re-satisfy the cooldown too.
        if (sustained && cooledDown && !snoozed) { state[key] = (entry.FirstAnomalousAt, now); return true; }
        return false;
    }

    /// <summary>Suppresses firing for this (process, direction) until <paramref name="until"/>, without affecting
    /// the sustained-duration timer or automatic cooldown, and without excluding the app from detection entirely
    /// (that's <c>AppSettings.TrustedApplications</c>, a separate, persisted mechanism).</summary>
    public void Snooze(string processName, AnomalyDirection direction, DateTimeOffset until) => snoozedUntil[(processName, direction)] = until;
}
