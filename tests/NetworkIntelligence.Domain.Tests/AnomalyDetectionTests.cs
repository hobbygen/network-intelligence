using NetworkIntelligence.Application;
using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Domain.Tests;

public class BaselineCalculatorTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static BaselineCalculator.MinutePoint Point(int minute, long received, long sent, double seconds = 60) =>
        new(Start.AddMinutes(minute), received, sent, seconds);
    [Fact] public void NoHistoryProducesEmptyBaseline() =>
        Assert.Equal(0, BaselineCalculator.Compute("x", []).SampleCount);
    [Fact] public void ComputesMeanRateFromCoveredSeconds()
    {
        // 6000 bytes over 60s = 100 bytes/s, consistently
        var baseline = BaselineCalculator.Compute("x", [Point(0, 6000, 0), Point(1, 6000, 0), Point(2, 6000, 0)]);
        Assert.Equal(3, baseline.SampleCount);
        Assert.Equal(100d, baseline.DownloadMeanBytesPerSecond, 3);
        Assert.Equal(0d, baseline.DownloadStdDevBytesPerSecond, 3);
    }
    [Fact] public void PartialCoverageUsesActualSecondsNotSixty()
    {
        // 500 bytes over 5s (one MonitoringService window) = 100 bytes/s, same as a full 6000/60 bucket
        var baseline = BaselineCalculator.Compute("x", [Point(0, 500, 0, seconds: 5)]);
        Assert.Equal(100d, baseline.DownloadMeanBytesPerSecond, 3);
    }
    [Fact] public void ZeroCoveredSecondsBucketsAreExcluded() =>
        Assert.Equal(0, BaselineCalculator.Compute("x", [Point(0, 100, 0, seconds: 0)]).SampleCount);
    [Fact] public void SingleExtremeOutlierIsTrimmedFromTheBaseline()
    {
        var history = Enumerable.Range(0, 20).Select(i => Point(i, 6000, 0)).ToList(); // 100 B/s steady
        history.Add(Point(20, 6_000_000, 0)); // one wildly anomalous minute: 100,000 B/s
        var baseline = BaselineCalculator.Compute("x", history);
        // The trimmed mean should stay close to the steady 100 B/s, not be dragged toward the outlier.
        Assert.True(baseline.DownloadMeanBytesPerSecond < 1000, $"mean was {baseline.DownloadMeanBytesPerSecond}");
    }
}

public class AnomalyEvaluatorTests
{
    private static readonly AnomalySettings Settings = AnomalySettings.Balanced;
    // ~16 Mbps mean with 10% stddev — realistic enough to sit well above the 50 KB/s minimum-bandwidth floor
    // (a low-magnitude baseline would make every case BelowThreshold rather than exercising the deviation check).
    private static ApplicationBaseline Learned(double mean = 2_000_000, double stdDev = 200_000, int samples = 100, double days = 10) =>
        new("chrome", samples, TimeSpan.FromDays(days), mean, stdDev, mean, stdDev, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(days));
    [Fact] public void NormalTrafficWithinBaselineIsNormal()
    {
        var verdict = AnomalyEvaluator.Evaluate(Learned(), AnomalyDirection.Download, 2_200_000, Settings, trusted: false);
        Assert.Equal(AnomalyAvailability.Normal, verdict.Availability);
    }
    [Fact] public void SustainedHighDownloadIsAnomalous()
    {
        var verdict = AnomalyEvaluator.Evaluate(Learned(), AnomalyDirection.Download, 20_000_000, Settings, trusted: false);
        Assert.Equal(AnomalyAvailability.Anomalous, verdict.Availability);
        Assert.Equal(AnomalyDirection.Download, verdict.Direction);
        Assert.Equal(AnomalySeverity.High, verdict.Severity);
    }
    [Fact] public void HighUploadIsEvaluatedIndependentlyOfDownload()
    {
        var baseline = Learned(); // symmetric mean/stddev for both directions in this fixture
        var download = AnomalyEvaluator.Evaluate(baseline, AnomalyDirection.Download, 2_200_000, Settings, trusted: false);
        var upload = AnomalyEvaluator.Evaluate(baseline, AnomalyDirection.Upload, 20_000_000, Settings, trusted: false);
        Assert.Equal(AnomalyAvailability.Normal, download.Availability);
        Assert.Equal(AnomalyAvailability.Anomalous, upload.Availability);
    }
    [Fact] public void NewApplicationWithNoBaselineIsNeverFlaggedAnomalous() =>
        Assert.Equal(AnomalyAvailability.NoBaseline, AnomalyEvaluator.Evaluate(ApplicationBaseline.Empty("new.exe"), AnomalyDirection.Download, 50_000_000, Settings, trusted: false).Availability);
    [Fact] public void ApplicationBelowMinimumSamplesIsInsufficientHistoryNotAnomalous() =>
        Assert.Equal(AnomalyAvailability.InsufficientHistory, AnomalyEvaluator.Evaluate(Learned(samples: 5, days: 10), AnomalyDirection.Download, 50_000_000, Settings, trusted: false).Availability);
    [Fact] public void ApplicationBelowMinimumObservationDaysIsInsufficientHistory() =>
        Assert.Equal(AnomalyAvailability.InsufficientHistory, AnomalyEvaluator.Evaluate(Learned(samples: 100, days: 1), AnomalyDirection.Download, 50_000_000, Settings, trusted: false).Availability);
    [Fact] public void TrustedApplicationIsNeverFlaggedRegardlessOfRate() =>
        Assert.Equal(AnomalyAvailability.Trusted, AnomalyEvaluator.Evaluate(Learned(), AnomalyDirection.Download, 50_000_000, Settings, trusted: true).Availability);
    [Fact] public void BelowBandwidthFloorIsNeverAnomalousEvenWithHugeDeviationMultiple()
    {
        // A baseline of near-zero mean/stddev would make even a tiny rate look like a huge multiple; the floor prevents that.
        var tinyBaseline = new ApplicationBaseline("quiet.exe", 100, TimeSpan.FromDays(10), 1, 1, 1, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(10));
        var verdict = AnomalyEvaluator.Evaluate(tinyBaseline, AnomalyDirection.Download, 1000, Settings, trusted: false);
        Assert.Equal(AnomalyAvailability.BelowThreshold, verdict.Availability);
    }
    [Fact] public void NeverAssertsMaliceInExplanation()
    {
        // The explanation may reassuringly *disclaim* malice ("not a malware determination") — the spec
        // requirement (section 10.6) is that it must never *assert* it.
        var verdict = AnomalyEvaluator.Evaluate(Learned(), AnomalyDirection.Download, 20_000_000, Settings, trusted: false);
        foreach (var claim in new[] { "is malware", "is a virus", "is infected", "is malicious" })
            Assert.DoesNotContain(claim, verdict.Explanation, StringComparison.OrdinalIgnoreCase);
    }
}

public class QuietHoursTests
{
    private static NetworkIntelligence.Contracts.AppSettings Settings(bool enabled, int start, int end) =>
        new() { QuietHoursEnabled = enabled, QuietStartHour = start, QuietEndHour = end };
    [Fact] public void DisabledIsNeverQuiet() => Assert.False(QuietHours.IsQuiet(Settings(false, 22, 7), 23));
    [Fact] public void OvernightRangeWrapsPastMidnight()
    {
        var settings = Settings(true, 22, 7);
        Assert.True(QuietHours.IsQuiet(settings, 23)); Assert.True(QuietHours.IsQuiet(settings, 3));
        Assert.False(QuietHours.IsQuiet(settings, 12));
    }
    [Fact] public void SameDayRangeDoesNotWrap()
    {
        var settings = Settings(true, 9, 17);
        Assert.True(QuietHours.IsQuiet(settings, 12));
        Assert.False(QuietHours.IsQuiet(settings, 20));
    }
    [Fact] public void EqualStartAndEndMeansAlwaysQuiet() => Assert.True(QuietHours.IsQuiet(Settings(true, 5, 5), 0));
}

public class AnomalyTrackerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    [Fact] public void BriefSpikeNeverFires()
    {
        var tracker = new AnomalyTracker(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start));
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, false, Start.AddSeconds(4))); // spike ends before sustained threshold
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, false, Start.AddSeconds(40)));
    }
    [Fact] public void SustainedSpikeFiresOnceThresholdReached()
    {
        var tracker = new AnomalyTracker(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start));
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddSeconds(20)));
        Assert.True(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddSeconds(31)));
    }
    [Fact] public void CooldownSuppressesRepeatFiringUntilElapsed()
    {
        var tracker = new AnomalyTracker(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        tracker.ShouldFire("x", AnomalyDirection.Download, true, Start);
        Assert.True(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddSeconds(31)));
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddMinutes(5))); // still within cooldown
        Assert.True(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddMinutes(16))); // cooldown elapsed
    }
    [Fact] public void DroppingBelowThresholdResetsTheSustainedTimer()
    {
        var tracker = new AnomalyTracker(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        tracker.ShouldFire("x", AnomalyDirection.Download, true, Start);
        tracker.ShouldFire("x", AnomalyDirection.Download, false, Start.AddSeconds(20)); // dips back to normal
        tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddSeconds(25)); // spikes again — timer restarts
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddSeconds(40))); // only 15s since restart
        Assert.True(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddSeconds(56)));
    }
    [Fact] public void DownloadAndUploadAreTrackedIndependentlyPerProcess()
    {
        var tracker = new AnomalyTracker(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        tracker.ShouldFire("x", AnomalyDirection.Download, true, Start);
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Upload, true, Start.AddSeconds(31))); // upload only just started
        Assert.True(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddSeconds(31)));
    }
    [Fact] public void DifferentProcessNamesAreTrackedIndependently()
    {
        var tracker = new AnomalyTracker(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        tracker.ShouldFire("a", AnomalyDirection.Download, true, Start);
        Assert.False(tracker.ShouldFire("b", AnomalyDirection.Download, true, Start.AddSeconds(31)));
        Assert.True(tracker.ShouldFire("a", AnomalyDirection.Download, true, Start.AddSeconds(31)));
    }
    [Fact] public void SnoozeSuppressesFiringUntilItLapsesThenFiresImmediately()
    {
        var tracker = new AnomalyTracker(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        tracker.Snooze("x", AnomalyDirection.Download, Start.AddHours(1));
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start)); // first sighting
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddSeconds(31))); // sustained+cooled down, but snoozed
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddMinutes(59))); // still snoozed
        // Snooze lapsed: fires immediately — a suppressed firing doesn't reset the sustained timer or start a
        // phantom cooldown, so the anomaly doesn't need to re-accumulate 30s once it's visible again.
        Assert.True(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddHours(1).AddSeconds(1)));
    }
    [Fact] public void SnoozeIsPerProcessAndDirection()
    {
        var tracker = new AnomalyTracker(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        tracker.Snooze("x", AnomalyDirection.Download, Start.AddHours(1));
        tracker.ShouldFire("x", AnomalyDirection.Download, true, Start);
        tracker.ShouldFire("x", AnomalyDirection.Upload, true, Start);
        tracker.ShouldFire("y", AnomalyDirection.Download, true, Start);
        Assert.False(tracker.ShouldFire("x", AnomalyDirection.Download, true, Start.AddSeconds(31))); // snoozed
        Assert.True(tracker.ShouldFire("x", AnomalyDirection.Upload, true, Start.AddSeconds(31))); // different direction
        Assert.True(tracker.ShouldFire("y", AnomalyDirection.Download, true, Start.AddSeconds(31))); // different process
    }
}
