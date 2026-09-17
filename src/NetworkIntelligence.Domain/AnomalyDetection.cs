namespace NetworkIntelligence.Domain;

public enum AnomalyDirection { Download, Upload }
public enum AnomalySeverity { Low, Medium, High }
public enum AnomalyAvailability { NoBaseline, InsufficientHistory, BelowThreshold, Trusted, Normal, Anomalous }

/// <summary>One process-name's learned traffic profile. Grouping is by process name only — an approximation of
/// "application identity" (see docs/DECISIONS.md ADR-010/011), not a stable one. Mean/stddev come from a
/// two-pass, outlier-trimmed Welford computation (BaselineCalculator), not percentiles/MAD — a deliberate,
/// documented simplification for v1, not the richer statistic sketched as a proposal in ARCHITECTURE_REVIEW.md.</summary>
public sealed record ApplicationBaseline(string ProcessName, int SampleCount, TimeSpan ObservationDuration,
    double DownloadMeanBytesPerSecond, double DownloadStdDevBytesPerSecond,
    double UploadMeanBytesPerSecond, double UploadStdDevBytesPerSecond,
    DateTimeOffset First, DateTimeOffset Last)
{
    public static ApplicationBaseline Empty(string processName) => new(processName, 0, TimeSpan.Zero, 0, 0, 0, 0, default, default);
}

public sealed record AnomalySettings(int MinimumSamples, double MinimumObservationDays, double MinimumBytesPerSecond,
    double SensitivityStdDevMultiple, TimeSpan MinimumSustainedDuration, TimeSpan Cooldown)
{
    /// <summary>Starting parameters proposed in docs/ARCHITECTURE_REVIEW.md's anomaly design section — not
    /// validated against real false-positive-rate testing (that needs days/weeks of live usage data, tracked
    /// as outstanding in docs/DECISIONS.md ADR-011).</summary>
    public static AnomalySettings Balanced { get; } = new(
        MinimumSamples: 60, MinimumObservationDays: 7, MinimumBytesPerSecond: 50_000,
        SensitivityStdDevMultiple: 3.0, MinimumSustainedDuration: TimeSpan.FromSeconds(30), Cooldown: TimeSpan.FromMinutes(15));
}

public sealed record AnomalyVerdict(AnomalyAvailability Availability, AnomalyDirection Direction, AnomalySeverity? Severity,
    double CurrentBytesPerSecond, double BaselineMeanBytesPerSecond, double? DeviationMultiple, string Explanation);

/// <summary>Computes a per-process baseline from stored minute-bucket history. Pure function: no I/O, no clock
/// reads — the caller supplies history already queried from storage.</summary>
public static class BaselineCalculator
{
    public sealed record MinutePoint(DateTimeOffset Minute, long ReceivedBytes, long SentBytes, double CoveredSeconds);

    public static ApplicationBaseline Compute(string processName, IReadOnlyList<MinutePoint> history)
    {
        var usable = history.Where(h => h.CoveredSeconds > 0).OrderBy(h => h.Minute).ToArray();
        if (usable.Length == 0) return ApplicationBaseline.Empty(processName);
        var downRates = usable.Select(h => h.ReceivedBytes / h.CoveredSeconds).ToArray();
        var upRates = usable.Select(h => h.SentBytes / h.CoveredSeconds).ToArray();
        var (downMean, downStdDev) = TrimmedMeanStdDev(downRates);
        var (upMean, upStdDev) = TrimmedMeanStdDev(upRates);
        return new(processName, usable.Length, usable[^1].Minute - usable[0].Minute,
            downMean, downStdDev, upMean, upStdDev, usable[0].Minute, usable[^1].Minute);
    }

    /// <summary>Welford's online mean/variance, then one outlier-trimmed re-pass excluding samples beyond 4
    /// standard deviations of the first pass — "robustly limit anomalous samples entering baseline"
    /// (ARCHITECTURE_REVIEW.md) without the cost of full percentile/MAD tracking.</summary>
    private static (double Mean, double StdDev) TrimmedMeanStdDev(IReadOnlyList<double> values)
    {
        var (mean, stdDev) = Welford(values);
        if (stdDev <= 0 || values.Count < 4) return (mean, stdDev);
        var trimmed = values.Where(v => Math.Abs(v - mean) <= 4 * stdDev).ToArray();
        return trimmed.Length >= 2 ? Welford(trimmed) : (mean, stdDev);
    }

    private static (double Mean, double StdDev) Welford(IReadOnlyList<double> values)
    {
        double mean = 0, m2 = 0; int n = 0;
        foreach (var value in values)
        {
            n++;
            double delta = value - mean;
            mean += delta / n;
            m2 += delta * (value - mean);
        }
        return (mean, n > 1 ? Math.Sqrt(m2 / (n - 1)) : 0);
    }
}

/// <summary>Momentary, stateless evaluation of one rate sample against a baseline. Does not know about
/// sustained duration or cooldown — that is AnomalyTracker's job (NetworkIntelligence.Application), deliberately
/// separated so this stays a pure, exhaustively unit-testable function.</summary>
public static class AnomalyEvaluator
{
    public static AnomalyVerdict Evaluate(ApplicationBaseline baseline, AnomalyDirection direction,
        double currentBytesPerSecond, AnomalySettings settings, bool trusted)
    {
        double mean = direction == AnomalyDirection.Download ? baseline.DownloadMeanBytesPerSecond : baseline.UploadMeanBytesPerSecond;
        double stdDev = direction == AnomalyDirection.Download ? baseline.DownloadStdDevBytesPerSecond : baseline.UploadStdDevBytesPerSecond;
        if (trusted)
            return new(AnomalyAvailability.Trusted, direction, null, currentBytesPerSecond, mean, null,
                "Application is marked trusted; excluded from anomaly evaluation.");
        if (baseline.SampleCount == 0)
            return new(AnomalyAvailability.NoBaseline, direction, null, currentBytesPerSecond, mean, null,
                "No baseline yet — this application has not been observed long enough to have learned history.");
        if (baseline.SampleCount < settings.MinimumSamples || baseline.ObservationDuration.TotalDays < settings.MinimumObservationDays)
            return new(AnomalyAvailability.InsufficientHistory, direction, null, currentBytesPerSecond, mean, null,
                $"Learning period: {baseline.SampleCount}/{settings.MinimumSamples} samples, {baseline.ObservationDuration.TotalDays:0.0}/{settings.MinimumObservationDays:0} days observed.");
        if (currentBytesPerSecond < settings.MinimumBytesPerSecond)
            return new(AnomalyAvailability.BelowThreshold, direction, null, currentBytesPerSecond, mean, null,
                "Below the minimum bandwidth floor; never alerted regardless of statistical deviation.");
        // Floor the effective standard deviation so a baseline with almost no variance (e.g. a consistently-idle
        // app) doesn't turn every small absolute change into an extreme deviation multiple.
        double effectiveStdDev = Math.Max(stdDev, mean * 0.05);
        double deviation = effectiveStdDev > 0 ? (currentBytesPerSecond - mean) / effectiveStdDev : 0;
        if (deviation < settings.SensitivityStdDevMultiple)
            return new(AnomalyAvailability.Normal, direction, null, currentBytesPerSecond, mean, deviation,
                "Within normal range for this application's baseline.");
        var severity = deviation >= settings.SensitivityStdDevMultiple * 2.5 ? AnomalySeverity.High
            : deviation >= settings.SensitivityStdDevMultiple * 1.5 ? AnomalySeverity.Medium : AnomalySeverity.Low;
        string dirWord = direction == AnomalyDirection.Download ? "download" : "upload";
        return new(AnomalyAvailability.Anomalous, direction, severity, currentBytesPerSecond, mean, deviation,
            $"{FormatRate(currentBytesPerSecond)} {dirWord}, {deviation:0.0}x its usual variation above a baseline average of {FormatRate(mean)}. " +
            "This reflects bandwidth only and is not a malware or security determination.");
    }

    private static string FormatRate(double bytesPerSecond) =>
        bytesPerSecond < 125_000 ? $"{bytesPerSecond * 8 / 1_000:0.0} Kbps" : $"{bytesPerSecond * 8 / 1_000_000:0.00} Mbps";
}
