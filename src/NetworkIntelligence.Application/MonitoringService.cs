using Microsoft.Extensions.Logging;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.Application;

public sealed class MonitoringService(INetworkCollector collector, IHistoryStore store, ILogger<MonitoringService> logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private Task? worker;
    private AppSettings settings = new();
    private volatile bool paused;
    private volatile bool resetRequested;
    private volatile bool resetSessionRequested;
    private readonly Dictionary<string, TrafficTotals> session = [];
    private readonly Dictionary<string, (string Name, string State)> states = [];
    public event Action<MonitoringSnapshot>? Snapshot;
    public event Action<IReadOnlyList<ConnectionEvent>>? ConnectionsChanged;
    public event Action<string>? Status;
    public bool IsPaused => paused;
    public AppSettings Settings => settings;
    public void UpdateSettings(AppSettings value) { value.Validate(); Interlocked.Exchange(ref settings, value); resetRequested = true; }
    public void SetPaused(bool value) { paused = value; Status?.Invoke(value ? "Monitoring paused · live values are stale" : "Monitoring resumed"); }
    public void Start() => worker ??= Task.Run(RunAsync);
    public void ResetSession() => resetSessionRequested = true;
    private async Task RunAsync()
    {
        int tick = 0; bool wasPaused = false; var nextCleanup = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                if (paused) { wasPaused = true; continue; }
                if (wasPaused) { collector.Reset(); states.Clear(); wasPaused = false; }
                if (resetRequested) { collector.Reset(); resetRequested = false; tick = 0; nextCleanup = DateTimeOffset.MinValue; }
                if (resetSessionRequested) { session.Clear(); collector.Reset(); resetSessionRequested = false; }
                var currentSettings = settings;
                var current = collector.Collect(currentSettings, tick++ % 5 == 0);
                foreach (var adapter in current.Adapters)
                {
                    if (adapter.DownloadDelta is not { } down || adapter.UploadDelta is not { } up) continue;
                    var total = session.GetValueOrDefault(adapter.Id) ?? new(0, 0);
                    session[adapter.Id] = new(total.Down + down, total.Up + up);
                }
                current = current with { SessionTotals = new Dictionary<string, TrafficTotals>(session) };
                var changes = new List<ConnectionEvent>();
                foreach (var adapter in current.Adapters)
                {
                    if (states.TryGetValue(adapter.Id, out var last) && last.State != adapter.State)
                        changes.Add(new(current.Timestamp, adapter.Id, adapter.Name, last.State, adapter.State));
                    states[adapter.Id] = (adapter.Name, adapter.State);
                }
                var present = current.Adapters.Select(a => a.Id).ToHashSet();
                foreach (var id in states.Keys.Where(id => !present.Contains(id)).ToArray())
                { changes.Add(new(current.Timestamp, id, states[id].Name, states[id].State, "Removed")); states.Remove(id); }
                Snapshot?.Invoke(current);
                if (changes.Count > 0) ConnectionsChanged?.Invoke(changes);
                try
                {
                    await store.SaveAsync(current, changes, stop.Token);
                    if (DateTimeOffset.UtcNow >= nextCleanup)
                    {
                        int removed = await store.CleanupAsync(currentSettings.RetentionDays, stop.Token);
                        nextCleanup = DateTimeOffset.UtcNow.AddMinutes(removed >= 2000 ? 1 : 60);
                        Status?.Invoke($"Local history saved · retention {currentSettings.RetentionDays} days · cleanup removed {removed} rows");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { logger.LogError("History write failed: {Type}", ex.GetType().Name); Status?.Invoke("History could not be saved. Live collection continues; check available disk space and database access."); }
            } while (await timer.WaitForNextTickAsync(stop.Token));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError("Collection stopped: {Type}", ex.GetType().Name); Status?.Invoke("Collection stopped: " + ex.GetType().Name + ". Restart the application to retry."); }
    }
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        if (worker is not null) await worker;
        stop.Dispose();
    }
}
