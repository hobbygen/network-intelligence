using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Contracts;

public sealed record AdapterSnapshot(string Id, string Name, string Description, string Type, string State,
    long? LinkBitsPerSecond, long? ReceivedBytes, long? SentBytes, long? Errors, long? Discards,
    double? DownloadBytesPerSecond, double? UploadBytesPerSecond, long? DownloadDelta, long? UploadDelta,
    double IntervalSeconds, bool HasGateway, string[] Addresses, string[] DnsServers, string[] Gateways,
    DateTimeOffset Timestamp, Availability Availability, string? Detail = null);
public sealed record WifiSnapshot(string AdapterId, string State, string? Ssid, uint? SignalPercent,
    uint? ReceiveKbps, uint? TransmitKbps, string? Authentication, Availability Availability, string Detail, DateTimeOffset Timestamp);
public sealed record ProcessConnections(int Pid, DateTimeOffset? StartedAt, string Name, int TcpConnections, int UdpEndpoints,
    Availability Availability, string Detail);
public sealed record ConnectionEvent(DateTimeOffset Timestamp, string AdapterId, string AdapterName, string PreviousState, string State);
public sealed record MonitoringSnapshot(DateTimeOffset Timestamp, IReadOnlyList<AdapterSnapshot> Adapters,
    IReadOnlyList<WifiSnapshot> Wifi, IReadOnlyList<ProcessConnections> Applications, string? Error,
    IReadOnlyDictionary<string, TrafficTotals>? SessionTotals = null);
public sealed record TrafficTotals(long Down, long Up);
public sealed record DiagnosticResult(DateTimeOffset Timestamp, string Target, double? DnsMilliseconds,
    double? AverageMilliseconds, double? MinMilliseconds, double? MaxMilliseconds, double? JitterMilliseconds,
    double? LossPercent, int Attempts, int Replies, int LocalErrors, string Status, string Detail);
public sealed record UsageSummary(string AdapterId, string AdapterName, long DownloadBytes, long UploadBytes,
    long Samples, double CoveredSeconds, DateTimeOffset First, DateTimeOffset Last);
public sealed record HistoryPoint(DateTimeOffset Timestamp, double? DownloadBytesPerSecond, double? UploadBytesPerSecond);
/// <summary>Grouped by (Pid, ProcessName) per minute bucket, not a stable application identity — a PID reused by a
/// different process within the same minute lands in the same row. Only ever populated from Measured
/// MonitoringService snapshots (see docs/DECISIONS.md ADR-007/009).</summary>
public sealed record ApplicationUsageSummary(int Pid, string ProcessName, long ReceivedBytes, long SentBytes,
    long Windows, DateTimeOffset First, DateTimeOffset Last);

public sealed record AppSettings
{
    public int RetentionDays { get; init; } = 365;
    public bool CollectAddresses { get; init; }
    public bool CollectSsid { get; init; }
    public bool CollectProcessNames { get; init; } = true;
    public bool AlertsEnabled { get; init; } = true;
    public bool CloseToTray { get; init; } = true;
    public string Theme { get; init; } = "System";
    public string DiagnosticTarget { get; init; } = "1.1.1.1";
    public int QuietStartHour { get; init; } = 22;
    public int QuietEndHour { get; init; } = 7;
    public bool QuietHoursEnabled { get; init; }
    public void Validate()
    {
        if (RetentionDays is < 1 or > 3650) throw new ArgumentException("Retention must be between 1 and 3650 days.");
        if (Theme is not ("System" or "Light" or "Dark")) throw new ArgumentException("Choose System, Light or Dark theme.");
        if (QuietStartHour is < 0 or > 23 || QuietEndHour is < 0 or > 23) throw new ArgumentException("Quiet hours must be 0–23.");
        if (Uri.CheckHostName(DiagnosticTarget) == UriHostNameType.Unknown) throw new ArgumentException("Enter a host name or IP address, without a URL or port.");
    }
}
public interface INetworkCollector
{
    MonitoringSnapshot Collect(AppSettings settings, bool includeDetails);
    void Reset();
}
public interface IHistoryStore
{
    Task InitializeAsync(CancellationToken token);
    Task<AppSettings> LoadSettingsAsync(CancellationToken token);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken token);
    Task SaveAsync(MonitoringSnapshot snapshot, IReadOnlyList<ConnectionEvent> events, CancellationToken token);
    Task<IReadOnlyList<UsageSummary>> GetUsageAsync(DateTimeOffset from, CancellationToken token);
    Task<IReadOnlyList<HistoryPoint>> GetHistoryAsync(string adapterId, DateTimeOffset from, CancellationToken token);
    Task<IReadOnlyList<ConnectionEvent>> GetEventsAsync(CancellationToken token);
    Task SaveDiagnosticAsync(DiagnosticResult result, CancellationToken token);
    Task<int> CleanupAsync(int retentionDays, CancellationToken token);
    Task DeleteHistoryAsync(CancellationToken token);
    Task<string> CheckIntegrityAsync(CancellationToken token);
    /// <summary>No-ops when <paramref name="snapshot"/>.Availability isn't Measured — never persists an
    /// unavailable/error window as if it were zero traffic.</summary>
    Task SaveApplicationTrafficAsync(ServiceSnapshot snapshot, CancellationToken token);
    Task<IReadOnlyList<ApplicationUsageSummary>> GetApplicationUsageAsync(DateTimeOffset from, CancellationToken token);
}
/// <summary>Talks to the optional, elevated MonitoringService (docs/DECISIONS.md ADR-007) over its named pipe.
/// Never throws: any failure to reach the service — not installed, not running, access denied — is reported as
/// an Unavailable <see cref="ServiceSnapshot"/>, the same way every other collector in this app degrades.</summary>
public interface IApplicationTrafficClient
{
    Task<ServiceSnapshot> GetSnapshotAsync(CancellationToken token);
}
