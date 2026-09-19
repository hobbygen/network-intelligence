using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Contracts;

public sealed record DailyAdapterUsage(UsageReportDay Day, long? DownloadBytes, long? UploadBytes, long Samples,
    double StoredCoveredSeconds, double CoveredSeconds, bool OverlappingCoverage)
{
    public bool HasMeasurements => Samples > 0;
    public double? CoveragePercent => Day.ExpectedSeconds > 0 ? 100 * CoveredSeconds / Day.ExpectedSeconds : null;
    public string Status => !HasMeasurements ? "No data" : OverlappingCoverage ? "Overlapping coverage; totals may be duplicated"
        : CoveredSeconds < Day.ExpectedSeconds - 1 ? "Partial coverage" : "Covered (minute precision)";
}

public sealed record AdapterUsageReport(string AdapterId, string AdapterName, IReadOnlyList<DailyAdapterUsage> Days)
{
    public bool HasMeasurements => Days.Any(day => day.HasMeasurements);
    public long? DownloadBytes => HasMeasurements ? Days.Sum(day => day.DownloadBytes ?? 0) : null;
    public long? UploadBytes => HasMeasurements ? Days.Sum(day => day.UploadBytes ?? 0) : null;
    public double CoveredSeconds => Days.Sum(day => day.CoveredSeconds);
    public double ExpectedSeconds => Days.Sum(day => day.Day.ExpectedSeconds);
    public double? CoveragePercent => ExpectedSeconds > 0 ? 100 * CoveredSeconds / ExpectedSeconds : null;
    public bool OverlappingCoverage => Days.Any(day => day.OverlappingCoverage);
}

public sealed record ReportApplicationUsage(string ProcessName, long DownloadBytes, long UploadBytes);

public sealed record UsageReport(UsageReportRange Range, IReadOnlyList<AdapterUsageReport> Adapters,
    IReadOnlyList<ReportApplicationUsage> Applications)
{
    public const string AdapterSource = "NetworkInterface counter deltas; UTC minute aggregates assigned by interval end";
    public const string ApplicationSource = "MonitoringService kernel ETW; UTC minute aggregates; grouped by process name";
    public const string CoverageDetail = "Approximate recorded coverage, capped per minute and at elapsed time. Missing data is not zero usage. Minute boundaries are not split; retention, downtime and counter resets can leave gaps.";
    public const string ApplicationDetail = "All interfaces, independent of the selected adapter. Process-name grouping is not stable application identity. Service coverage is unknown; absent rows do not prove zero usage. Accuracy is not certified beyond narrow loopback validation.";
}
