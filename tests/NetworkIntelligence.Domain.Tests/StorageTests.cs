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
    [Fact] public async Task SpeedProviderOverridePersistsAndCanReturnToAutomatic()
    {
        await store.SaveSettingsAsync(new() { SpeedTestEndpoint = "https://example.com/speed" }, default);
        Assert.Equal("https://example.com/speed", (await store.LoadSettingsAsync(default)).SpeedTestEndpoint);
        await store.SaveSettingsAsync((await store.LoadSettingsAsync(default)) with { SpeedTestEndpoint = "" }, default);
        Assert.Equal(SpeedTestProvider.DefaultEndpoint, SpeedTestProvider.Resolve((await store.LoadSettingsAsync(default)).SpeedTestEndpoint));
    }
    [Fact] public async Task ExistingSettingsWithoutSpeedProviderLoadWithAutomaticDefault()
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "test.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Settings(Key, Json) VALUES('app', '{\"Theme\":\"Dark\"}')";
        await command.ExecuteNonQueryAsync();
        var settings = await store.LoadSettingsAsync(default);
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal("", settings.SpeedTestEndpoint);
        Assert.Equal(SpeedTestProvider.DefaultEndpoint, SpeedTestProvider.Resolve(settings.SpeedTestEndpoint));
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
    [Fact] public async Task ApplicationMinuteSeriesGroupsAcrossPidsSharingAProcessName()
    {
        var minute = new DateTimeOffset(2026, 1, 1, 12, 0, 30, TimeSpan.Zero);
        // Two different PIDs both named "chrome" in the same minute — should sum into one series point,
        // matching "multiple processes belonging to one application" (requirements section 10.1).
        await store.SaveApplicationTrafficAsync(ServiceSample(minute, 100, "chrome", 1000, 100), default);
        await store.SaveApplicationTrafficAsync(ServiceSample(minute, 200, "chrome", 500, 50), default);
        var series = await store.GetApplicationMinuteSeriesAsync("chrome", DateTimeOffset.MinValue, default);
        var point = Assert.Single(series);
        Assert.Equal(1500, point.ReceivedBytes); Assert.Equal(150, point.SentBytes); Assert.Equal(5.0, point.CoveredSeconds); // one 5s window each, same minute
    }
    [Fact] public async Task AnomalyEventsPersistAndListNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        long firstId = await store.SaveAnomalyEventAsync(new(0, now.AddMinutes(-1), "chrome", "Download", "High", 20_000_000, 2_000_000, 4.5, "test explanation", true), default);
        long secondId = await store.SaveAnomalyEventAsync(new(0, now, "svchost", "Upload", "Low", 500_000, 100_000, 3.1, "another", false), default);
        Assert.True(secondId > firstId);
        var rows = await store.GetAnomalyEventsAsync(10, default);
        Assert.Equal(2, rows.Count);
        Assert.Equal("svchost", rows[0].ProcessName); Assert.False(rows[0].Notified); // newest first
        Assert.Equal("chrome", rows[1].ProcessName); Assert.True(rows[1].Notified);
    }
    [Fact] public async Task AnomalyEventRetentionAndDeletionMatchOtherHistory()
    {
        await store.SaveAnomalyEventAsync(new(0, DateTimeOffset.UtcNow.AddDays(-400), "old.exe", "Download", "Low", 1, 1, 1, "x", false), default);
        await store.SaveAnomalyEventAsync(new(0, DateTimeOffset.UtcNow, "recent.exe", "Download", "Low", 1, 1, 1, "x", false), default);
        await store.CleanupAsync(365, default);
        Assert.Equal("recent.exe", Assert.Single(await store.GetAnomalyEventsAsync(10, default)).ProcessName);
        await store.DeleteHistoryAsync(default);
        Assert.Empty(await store.GetAnomalyEventsAsync(10, default));
    }
    [Fact] public async Task SpeedTestsPersistAndListNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        await store.SaveSpeedTestAsync(new(now.AddMinutes(-5), "https://speed.cloudflare.com", 50.0, 10.0, 20.0, 2.0, 8.5), default);
        await store.SaveSpeedTestAsync(new(now, "https://example.com/speed", 90.0, 30.0, 15.0, 1.0, 7.1), default);
        var rows = await store.GetSpeedTestsAsync(10, default);
        Assert.Equal(2, rows.Count);
        Assert.Equal("https://example.com/speed", rows[0].Endpoint); // newest first
        Assert.Equal("https://speed.cloudflare.com", rows[1].Endpoint);
    }
    [Fact] public async Task SpeedTestsRespectTheRequestedLimit()
    {
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 5; i++)
            await store.SaveSpeedTestAsync(new(now.AddMinutes(i), "https://speed.cloudflare.com", 50.0, 10.0, 20.0, 2.0, 8.5), default);
        Assert.Equal(3, (await store.GetSpeedTestsAsync(3, default)).Count);
    }
    [Fact] public async Task SpeedTestRetentionAndDeletionMatchOtherHistory()
    {
        await store.SaveSpeedTestAsync(new(DateTimeOffset.UtcNow.AddDays(-400), "https://speed.cloudflare.com", 10.0, 5.0, 30.0, 3.0, 9.0), default);
        await store.SaveSpeedTestAsync(new(DateTimeOffset.UtcNow, "https://speed.cloudflare.com", 90.0, 30.0, 15.0, 1.0, 7.1), default);
        await store.CleanupAsync(365, default);
        Assert.Equal(90.0, Assert.Single(await store.GetSpeedTestsAsync(10, default)).DownloadMbps);
        await store.DeleteHistoryAsync(default);
        Assert.Empty(await store.GetSpeedTestsAsync(10, default));
    }
    [Theory] [InlineData("=1+1")] [InlineData(" +cmd")] [InlineData("@formula")]
    public void CsvPreventsSpreadsheetFormulaInterpretation(string text) => Assert.StartsWith("\"'", ExportService.Csv(text));
    [Theory] [InlineData(0)] [InlineData(3651)]
    public void SettingsRejectInvalidRetention(int days) => Assert.Throws<ArgumentException>(() => (new AppSettings { RetentionDays = days }).Validate());
    [Theory] [InlineData(1.0)] [InlineData(6.1)]
    public void SettingsRejectInvalidAnomalySensitivity(double sensitivity) => Assert.Throws<ArgumentException>(() => (new AppSettings { AnomalySensitivity = sensitivity }).Validate());
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
