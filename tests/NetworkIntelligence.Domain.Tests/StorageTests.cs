using Microsoft.Data.Sqlite;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Domain;
using NetworkIntelligence.Infrastructure;
namespace NetworkIntelligence.Domain.Tests;

public sealed class StorageTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "NetworkIntelligenceTests", Guid.NewGuid().ToString("N"));
    private SqliteHistoryStore store = null!;
    public async Task InitializeAsync()
    { Directory.CreateDirectory(directory); store = new(Path.Combine(directory, "test.db")); await store.InitializeAsync(default); }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); return Task.CompletedTask; }
    private static MonitoringSnapshot Sample(DateTimeOffset time, long? down, long? up) => new(time,
        [new("a", "Ethernet", "test", "Ethernet", "Up", 1_000_000_000, 1000, 2000, 0, 0, down / 2d, up / 2d, down, up, 2, true, [], [], [], time, Availability.Measured)], [], [], null);
    private static ServiceSnapshot ServiceSample(DateTimeOffset windowEnd, int pid, string name, long received, long sent, string availability = "Measured") =>
        new(windowEnd, TimeSpan.FromSeconds(5), 0, [new(pid, name, received / 5, sent / 5, received, sent, 3, windowEnd.AddSeconds(-5), windowEnd)], availability, "test");
    [Fact] public async Task MigrationIsIdempotentAndIntegrityPasses()
    { await store.InitializeAsync(default); Assert.Equal("ok", await store.CheckIntegrityAsync(default)); }
    [Fact] public async Task AggregatesDeltasWithoutTreatingMissingAsZero()
    {
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(Sample(now, 100, 50), [], default);
        await store.SaveAsync(Sample(now.AddSeconds(2), 300, 20), [], default);
        await store.SaveAsync(Sample(now.AddSeconds(4), null, null), [], default);
        var row = Assert.Single(await store.GetUsageAsync(now.AddMinutes(-1), default));
        Assert.Equal(400, row.DownloadBytes); Assert.Equal(70, row.UploadBytes); Assert.Equal(2, row.Samples); Assert.Equal(4d, row.CoveredSeconds);
    }
    [Fact] public async Task RetentionDeletesOldHistoryAndPreservesRecent()
    {
        await store.SaveAsync(Sample(DateTimeOffset.UtcNow.AddDays(-400), 100, 50), [], default);
        await store.SaveAsync(Sample(DateTimeOffset.UtcNow, 300, 20), [], default);
        Assert.Equal(1, await store.CleanupAsync(365, default));
        Assert.Equal(300, Assert.Single(await store.GetUsageAsync(DateTimeOffset.MinValue, default)).DownloadBytes);
    }
    [Fact] public async Task SettingsSurviveDataDeletion()
    {
        await store.SaveSettingsAsync(new() { RetentionDays = 14, Theme = "Dark" }, default);
        await store.SaveAsync(Sample(DateTimeOffset.UtcNow, 100, 50), [new(DateTimeOffset.UtcNow, "a", "Ethernet", "Up", "Down")], default);
        await store.DeleteHistoryAsync(default);
        Assert.Empty(await store.GetUsageAsync(DateTimeOffset.MinValue, default)); Assert.Empty(await store.GetEventsAsync(default));
        Assert.Equal(14, (await store.LoadSettingsAsync(default)).RetentionDays);
    }
    [Fact] public async Task JsonAndCsvExportRealAggregates()
    {
        await store.SaveAsync(Sample(DateTimeOffset.UtcNow, 100, 50), [], default);
        var rows = await store.GetUsageAsync(DateTimeOffset.MinValue, default);
        var path = Path.Combine(directory, "export.json"); await ExportService.WriteUsageAsync(path, rows, true, default);
        Assert.Contains("\"DownloadBytes\": 100", await File.ReadAllTextAsync(path));
        path = Path.Combine(directory, "export.csv"); await ExportService.WriteUsageAsync(path, rows, false, default);
        Assert.Contains("CoveredSeconds", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task ApplicationTrafficAggregatesByMinutePidAndName()
    {
        var minute = new DateTimeOffset(2026, 1, 1, 12, 0, 30, TimeSpan.Zero); // same minute bucket as +20s below
        await store.SaveApplicationTrafficAsync(ServiceSample(minute, 4242, "test.exe", 1000, 200), default);
        await store.SaveApplicationTrafficAsync(ServiceSample(minute.AddSeconds(20), 4242, "test.exe", 500, 100), default);
        var row = Assert.Single(await store.GetApplicationUsageAsync(DateTimeOffset.MinValue, default));
        Assert.Equal(4242, row.Pid); Assert.Equal("test.exe", row.ProcessName);
        Assert.Equal(1500, row.ReceivedBytes); Assert.Equal(300, row.SentBytes); Assert.Equal(2, row.Windows);
    }
    [Fact] public async Task UnavailableApplicationSnapshotIsNeverPersisted()
    {
        await store.SaveApplicationTrafficAsync(ServiceSample(DateTimeOffset.UtcNow, 1, "x", 100, 100, "Unavailable"), default);
        Assert.Empty(await store.GetApplicationUsageAsync(DateTimeOffset.MinValue, default));
    }
    [Fact] public async Task ApplicationTrafficRetentionAndDeletionMatchAdapterHistory()
    {
        await store.SaveApplicationTrafficAsync(ServiceSample(DateTimeOffset.UtcNow.AddDays(-400), 1, "old.exe", 100, 100), default);
        await store.SaveApplicationTrafficAsync(ServiceSample(DateTimeOffset.UtcNow, 2, "recent.exe", 200, 200), default);
        await store.CleanupAsync(365, default);
        var remaining = Assert.Single(await store.GetApplicationUsageAsync(DateTimeOffset.MinValue, default));
        Assert.Equal("recent.exe", remaining.ProcessName);
        await store.DeleteHistoryAsync(default);
        Assert.Empty(await store.GetApplicationUsageAsync(DateTimeOffset.MinValue, default));
    }
    [Fact] public async Task ApplicationUsageJsonAndCsvExportRealAggregates()
    {
        await store.SaveApplicationTrafficAsync(ServiceSample(DateTimeOffset.UtcNow, 99, "sample.exe", 4096, 512), default);
        var rows = await store.GetApplicationUsageAsync(DateTimeOffset.MinValue, default);
        var path = Path.Combine(directory, "app-export.json"); await ExportService.WriteApplicationUsageAsync(path, rows, true, default);
        Assert.Contains("\"ReceivedBytes\": 4096", await File.ReadAllTextAsync(path));
        path = Path.Combine(directory, "app-export.csv"); await ExportService.WriteApplicationUsageAsync(path, rows, false, default);
        Assert.Contains("sample.exe", await File.ReadAllTextAsync(path));
    }
    [Theory] [InlineData("=1+1")] [InlineData(" +cmd")] [InlineData("@formula")]
    public void CsvPreventsSpreadsheetFormulaInterpretation(string text) => Assert.StartsWith("\"'", ExportService.Csv(text));
    [Theory] [InlineData(0)] [InlineData(3651)]
    public void SettingsRejectInvalidRetention(int days) => Assert.Throws<ArgumentException>(() => (new AppSettings { RetentionDays = days }).Validate());
    [Fact] public async Task LiveAdapterCollectorDoesNotExposePrivateFieldsByDefault()
    {
        if (!OperatingSystem.IsWindows()) return;
        var collector = new NetworkCollector(); var first = collector.Collect(new(), true);
        Assert.NotEmpty(first.Adapters); Assert.All(first.Adapters, a => { Assert.Empty(a.Addresses); Assert.Empty(a.DnsServers); Assert.Empty(a.Gateways); Assert.Null(a.DownloadBytesPerSecond); });
        await Task.Delay(50);
        var second = collector.Collect(new(), false);
        Assert.Contains(second.Adapters, a => a.DownloadBytesPerSecond.HasValue);
        Assert.All(second.Wifi, w => Assert.Null(w.Ssid));
    }
    [Fact] public async Task LocalDiagnosticsMeasureResponses()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await new Diagnostics().RunAsync("localhost", default);
        Assert.Equal(10, result.Attempts); Assert.Equal(10, result.Replies); Assert.Equal(0d, result.LossPercent); Assert.NotNull(result.DnsMilliseconds);
    }
    [Fact] public async Task DiagnosticsHonorsCancellation()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Diagnostics().RunAsync("localhost", source.Token));
    }
}
