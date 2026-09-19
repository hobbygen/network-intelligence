using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.Infrastructure;

public static partial class ExportService
{
    /// <summary>Exports the displayed snapshot and selected adapter, plus the full all-interface app breakdown.</summary>
    public static async Task WriteUsageReportAsync(string path, UsageReport report, string? adapterId, bool json, CancellationToken token)
    {
        var adapter = adapterId is null ? null : report.Adapters.SingleOrDefault(item => item.AdapterId == adapterId)
            ?? throw new ArgumentException("The selected adapter is not in this report.", nameof(adapterId));
        var days = adapter?.Days ?? report.Range.Days.Select(day => new DailyAdapterUsage(day, null, null, 0, 0, 0, false)).ToArray();
        if (json)
        {
            var payload = new
            {
                SchemaVersion = 1, ExportedUtc = DateTimeOffset.UtcNow, Unit = "bytes", report.Range,
                AdapterSource = UsageReport.AdapterSource, CoverageDetail = UsageReport.CoverageDetail,
                Adapter = adapter, Days = days, ApplicationSource = UsageReport.ApplicationSource,
                ApplicationScope = UsageReport.ApplicationDetail, report.Applications
            };
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            await JsonSerializer.SerializeAsync(stream, payload,
                new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }, token);
            return;
        }

        var buffer = new StringBuilder("RecordType,Period,StartDate,EndDate,FromUtc,ToUtc,TimeZone,AsOfUtc,AdapterId,Name,Date,DownloadBytes,UploadBytes,Samples,StoredCoveredSeconds,CoveredSeconds,ExpectedSeconds,CoveragePercent,Status,Source,Scope\r\n");
        string Number(object? value) => value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : "";
        void Row(string kind, string name, string date, long? down, long? up, long? samples, double? stored,
            double? covered, double? expected, double? percent, string status, string source, string scope)
        {
            buffer.AppendLine(string.Join(",", new[]
            {
                kind, report.Range.Period.ToString(), report.Range.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                report.Range.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), report.Range.FromUtc.ToString("O"),
                report.Range.ToUtc.ToString("O"), Csv(report.Range.TimeZoneId), report.Range.AsOfUtc.ToString("O"),
                Csv(kind == "Application" ? "" : adapter?.AdapterId ?? ""), Csv(name), date,
                Number(down), Number(up), Number(samples), Number(stored), Number(covered), Number(expected), Number(percent),
                Csv(status), Csv(source), Csv(scope)
            }));
        }
        Row("AdapterSummary", adapter?.AdapterName ?? "No recorded adapter", "", adapter?.DownloadBytes, adapter?.UploadBytes,
            adapter?.Days.Sum(day => day.Samples), adapter?.Days.Sum(day => day.StoredCoveredSeconds), adapter?.CoveredSeconds ?? 0,
            report.Range.ExpectedSeconds, adapter?.CoveragePercent, adapter?.OverlappingCoverage == true ? "Overlapping coverage; totals may be duplicated" : "Measured intervals only",
            UsageReport.AdapterSource, UsageReport.CoverageDetail);
        foreach (var day in days)
            Row("AdapterDay", adapter?.AdapterName ?? "", day.Day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                day.DownloadBytes, day.UploadBytes, day.Samples, day.StoredCoveredSeconds, day.CoveredSeconds,
                day.Day.ExpectedSeconds, day.CoveragePercent, day.Status, UsageReport.AdapterSource, "Selected adapter only; missing measurements have empty byte fields");
        foreach (var app in report.Applications)
            Row("Application", app.ProcessName, "", app.DownloadBytes, app.UploadBytes, null, null, null, null, null,
                "Recorded service traffic only; coverage unknown", UsageReport.ApplicationSource, UsageReport.ApplicationDetail);
        await File.WriteAllTextAsync(path, buffer.ToString(), new UTF8Encoding(true), token);
    }
}
