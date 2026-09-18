using Microsoft.Extensions.Logging;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Application;

/// <summary>
/// Requirements section 10.5's detection pipeline, downstream of MonitoringService.ApplicationTraffic (ADR-009):
/// resolve identity (process name — see docs/DECISIONS.md ADR-010's caveat), retrieve baseline, evaluate
/// independently per direction, apply trust/sensitivity/cooldown, persist and surface a fired anomaly.
/// A pure consumer of already-Measured snapshots; never touches ETW or the pipe itself.
/// </summary>
public sealed class AnomalyDetectionService(IHistoryStore store, ILogger<AnomalyDetectionService> logger)
{
    private static readonly TimeSpan BaselineRefreshInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BaselineLookback = TimeSpan.FromDays(30);
    private readonly Dictionary<string, ApplicationBaseline> baselines = [];
    private readonly AnomalyTracker tracker = new(AnomalySettings.Balanced.MinimumSustainedDuration, AnomalySettings.Balanced.Cooldown);
    private DateTimeOffset lastBaselineRefresh = DateTimeOffset.MinValue;
    private AppSettings settings = new();
    public event Action<AnomalyEvent>? Anomaly;
    public void UpdateSettings(AppSettings value) => settings = value;
    /// <summary>Suppresses further firing for this (process, direction) until <paramref name="duration"/> has
    /// elapsed (section 10.6's "snooze," distinct from "dismiss" — a UI-only, view-level action — and from
    /// trusting an app permanently). Silently ignores an unrecognized direction string.</summary>
    public void Snooze(string processName, string direction, TimeSpan duration)
    {
        if (Enum.TryParse<AnomalyDirection>(direction, out var parsed)) tracker.Snooze(processName, parsed, DateTimeOffset.UtcNow + duration);
    }

    public async Task IngestAsync(ServiceSnapshot snapshot, CancellationToken token)
    {
        if (!settings.AnomalyDetectionEnabled || snapshot.Availability != "Measured" || snapshot.Applications.Count == 0) return;
        var now = snapshot.Timestamp;
        if (now - lastBaselineRefresh >= BaselineRefreshInterval)
        {
            await RefreshBaselinesAsync(snapshot.Applications.Select(a => a.ProcessName), token);
            lastBaselineRefresh = now;
        }
        var effectiveSettings = AnomalySettings.Balanced with { SensitivityStdDevMultiple = settings.AnomalySensitivity };
        foreach (var sample in snapshot.Applications)
        {
            bool trusted = settings.TrustedApplications.Contains(sample.ProcessName, StringComparer.OrdinalIgnoreCase);
            var baseline = baselines.GetValueOrDefault(sample.ProcessName, ApplicationBaseline.Empty(sample.ProcessName));
            foreach (var (direction, rate) in new (AnomalyDirection, double)[]
                { (AnomalyDirection.Download, sample.ReceivedBytesPerSecond), (AnomalyDirection.Upload, sample.SentBytesPerSecond) })
            {
                var verdict = AnomalyEvaluator.Evaluate(baseline, direction, rate, effectiveSettings, trusted);
                bool fire = tracker.ShouldFire(sample.ProcessName, direction, verdict.Availability == AnomalyAvailability.Anomalous, now);
                if (!fire) continue;
                bool notify = settings.AlertsEnabled && !QuietHours.IsQuiet(settings, DateTime.Now.Hour);
                var anomaly = new AnomalyEvent(0, now, sample.ProcessName, direction.ToString(), verdict.Severity!.Value.ToString(),
                    verdict.CurrentBytesPerSecond, verdict.BaselineMeanBytesPerSecond, verdict.DeviationMultiple ?? 0, verdict.Explanation, notify);
                try
                {
                    long id = await store.SaveAnomalyEventAsync(anomaly, token);
                    Anomaly?.Invoke(anomaly with { Id = id });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { logger.LogError("Anomaly event persist failed: {Type}", ex.GetType().Name); }
            }
        }
    }

    private async Task RefreshBaselinesAsync(IEnumerable<string> processNames, CancellationToken token)
    {
        var from = DateTimeOffset.UtcNow - BaselineLookback;
        foreach (var name in processNames.Distinct())
        {
            try
            {
                var series = await store.GetApplicationMinuteSeriesAsync(name, from, token);
                baselines[name] = BaselineCalculator.Compute(name,
                    series.Select(p => new BaselineCalculator.MinutePoint(p.Minute, p.ReceivedBytes, p.SentBytes, p.CoveredSeconds)).ToArray());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError("Baseline refresh failed for {Name}: {Type}", name, ex.GetType().Name); }
        }
    }
}
