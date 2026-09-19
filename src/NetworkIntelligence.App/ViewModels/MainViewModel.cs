using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Domain;
using SkiaSharp;
namespace NetworkIntelligence.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private string status = "Starting local monitoring…";
    public string Status { get => status; set { status = value; Changed(); } }
    public ObservableCollection<AdapterSnapshot> Adapters { get; } = [];
    public ObservableCollection<AdapterDisplay> Ethernet { get; } = [];
    public ObservableCollection<WifiDisplay> Wifi { get; } = [];
    public ObservableCollection<ProcessDisplay> Applications { get; } = [];
    public ObservableCollection<AppTrafficDisplay> LiveApplicationTraffic { get; } = [];
    public ObservableCollection<string> Events { get; } = [];
    public ObservableCollection<string> Usage { get; } = [];
    public ObservableCollection<string> AppUsage { get; } = [];
    public ObservableCollection<string> SpeedTests { get; } = [];
    public ObservableCollection<AlertDisplay> RecentAlerts { get; } = [];
    public ObservableCollection<double?> Downloads { get; } = [];
    public ObservableCollection<double?> Uploads { get; } = [];
    public ISeries[] Series { get; }
    public Axis[] XAxes { get; } = [new() { IsVisible = false }];
    public Axis[] YAxes { get; } = [new() { Name = "Mbps", MinLimit = 0, Labeler = value => value.ToString("0.##") }];
    private AdapterSnapshot? selected;
    private readonly Dictionary<string, (long Down, long Up)> session = [];
    private MonitoringSnapshot? latest;
    private ServiceSnapshot appTraffic = ServiceSnapshot.Unavailable("Not checked yet.");
    private readonly List<ConnectionEvent> connectionEvents = [];
    private DiagnosticResult? lastDiagnostic;
    public NetworkHealthScore Health { get; private set; } = NetworkHealthScore.Insufficient("Waiting for the first adapter measurement.");
    public string HealthScoreText => Health.Score?.ToString() ?? "—";
    public string HealthBandText => Health.Band ?? "Insufficient data";
    public IReadOnlyList<ProcessConnections> Processes => latest?.Applications ?? [];
    public AdapterSnapshot? Selected
    {
        get => selected;
        set
        {
            if (selected?.Id != value?.Id) { Downloads.Clear(); Uploads.Clear(); }
            selected = value; NotifyMetrics(); Changed();
        }
    }
    public string Download => FormatRate(selected?.DownloadBytesPerSecond);
    public string Upload => FormatRate(selected?.UploadBytesPerSecond);
    public string LinkSpeed => selected?.LinkBitsPerSecond is { } bits ? FormatRate(bits / 8d) : "Unavailable";
    public string Connection => selected is null ? "No adapter" : selected.State;
    public string AdapterName => selected?.Name ?? "Waiting for adapters";
    public string AdapterDescription => selected?.Description ?? "No telemetry yet";
    public string SessionUsage => selected is not null && session.TryGetValue(selected.Id, out var total) ? $"↓ {Bytes(total.Down)}    ↑ {Bytes(total.Up)}" : "Waiting for measured intervals";
    public string MeasurementDetail => selected is null ? "Unavailable" : $"{selected.Timestamp.ToLocalTime():HH:mm:ss} · {selected.Availability} · NetworkInterface counters · {selected.Type}";
    public string Gateway => selected is null ? "Unavailable" : selected.HasGateway ? "Configured; reachability not tested" : "No gateway configured";
    public string Internet => "Not tested · run target diagnostics";
    public string CounterDetail => selected is null ? "Unavailable" : $"Errors: {selected.Errors?.ToString() ?? "Unavailable"} · Discards: {selected.Discards?.ToString() ?? "Unavailable"} · Since adapter counters began";
    public string AddressDetail => selected is null ? "Unavailable" : selected.Addresses.Length == 0 ? "Address collection disabled or no addresses available" : $"IP: {string.Join(", ", selected.Addresses)}\nGateway: {string.Join(", ", selected.Gateways)}\nDNS: {string.Join(", ", selected.DnsServers)}";
    public string DiagnosticText { get; set; } = "No diagnostic run yet. Tests send DNS queries and 10 ICMP echo requests to the target you choose.";
    public string LastUpdated => latest is null ? "Waiting" : $"Updated {latest.Timestamp.ToLocalTime():HH:mm:ss} · every 2 seconds";
    public bool ApplicationTrafficAvailable => appTraffic.Availability == "Measured";
    public string ApplicationTrafficStatus => ApplicationTrafficAvailable
        ? $"Live · updated {appTraffic.Timestamp.ToLocalTime():HH:mm:ss} · {appTraffic.WindowDuration.TotalSeconds:0}s window · events lost this window: {appTraffic.EventsLost} · {appTraffic.Applications.Count} processes"
        : $"{appTraffic.Availability} · {appTraffic.Detail}";
    public MainViewModel()
    {
        Series = [new LineSeries<double?> { Name = "Download", Values = Downloads, GeometrySize = 0, LineSmoothness = 0.25, Stroke = new SolidColorPaint(SKColor.Parse("#26D9C4"), 3), Fill = new SolidColorPaint(SKColor.Parse("#26D9C4").WithAlpha(25)) },
            new LineSeries<double?> { Name = "Upload", Values = Uploads, GeometrySize = 0, LineSmoothness = 0.25, Stroke = new SolidColorPaint(SKColor.Parse("#6C9EFF"), 2), Fill = null }];
    }
    public void Apply(MonitoringSnapshot snapshot)
    {
        latest = snapshot;
        if (snapshot.SessionTotals is not null)
            foreach (var pair in snapshot.SessionTotals) session[pair.Key] = (pair.Value.Down, pair.Value.Up);
        var ordered = snapshot.Adapters.OrderByDescending(a => a.State == "Up" && a.HasGateway).ThenByDescending(a => a.State == "Up").ThenBy(a => a.Name).ToArray();
        var id = selected?.Id;
        if (!Adapters.Select(a => a.Id).SequenceEqual(ordered.Select(a => a.Id)))
        { Adapters.Clear(); foreach (var adapter in ordered) Adapters.Add(adapter); }
        Selected = ordered.FirstOrDefault(a => a.Id == id) ?? ordered.FirstOrDefault();
        Downloads.Add(selected?.DownloadBytesPerSecond * 8 / 1_000_000); Uploads.Add(selected?.UploadBytesPerSecond * 8 / 1_000_000);
        while (Downloads.Count > 90) Downloads.RemoveAt(0);
        while (Uploads.Count > 90) Uploads.RemoveAt(0);
        Ethernet.Clear(); foreach (var adapter in ordered.Where(a => a.Type.Contains("Ethernet"))) Ethernet.Add(new(adapter));
        Wifi.Clear(); foreach (var item in snapshot.Wifi) Wifi.Add(new(item));
        if (snapshot.Error is not null) Status = snapshot.Error;
        Changed(nameof(LastUpdated));
        RecomputeHealth();
    }
    public void ApplyApplicationTraffic(ServiceSnapshot snapshot)
    {
        appTraffic = snapshot;
        LiveApplicationTraffic.Clear();
        foreach (var sample in snapshot.Applications.Take(50)) LiveApplicationTraffic.Add(new(sample));
        Changed(nameof(ApplicationTrafficAvailable));
        Changed(nameof(ApplicationTrafficStatus));
    }
    public void FilterApplications(string query)
    {
        Applications.Clear();
        foreach (var row in Processes.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Pid.ToString().Contains(query)).Take(300)) Applications.Add(new(row));
    }
    public void AddEvents(IReadOnlyList<ConnectionEvent> events)
    {
        foreach (var item in events)
        {
            Events.Insert(0, $"{item.Timestamp.ToLocalTime():g}   {item.AdapterName}   {item.PreviousState} → {item.State}");
            connectionEvents.Insert(0, item);
        }
        while (Events.Count > 200) Events.RemoveAt(Events.Count - 1);
        while (connectionEvents.Count > 200) connectionEvents.RemoveAt(connectionEvents.Count - 1);
        RecomputeHealth();
    }
    /// <summary>Feeds the health score's latency/loss/jitter/DNS factors (requirements section 12.4) — diagnostics
    /// are only ever user-initiated (see MainWindow.DiagnosticsClicked), so this is the one place that data enters
    /// the ViewModel; <see cref="HealthScoreCalculator"/> itself decides when a result is too stale to use.</summary>
    public void SetDiagnostic(DiagnosticResult? result) { lastDiagnostic = result; RecomputeHealth(); }
    private void RecomputeHealth()
    {
        // WLAN interface GUIDs come back wrapped in braces (WifiReader's id.ToString("B")); NetworkInterface.Id
        // (AdapterSnapshot.Id, used everywhere else) does not have them — strip both before comparing.
        static string NormalizeId(string id) => id.Trim('{', '}');
        var wifi = selected is null ? null : Wifi.FirstOrDefault(w => string.Equals(NormalizeId(w.Value.AdapterId), NormalizeId(selected.Id), StringComparison.OrdinalIgnoreCase));
        var disconnects = connectionEvents
            .Where(e => e.AdapterId == selected?.Id && e.PreviousState == "Up" && e.State != "Up")
            .Select(e => e.Timestamp).ToArray();
        Health = HealthScoreCalculator.Compute(DateTimeOffset.UtcNow, selected is not null, selected?.State == "Up", selected?.HasGateway ?? false,
            lastDiagnostic?.AverageMilliseconds, lastDiagnostic?.LossPercent, lastDiagnostic?.JitterMilliseconds, lastDiagnostic?.DnsMilliseconds, lastDiagnostic?.Timestamp,
            wifi is not null, wifi?.Value.SignalPercent, selected?.Errors, selected?.Discards, disconnects);
        Changed(nameof(Health)); Changed(nameof(HealthScoreText)); Changed(nameof(HealthBandText));
    }
    public void AddAlert(AnomalyEvent anomaly)
    {
        RecentAlerts.Insert(0, new AlertDisplay(anomaly));
        while (RecentAlerts.Count > 50) RecentAlerts.RemoveAt(RecentAlerts.Count - 1);
    }
    /// <summary>View-only removal (requirements section 10.6's "dismiss") — the persisted <see
    /// cref="AnomalyEvent"/> row and its evidence are untouched; only this session's Dashboard list changes.</summary>
    public void RemoveAlert(AlertDisplay alert) => RecentAlerts.Remove(alert);
    public void ClearSession() { session.Clear(); Downloads.Clear(); Uploads.Clear(); connectionEvents.Clear(); NotifyMetrics(); RecomputeHealth(); }
    private void NotifyMetrics()
    { foreach (var name in new[] { nameof(Download), nameof(Upload), nameof(Connection), nameof(LinkSpeed), nameof(AdapterName), nameof(AdapterDescription), nameof(SessionUsage), nameof(MeasurementDetail), nameof(Gateway), nameof(CounterDetail), nameof(AddressDetail) }) Changed(name); }
    public static string FormatRate(double? bytes) => bytes is null ? "Unavailable" : bytes < 125_000 ? $"{bytes * 8 / 1_000:0.0} Kbps" : $"{bytes * 8 / 1_000_000:0.00} Mbps";
    public static string Bytes(long value) => value >= 1L << 30 ? $"{value / (double)(1L << 30):0.00} GiB" : value >= 1L << 20 ? $"{value / (double)(1L << 20):0.00} MiB" : $"{value / 1024d:0.0} KiB";
}
public sealed record AdapterDisplay(AdapterSnapshot Value)
{
    public string Title => $"{Value.Name} · {Value.State}";
    public string Summary => $"↓ {MainViewModel.FormatRate(Value.DownloadBytesPerSecond)}   ↑ {MainViewModel.FormatRate(Value.UploadBytesPerSecond)}   Link: {MainViewModel.FormatRate(Value.LinkBitsPerSecond / 8d)}";
    public string Detail => $"{Value.Description}\n{Value.Availability} · {Value.Timestamp.ToLocalTime():T} · Errors {Value.Errors?.ToString() ?? "Unavailable"}, discards {Value.Discards?.ToString() ?? "Unavailable"}";
}
public sealed record WifiDisplay(WifiSnapshot Value)
{
    public string Title => Value.Ssid ?? $"Wi-Fi · {Value.State}";
    public string Summary => $"Signal: {(Value.SignalPercent is { } signal ? signal + "%" : "Unavailable")} · RX link: {Value.ReceiveKbps?.ToString() ?? "Unavailable"} Kbps · TX link: {Value.TransmitKbps?.ToString() ?? "Unavailable"} Kbps";
    public string Detail => $"{Value.Availability} · {Value.Timestamp.ToLocalTime():T} · {Value.Authentication ?? "Security unavailable"}\n{Value.Detail}";
}
public sealed record ProcessDisplay(ProcessConnections Value)
{
    public string Name => Value.Name;
    public string Summary => $"PID {Value.Pid} · TCP {Value.TcpConnections} · UDP {Value.UdpEndpoints} · started {Value.StartedAt?.ToLocalTime().ToString("g") ?? "unavailable"}";
    public string Detail => $"{Value.Availability} · {Value.Detail}";
}
public sealed record AppTrafficDisplay(ApplicationTrafficSample Value)
{
    public string Name => Value.ProcessName;
    public string Summary => $"PID {Value.Pid} · ↓ {MainViewModel.FormatRate(Value.ReceivedBytesPerSecond)} · ↑ {MainViewModel.FormatRate(Value.SentBytesPerSecond)}";
    public string Detail => $"Window total: ↓ {MainViewModel.Bytes(Value.ReceivedBytesTotal)}  ↑ {MainViewModel.Bytes(Value.SentBytesTotal)} · {Value.Events} events · {Value.WindowStart.ToLocalTime():T}–{Value.WindowEnd.ToLocalTime():T}";
}
public sealed record AlertDisplay(AnomalyEvent Value)
{
    public string Title => $"{Value.Timestamp.ToLocalTime():g}   {Value.Severity}   {Value.ProcessName}";
    public string Summary => $"{(Value.Direction == "Download" ? "↓" : "↑")} {MainViewModel.FormatRate(Value.CurrentBytesPerSecond)} vs. baseline {MainViewModel.FormatRate(Value.BaselineMeanBytesPerSecond)}";
    public string Detail => Value.Explanation;
}
