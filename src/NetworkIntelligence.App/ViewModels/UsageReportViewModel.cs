using System.Collections.ObjectModel;
using System.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using NetworkIntelligence.Contracts;
using SkiaSharp;
namespace NetworkIntelligence.App.ViewModels;

public sealed class UsageReportViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public UsageReport? Report { get; private set; }
    public AdapterUsageReport? Adapter { get; private set; }
    public ObservableCollection<AdapterUsageReport> Adapters { get; } = [];
    public ObservableCollection<ReportDayDisplay> Days { get; } = [];
    public ObservableCollection<ReportAppDisplay> Applications { get; } = [];
    public ISeries[] Series { get; private set; } = [];
    public Axis[] XAxes { get; private set; } = [];
    public Axis[] YAxes { get; private set; } = [];
    public string ChartNote { get; private set; } = "Daily totals · missing measurements are gaps";
    public string PeriodText { get; private set; } = "Choose a period to view recorded usage.";
    public string Download => FormatBytes(Adapter?.DownloadBytes);
    public string Upload => FormatBytes(Adapter?.UploadBytes);
    public string Coverage => FormatPercent(Adapter?.CoveragePercent);
    public string CoverageText => Report is null ? "No report loaded" : $"{FormatDuration(Adapter?.CoveredSeconds ?? 0)} of {FormatDuration(Report.Range.ExpectedSeconds)} elapsed recorded";
    public string DataStatus => Report is null ? "" : Adapter?.HasMeasurements != true
        ? "No measurements for this adapter and period. Missing data is not zero usage."
        : Adapter.OverlappingCoverage ? "Overlapping coverage detected. Multiple collectors may have duplicated totals; coverage is capped."
        : "Totals include recorded intervals only. Gaps from downtime, retention or counter resets are not estimated.";
    public string AppStatus => Report is null ? "No report loaded" : Applications.Count == 0
        ? "No application traffic recorded in this period. The monitoring service may have been unavailable, or no traffic was captured."
        : $"Top {Applications.Count} of {Report.Applications.Count} process names · export includes all names · service coverage unknown";

    public void Clear()
    {
        Report = null; Adapter = null; Adapters.Clear(); Days.Clear(); Applications.Clear(); Series = []; XAxes = [];
        PeriodText = "No report loaded";
        Changed();
    }
    public void SetReport(UsageReport report)
    {
        Report = report;
        Adapters.Clear(); foreach (var adapter in report.Adapters) Adapters.Add(adapter);
        Applications.Clear(); foreach (var app in report.Applications.Take(10)) Applications.Add(new(app));
        PeriodText = $"{report.Range.StartDate:dd MMM yyyy} – {report.Range.EndDate:dd MMM yyyy} · {report.Range.TimeZoneId}\nSnapshot as of {report.Range.AsOfUtc.ToLocalTime():g} · weeks start Monday · daily totals";
        SelectAdapter(report.Adapters.FirstOrDefault());
    }
    public void SelectAdapter(AdapterUsageReport? adapter)
    {
        Adapter = adapter;
        var days = adapter?.Days ?? Report?.Range.Days.Select(day => new DailyAdapterUsage(day, null, null, 0, 0, 0, false)).ToArray() ?? [];
        Days.Clear(); foreach (var day in days) Days.Add(new(day));
        double largest = days.Select(day => (double)Math.Max(day.DownloadBytes ?? 0, day.UploadBytes ?? 0)).DefaultIfEmpty(0).Max();
        (double divisor, string unit) = largest >= 1L << 30 ? (1L << 30, "GiB") : largest >= 1L << 20 ? (1L << 20, "MiB") : largest >= 1024 ? (1024, "KiB") : (1, "bytes");
        Series = [
            new ColumnSeries<double?> { Name = $"Download ({unit})", Values = days.Select(day => day.DownloadBytes / (double?)divisor).ToArray(), Fill = new SolidColorPaint(SKColor.Parse("#009F90")) },
            new ColumnSeries<double?> { Name = $"Upload ({unit})", Values = days.Select(day => day.UploadBytes / (double?)divisor).ToArray(), Fill = new SolidColorPaint(SKColor.Parse("#427DD1")) }
        ];
        XAxes = [new() { Labels = days.Select(day => day.Day.Date.ToString("dd MMM")).ToArray(), MinStep = 1,
            MinLimit = -0.5, MaxLimit = Math.Max(0.5, days.Count - 0.5), LabelsRotation = days.Count > 14 ? -45 : 0 }];
        YAxes = [new() { Name = $"{unit} per day", MinLimit = 0, Labeler = value => value.ToString("0.##") }];
        ChartNote = $"Daily totals in {unit} · gaps mean no measurements · measured zeros are shown as 0 in the daily breakdown";
        Changed();
    }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public static string FormatBytes(long? value) => value is { } bytes ? MainViewModel.Bytes(bytes) : "—";
    public static string FormatPercent(double? value) => value is null ? "—" : value is > 0 and < 0.1 ? "<0.1%" : $"{value:0.0}%";
    public static string FormatDuration(double seconds) => seconds >= 3600 ? $"{seconds / 3600:0.00} h" : seconds >= 60 ? $"{seconds / 60:0.0} min" : $"{seconds:0} s";
}

public sealed record ReportDayDisplay(DailyAdapterUsage Value)
{
    public string Date => Value.Day.Date.ToString("ddd, dd MMM yyyy");
    public string Download => UsageReportViewModel.FormatBytes(Value.DownloadBytes);
    public string Upload => UsageReportViewModel.FormatBytes(Value.UploadBytes);
    public string Coverage => $"{UsageReportViewModel.FormatPercent(Value.CoveragePercent)} · {UsageReportViewModel.FormatDuration(Value.CoveredSeconds)}";
    public string Status => Value.Status;
}
public sealed record ReportAppDisplay(ReportApplicationUsage Value)
{
    public string Name => Value.ProcessName;
    public string Usage => $"↓ {MainViewModel.Bytes(Value.DownloadBytes)}    ↑ {MainViewModel.Bytes(Value.UploadBytes)}";
}
