using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetworkIntelligence.App.ViewModels;
using NetworkIntelligence.Application;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Infrastructure;
using Windows.Storage.Pickers;
using System.Text.Json;

namespace NetworkIntelligence.App;
public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();
    private readonly MonitoringService monitoring;
    private readonly IHistoryStore store;
    private readonly Diagnostics diagnostics;
    private readonly TransferTest transfer;
    private TrayIcon? tray;
    private bool loaded, quitting, closeNotified;
    private CancellationTokenSource? diagnosticCancellation;
    private CancellationTokenSource? speedCancellation;
    private readonly Dictionary<string, DateTimeOffset> lastAlert = [];
    private IReadOnlyList<UsageSummary> usage = [];
    private DiagnosticResult? lastDiagnostic;
    private string currentPage = "Dashboard";
    private MonitoringSnapshot? pendingSnapshot;
    private int snapshotQueued;
    public MainWindow(MonitoringService monitoring, IHistoryStore store, Diagnostics diagnostics, TransferTest transfer)
    {
        this.monitoring = monitoring; this.store = store; this.diagnostics = diagnostics; this.transfer = transfer;
        InitializeComponent();
        Root.DataContext = ViewModel;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 920));
        AppWindow.Closing += Closing;
        monitoring.Snapshot += snapshot =>
        {
            Interlocked.Exchange(ref pendingSnapshot, snapshot);
            if (Interlocked.Exchange(ref snapshotQueued, 1) != 0) return;
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var latest = Interlocked.Exchange(ref pendingSnapshot, null);
                    if (latest is null) return;
                    ViewModel.Apply(latest);
                    if (AdapterPicker.SelectedItem is not AdapterSnapshot picked || picked.Id != ViewModel.Selected?.Id)
                        AdapterPicker.SelectedItem = ViewModel.Adapters.FirstOrDefault(a => a.Id == ViewModel.Selected?.Id);
                    if (currentPage == "Applications") ViewModel.FilterApplications(ApplicationSearch.Text);
                    tray?.Update($"Network Intelligence · {ViewModel.Connection} · ↓ {ViewModel.Download} · ↑ {ViewModel.Upload}");
                }
                finally { Interlocked.Exchange(ref snapshotQueued, 0); }
            })) Interlocked.Exchange(ref snapshotQueued, 0);
        };
        monitoring.Status += message => DispatcherQueue.TryEnqueue(() => ViewModel.Status = message);
        monitoring.ConnectionsChanged += events => DispatcherQueue.TryEnqueue(() => ConnectionChanges(events));
    }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded) return; loaded = true;
        tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), TrayCommand);
        Navigation.SelectedItem = Navigation.MenuItems[0];
        StoragePath.Text = $"Local data: {App.DataDirectory}";
        try
        {
            await Task.Run(() => store.InitializeAsync(CancellationToken.None));
            var settings = await Task.Run(() => store.LoadSettingsAsync(CancellationToken.None));
            monitoring.UpdateSettings(settings); ApplySettings(settings);
            await LoadHistoryAsync();
            ViewModel.Status = "Monitoring locally · no cloud account required";
        }
        catch (Exception ex) { ViewModel.Status = "Local history unavailable: " + ex.Message; }
        monitoring.Start();
        if (Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            try
            {
                await Task.Delay(8000);
                await LoadHistoryAsync();
                var result = new { Adapters = ViewModel.Adapters.Count, PersistedAdapters = usage.Count, Integrity = await Task.Run(() => store.CheckIntegrityAsync(CancellationToken.None)), ViewModel.Status };
                await File.WriteAllTextAsync(Path.Combine(App.DataDirectory, "smoke-result.json"), JsonSerializer.Serialize(result));
                // Render the app's own visual tree for layout verification in automated test runs.
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await bitmap.RenderAsync(Root);
                var pixels = await bitmap.GetPixelsAsync();
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(App.DataDirectory);
                var image = await folder.CreateFileAsync("smoke-preview.png", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                using var stream = await image.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
                await encoder.FlushAsync();
            }
            catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(App.DataDirectory, "smoke-error.txt"), ex.ToString()); }
            finally { await ShutdownAsync(); }
        }
    }
    private void ApplySettings(AppSettings settings)
    {
        Retention.Value = settings.RetentionDays;
        ThemePicker.SelectedIndex = settings.Theme == "Dark" ? 2 : settings.Theme == "Light" ? 1 : 0;
        Root.RequestedTheme = settings.Theme == "Dark" ? ElementTheme.Dark : settings.Theme == "Light" ? ElementTheme.Light : ElementTheme.Default;
        CloseToTray.IsOn = settings.CloseToTray; CollectAddresses.IsOn = settings.CollectAddresses;
        CollectSsid.IsOn = settings.CollectSsid; CollectNames.IsOn = settings.CollectProcessNames;
        AlertsEnabled.IsOn = settings.AlertsEnabled; QuietEnabled.IsOn = settings.QuietHoursEnabled;
        QuietStart.Value = settings.QuietStartHour; QuietEnd.Value = settings.QuietEndHour;
        DiagnosticTarget.Text = settings.DiagnosticTarget;
    }
    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!loaded) return;
        ShowPage(args.IsSettingsSelected ? "Settings" : (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "Dashboard");
    }
    private void ShowPage(string page)
    {
        currentPage = page;
        var pages = new Dictionary<string, FrameworkElement> { ["Dashboard"] = DashboardPage, ["Ethernet"] = EthernetPage, ["Wifi"] = WifiPage, ["Performance"] = PerformancePage, ["Usage"] = UsagePage, ["Applications"] = ApplicationsPage, ["History"] = HistoryPage, ["Diagnostics"] = DiagnosticsPage, ["Settings"] = SettingsPage };
        foreach (var item in pages) item.Value.Visibility = item.Key == page ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page switch { "Dashboard" => "Network overview", "Wifi" => "Wi-Fi", "Performance" => "Network performance", "Usage" => "Bandwidth and data", "Applications" => "Application usage", "History" => "Connection history", _ => page };
        if (page == "Applications") ViewModel.FilterApplications(ApplicationSearch.Text);
    }
    private void AdapterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdapterPicker.SelectedItem is AdapterSnapshot adapter) ViewModel.Selected = adapter;
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (loaded) ViewModel.FilterApplications(ApplicationSearch.Text); }
    private void PauseClicked(object sender, RoutedEventArgs e) => TogglePaused();
    private void TogglePaused()
    {
        monitoring.SetPaused(!monitoring.IsPaused); PauseButton.Content = monitoring.IsPaused ? "Resume monitoring" : "Pause monitoring";
        if (monitoring.IsPaused) tray?.Update("Network Intelligence · monitoring paused");
    }
    private void OpenDiagnostics(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == "Diagnostics");
    }
    private void TrayCommand(string command)
    {
        switch (command)
        {
            case "show": tray?.Show(); Activate(); break;
            case "pause": TogglePaused(); break;
            case "diagnostics": tray?.Show(); Activate(); OpenDiagnostics(this, new()); break;
            case "settings": tray?.Show(); Activate(); Navigation.SelectedItem = Navigation.SettingsItem; break;
            case "exit": _ = ShutdownAsync(); break;
        }
    }
    private async void Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (quitting) return;
        args.Cancel = true;
        if (monitoring.Settings.CloseToTray && tray?.Available == true)
        {
            tray.Hide();
            if (!closeNotified) { tray.Notify("Monitoring continues in the tray", "Use the Network Intelligence tray icon to reopen the window. Choose Exit to stop monitoring."); closeNotified = true; }
        }
        else await ShutdownAsync();
    }
    private async Task ShutdownAsync()
    {
        if (quitting) return;
        quitting = true; diagnosticCancellation?.Cancel(); speedCancellation?.Cancel();
        await monitoring.DisposeAsync(); tray?.Dispose(); Close();
    }
    private void ConnectionChanges(IReadOnlyList<ConnectionEvent> events)
    {
        ViewModel.AddEvents(events);
        var settings = monitoring.Settings;
        if (!settings.AlertsEnabled || IsQuiet(settings, DateTime.Now.Hour)) return;
        foreach (var item in events.Where(e => e.PreviousState == "Up" && e.State != "Up"))
        {
            if (lastAlert.TryGetValue(item.AdapterId, out var last) && item.Timestamp - last < TimeSpan.FromMinutes(5)) continue;
            lastAlert[item.AdapterId] = item.Timestamp;
            tray?.Notify("Network adapter disconnected", $"{item.AdapterName}: {item.PreviousState} → {item.State}. This does not necessarily mean internet connectivity was lost.");
        }
    }
    internal static bool IsQuiet(AppSettings settings, int hour) => settings.QuietHoursEnabled && (settings.QuietStartHour == settings.QuietEndHour
        || (settings.QuietStartHour < settings.QuietEndHour ? hour >= settings.QuietStartHour && hour < settings.QuietEndHour : hour >= settings.QuietStartHour || hour < settings.QuietEndHour));
    private async void DiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        if (diagnosticCancellation is not null) return;
        diagnosticCancellation = new(); DiagnosticButton.IsEnabled = false; DiagnosticProgress.IsActive = true;
        ViewModel.DiagnosticText = "Running bounded DNS and ICMP tests…"; ViewModel.Changed(nameof(ViewModel.DiagnosticText));
        try
        {
            var result = await diagnostics.RunAsync(DiagnosticTarget.Text.Trim(), diagnosticCancellation.Token);
            lastDiagnostic = result;
            string Ms(double? value) => value is null ? "Unavailable" : $"{value:0.0} ms";
            ViewModel.DiagnosticText = $"{result.Timestamp.ToLocalTime():g} · {result.Target}\n{result.Status}\nDNS {Ms(result.DnsMilliseconds)} · Average {Ms(result.AverageMilliseconds)}\nMinimum {Ms(result.MinMilliseconds)} · Maximum {Ms(result.MaxMilliseconds)}\nJitter {Ms(result.JitterMilliseconds)} · ICMP nonresponses {(result.LossPercent is null ? "Unavailable" : result.LossPercent + "%")}\n{result.Replies}/{result.Attempts} replies · {result.LocalErrors} local errors\n\n{result.Detail}";
            // The target is user supplied; persist only when address collection is enabled.
            var stored = monitoring.Settings.CollectAddresses ? result : result with { Target = "[redacted]" };
            await Task.Run(() => store.SaveDiagnosticAsync(stored, CancellationToken.None));
        }
        catch (OperationCanceledException) { ViewModel.DiagnosticText = "Diagnostic cancelled. No incomplete result was stored."; }
        catch (Exception ex) { ViewModel.DiagnosticText += "\n" + ex.Message; }
        finally { ViewModel.Changed(nameof(ViewModel.DiagnosticText)); diagnosticCancellation.Dispose(); diagnosticCancellation = null; DiagnosticButton.IsEnabled = true; DiagnosticProgress.IsActive = false; }
    }
    private void CancelDiagnostics(object sender, RoutedEventArgs e) { diagnosticCancellation?.Cancel(); speedCancellation?.Cancel(); }
    private async void SpeedClicked(object sender, RoutedEventArgs e)
    {
        if (speedCancellation is not null) return;
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Run a bandwidth-consuming test?", Content = $"Endpoint: {SpeedEndpoint.Text}\nUp to 25 MB download and 10 MB random test upload, plus protocol overhead. Your public IP is visible to the provider. No application data is uploaded.", PrimaryButtonText = "Run test", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        speedCancellation = new(); SpeedButton.IsEnabled = false; SpeedResult.Text = "Testing… You can cancel from Diagnostics.";
        try
        {
            var result = await transfer.RunAsync(SpeedEndpoint.Text.Trim(), speedCancellation.Token);
            SpeedResult.Text = $"{result.Timestamp.ToLocalTime():g} · {result.Endpoint}\n↓ {result.DownloadMbps:0.00} Mbps · ↑ {result.UploadMbps:0.00} Mbps\nHTTP response {result.HttpResponseMilliseconds:0.0} ms · {result.DurationSeconds:0.0} seconds\n{result.Detail}";
        }
        catch (OperationCanceledException) { SpeedResult.Text = "Test cancelled or timed out; incomplete results discarded."; }
        catch (Exception ex) { SpeedResult.Text = "Test failed: " + ex.Message; }
        finally { speedCancellation.Dispose(); speedCancellation = null; SpeedButton.IsEnabled = true; }
    }
    private DateTimeOffset HistoryFrom() => DateTimeOffset.UtcNow.AddDays(HistoryRange.SelectedIndex switch { 0 => -1, 2 => -30, 3 => -365, _ => -7 });
    private async Task LoadHistoryAsync()
    {
        var from = HistoryFrom();
        usage = await Task.Run(() => store.GetUsageAsync(from, CancellationToken.None));
        ViewModel.Usage.Clear();
        foreach (var row in usage) ViewModel.Usage.Add($"{row.AdapterName}\n↓ {MainViewModel.Bytes(row.DownloadBytes)}    ↑ {MainViewModel.Bytes(row.UploadBytes)}\n{row.CoveredSeconds / 3600:0.00} hours of measured coverage · {row.Samples:N0} samples\n{row.First.ToLocalTime():g} – {row.Last.ToLocalTime():g}");
        if (usage.Count == 0) ViewModel.Usage.Add("No measured history in this range yet.");
        var events = await Task.Run(() => store.GetEventsAsync(CancellationToken.None));
        ViewModel.Events.Clear(); ViewModel.AddEvents(events.Reverse().ToArray());
        if (events.Count == 0) ViewModel.Events.Add("No connection transitions observed yet.");
    }
    private async void RefreshHistory(object sender, RoutedEventArgs e)
    {
        try { await LoadHistoryAsync(); } catch (Exception ex) { ViewModel.Status = "History unavailable: " + ex.Message; }
    }
    private void HistoryRangeChanged(object sender, SelectionChangedEventArgs e) { if (loaded) RefreshHistory(sender, new()); }
    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (currentPage == "Diagnostics" && lastDiagnostic is null) { ViewModel.Status = "Run diagnostics before exporting a result."; return; }
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"NetworkIntelligence-{DateTime.Now:yyyyMMdd-HHmmss}" };
            picker.FileTypeChoices.Add("JSON", [".json"]);
            if (currentPage != "Diagnostics") picker.FileTypeChoices.Add("CSV", [".csv"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync(); if (file is null) return;
            if (currentPage == "Diagnostics")
            {
                if (lastDiagnostic is null) { ViewModel.Status = "Run diagnostics before exporting a result."; return; }
                var result = monitoring.Settings.CollectAddresses ? lastDiagnostic : lastDiagnostic with { Target = "[redacted]" };
                await File.WriteAllTextAsync(file.Path, JsonSerializer.Serialize(new { SchemaVersion = 1, Source = "DNS / ICMP", Result = result }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                await LoadHistoryAsync();
                await ExportService.WriteUsageAsync(file.Path, usage, file.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase), CancellationToken.None);
            }
            ViewModel.Status = "Export saved: " + file.Name;
        }
        catch (Exception ex) { ViewModel.Status = "Export failed: " + ex.Message; }
    }
    private async void SaveSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = monitoring.Settings with { RetentionDays = checked((int)Retention.Value), Theme = ThemePicker.SelectedIndex == 2 ? "Dark" : ThemePicker.SelectedIndex == 1 ? "Light" : "System", CloseToTray = CloseToTray.IsOn,
                CollectAddresses = CollectAddresses.IsOn, CollectSsid = CollectSsid.IsOn, CollectProcessNames = CollectNames.IsOn, AlertsEnabled = AlertsEnabled.IsOn,
                QuietHoursEnabled = QuietEnabled.IsOn, QuietStartHour = checked((int)QuietStart.Value), QuietEndHour = checked((int)QuietEnd.Value), DiagnosticTarget = DiagnosticTarget.Text.Trim() };
            settings.Validate(); await Task.Run(() => store.SaveSettingsAsync(settings, CancellationToken.None));
            monitoring.UpdateSettings(settings); ApplySettings(settings);
            ViewModel.Status = "Settings saved. Privacy changes apply on the next collection cycle.";
        }
        catch (Exception ex) { ViewModel.Status = "Settings not saved: " + ex.Message; }
    }
    private async void IntegrityClicked(object sender, RoutedEventArgs e)
    {
        try { ViewModel.Status = "Database quick check: " + await Task.Run(() => store.CheckIntegrityAsync(CancellationToken.None)); }
        catch (Exception ex) { ViewModel.Status = "Integrity check failed: " + ex.Message; }
    }
    private async void DeleteHistory(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Delete stored history?", Content = "Deletes traffic aggregates, connection events and diagnostics from this local database. Settings are kept. Monitoring continues and will record new history.", PrimaryButtonText = "Delete history", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { await Task.Run(() => store.DeleteHistoryAsync(CancellationToken.None)); monitoring.ResetSession(); ViewModel.ClearSession(); await LoadHistoryAsync(); ViewModel.Status = "Stored history deleted."; }
        catch (Exception ex) { ViewModel.Status = "Deletion failed: " + ex.Message; }
    }
}
