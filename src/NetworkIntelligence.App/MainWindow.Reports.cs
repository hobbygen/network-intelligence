using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetworkIntelligence.App.ViewModels;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Domain;
using NetworkIntelligence.Infrastructure;
using Windows.Storage.Pickers;
namespace NetworkIntelligence.App;

public sealed partial class MainWindow
{
    private UsageReportViewModel Reports { get; } = new();
    private bool reportsReady, updatingReport;
    private int reportRevision;
    private string? reportAdapterId;
    private CancellationTokenSource? reportCancellation;

    private async Task InitializeUsageReportsAsync()
    {
        ReportStartDate.MaxDate = ReportEndDate.MaxDate = DateTimeOffset.Now;
        ReportStartDate.Date = DateTimeOffset.Now.AddDays(-6);
        ReportEndDate.Date = DateTimeOffset.Now;
        reportsReady = true;
        await RefreshUsageReportAsync();
    }

    private async Task RefreshUsageReportAsync()
    {
        if (!reportsReady) return;
        int revision = ++reportRevision;
        reportCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        reportCancellation = cancellation;
        string? previousAdapter = reportAdapterId ?? ViewModel.Selected?.Id;
        updatingReport = true;
        Reports.Clear();
        updatingReport = false;
        ReportExportButton.IsEnabled = false;
        ReportBusy.IsActive = true;
        ReportStatus.Text = "Loading recorded usage…";
        try
        {
            var range = UsageReportRange.Create((UsageReportPeriod)ReportPeriodPicker.SelectedIndex, DateTimeOffset.Now, TimeZoneInfo.Local,
                ReportStartDate.Date is { } start ? DateOnly.FromDateTime(start.DateTime) : null,
                ReportEndDate.Date is { } end ? DateOnly.FromDateTime(end.DateTime) : null);
            var report = await Task.Run(() => store.GetUsageReportAsync(range, cancellation.Token), cancellation.Token);
            if (revision != reportRevision || cancellation.IsCancellationRequested) return;
            updatingReport = true;
            try
            {
                Reports.SetReport(report);
                ReportAdapterPicker.SelectedItem = Reports.Adapters.FirstOrDefault(adapter => adapter.AdapterId == previousAdapter) ?? Reports.Adapters.FirstOrDefault();
                Reports.SelectAdapter(ReportAdapterPicker.SelectedItem as AdapterUsageReport);
                reportAdapterId = Reports.Adapter?.AdapterId;
            }
            finally { updatingReport = false; }
            ReportStatus.Text = report.Adapters.Count == 0 ? "No retained adapter measurements yet. Start monitoring, then refresh." : "Report ready · refresh to include newer measurements";
            ReportExportButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (revision == reportRevision) ReportStatus.Text = "Report unavailable: " + ex.Message; }
        finally
        {
            if (revision == reportRevision) { ReportBusy.IsActive = false; reportCancellation = null; }
        }
    }

    private async void ReportPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!reportsReady) return;
        ReportCustomDates.Visibility = (UsageReportPeriod)ReportPeriodPicker.SelectedIndex == UsageReportPeriod.Custom ? Visibility.Visible : Visibility.Collapsed;
        await RefreshUsageReportAsync();
    }
    private async void ReportDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (reportsReady && (UsageReportPeriod)ReportPeriodPicker.SelectedIndex == UsageReportPeriod.Custom) await RefreshUsageReportAsync();
    }
    private void ReportAdapterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updatingReport)
        {
            Reports.SelectAdapter(ReportAdapterPicker.SelectedItem as AdapterUsageReport);
            reportAdapterId = Reports.Adapter?.AdapterId;
        }
    }
    private async void RefreshReportClicked(object sender, RoutedEventArgs e) => await RefreshUsageReportAsync();
    private async void ExportReportClicked(object sender, RoutedEventArgs e) => await ExportUsageReportAsync();
    private async Task ExportUsageReportAsync()
    {
        // Capture the displayed snapshot before opening the picker; never silently query a different range.
        var report = Reports.Report;
        string? adapterId = Reports.Adapter?.AdapterId;
        if (report is null) { ReportStatus.Text = "Load a valid report before exporting."; return; }
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"NetworkIntelligence-Usage-{report.Range.StartDate:yyyyMMdd}-{report.Range.EndDate:yyyyMMdd}" };
            picker.FileTypeChoices.Add("JSON", [".json"]);
            picker.FileTypeChoices.Add("CSV", [".csv"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await ExportService.WriteUsageReportAsync(file.Path, report, adapterId, file.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase), CancellationToken.None);
            ReportStatus.Text = "Report exported: " + file.Name;
        }
        catch (Exception ex) { ReportStatus.Text = "Report export failed: " + ex.Message; }
    }
}
