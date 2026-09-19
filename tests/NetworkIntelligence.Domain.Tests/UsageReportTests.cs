using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Domain;
using NetworkIntelligence.Infrastructure;
namespace NetworkIntelligence.Domain.Tests;

public sealed class UsageReportTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "NetworkIntelligenceReportTests", Guid.NewGuid().ToString("N"));
    private SqliteHistoryStore store = null!;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-18T12:00:00Z");
    private static UsageReportRange Range(DateOnly? start = null, DateOnly? end = null) =>
        UsageReportRange.Create(UsageReportPeriod.Custom, Now, TimeZoneInfo.Utc, start ?? new(2026, 9, 16), end ?? new(2026, 9, 17));
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        store = new(Path.Combine(directory, "report.db"));
        await store.InitializeAsync(default);
    }
    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); return Task.CompletedTask;
    }
    private Task Save(DateTimeOffset time, string id, long down, long up, double seconds = 10, string? name = null) => store.SaveAsync(new(time,
        [new(id, name ?? id, "test", "Ethernet", "Up", 1_000_000_000, 1000, 2000, 0, 0,
            down / seconds, up / seconds, down, up, seconds, true, [], [], [], time, Availability.Measured)], [], [], null), [], default);
    private Task SaveApp(DateTimeOffset time, int pid, string name, long down, long up) => store.SaveApplicationTrafficAsync(
        new(time, TimeSpan.FromSeconds(5), 0, [new(pid, name, down / 5, up / 5, down, up, 1, time.AddSeconds(-5), time)], "Measured", "test"), default);

    [Fact] public async Task MissingDayIsNullWhileMeasuredIdleDayIsZero()
    {
        await Save(DateTimeOffset.Parse("2026-09-16T12:00:10Z"), "a", 0, 0);
        var adapter = Assert.Single((await store.GetUsageReportAsync(Range(), default)).Adapters);
        Assert.True(adapter.Days[0].HasMeasurements);
        Assert.Equal(0, adapter.Days[0].DownloadBytes);
        Assert.Equal(10, adapter.Days[0].CoveredSeconds);
        Assert.Null(adapter.Days[1].DownloadBytes);
        Assert.Equal("No data", adapter.Days[1].Status);
        Assert.Equal(10d / (2 * 86400) * 100, adapter.CoveragePercent!.Value, 8);
    }
    [Fact] public async Task RangeIncludesStartButExcludesEndForBothSources()
    {
        var range = Range();
        foreach (var time in new[] { range.FromUtc.AddMinutes(-1), range.FromUtc, range.ToUtc.AddMinutes(-1), range.ToUtc })
        {
            await Save(time, "a", 100, 10);
            await SaveApp(time, 1, "browser", 50, 5);
        }
        var report = await store.GetUsageReportAsync(range, default);
        Assert.Equal(200, Assert.Single(report.Adapters).DownloadBytes);
        Assert.Equal(100, Assert.Single(report.Applications).DownloadBytes);
    }
    [Fact] public async Task AdapterTotalsStaySeparateAndRemovedAdaptersRemainSelectable()
    {
        await Save(Now.AddDays(-1), "physical", 1000, 100);
        await Save(Now.AddDays(-1), "vpn", 1000, 100);
        await Save(Now.AddMonths(-1), "old adapter", 2000, 100);
        var report = await store.GetUsageReportAsync(Range(), default);
        Assert.Equal(3, report.Adapters.Count);
        Assert.Equal(1000, report.Adapters.Single(a => a.AdapterId == "physical").DownloadBytes);
        Assert.Equal(1000, report.Adapters.Single(a => a.AdapterId == "vpn").DownloadBytes);
        Assert.Null(report.Adapters.Single(a => a.AdapterId == "old adapter").DownloadBytes);
    }
    [Fact] public async Task MultiplePidsAndNameCasingGroupTogetherAndRankByBytes()
    {
        await SaveApp(Now.AddDays(-1), 1, "Browser", 100, 10);
        await SaveApp(Now.AddDays(-1), 2, "browser", 200, 20);
        await SaveApp(Now.AddDays(-1), 3, "other", 1, 1);
        var report = await store.GetUsageReportAsync(Range(), default);
        Assert.Equal(2, report.Applications.Count);
        Assert.Equal(300, report.Applications[0].DownloadBytes);
        Assert.Equal(30, report.Applications[0].UploadBytes);
    }
    [Fact] public async Task ExcessCoverageIsFlaggedAndCappedPerMinute()
    {
        for (int i = 0; i < 9; i++) await Save(Now.AddDays(-1), "a", 100, 10);
        var adapter = Assert.Single((await store.GetUsageReportAsync(Range(), default)).Adapters);
        Assert.True(adapter.OverlappingCoverage);
        Assert.Equal(90, adapter.Days[1].StoredCoveredSeconds);
        Assert.Equal(60, adapter.Days[1].CoveredSeconds);
        Assert.Equal(900, adapter.DownloadBytes); // Preserve evidence; do not guess how to deduplicate bytes.
    }
    [Fact] public async Task CurrentMinuteCoverageCannotExceedElapsedTime()
    {
        var now = DateTimeOffset.Parse("2026-09-18T00:00:05Z");
        await Save(now, "a", 100, 10);
        var report = await store.GetUsageReportAsync(UsageReportRange.Create(UsageReportPeriod.Today, now, TimeZoneInfo.Utc), default);
        var adapter = Assert.Single(report.Adapters);
        Assert.Equal(5, adapter.CoveredSeconds);
        Assert.Equal(100, adapter.CoveragePercent);
    }
    [Fact] public async Task LocalMidnightBucketsAreNotGroupedByUtcDate()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+4", TimeSpan.FromHours(4), "UTC+4", "UTC+4");
        var range = UsageReportRange.Create(UsageReportPeriod.Custom, Now, zone, new(2026, 9, 16), new(2026, 9, 17));
        await Save(DateTimeOffset.Parse("2026-09-16T19:59:00Z"), "a", 100, 10);
        await Save(DateTimeOffset.Parse("2026-09-16T20:00:00Z"), "a", 200, 20);
        var days = Assert.Single((await store.GetUsageReportAsync(range, default)).Adapters).Days;
        Assert.Equal(100, days[0].DownloadBytes);
        Assert.Equal(200, days[1].DownloadBytes);
    }
    [Fact] public async Task MaximumRangeAndEmptyDatabaseAreSupported()
    {
        var range = UsageReportRange.Create(UsageReportPeriod.Custom, Now, TimeZoneInfo.Utc, new(2025, 9, 18), new(2026, 9, 18));
        var report = await store.GetUsageReportAsync(range, default);
        Assert.Equal(366, range.Days.Count);
        Assert.Empty(report.Adapters); Assert.Empty(report.Applications);
    }
    [Fact] public async Task YearlyReportIncludesJanuaryAndExcludesPreviousYear()
    {
        await Save(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "a", 100, 10);
        await Save(DateTimeOffset.Parse("2025-12-31T23:59:00Z"), "a", 900, 90);
        await SaveApp(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 1, "browser", 50, 5);
        var report = await store.GetUsageReportAsync(UsageReportRange.Create(UsageReportPeriod.ThisYear, Now, TimeZoneInfo.Utc), default);
        var adapter = Assert.Single(report.Adapters);
        Assert.Equal(100, adapter.DownloadBytes);
        Assert.Equal(100, adapter.Days[0].DownloadBytes);
        Assert.Null(adapter.Days[1].DownloadBytes);
        Assert.Equal(50, Assert.Single(report.Applications).DownloadBytes);
    }

    [Fact] public async Task DeletingHistoryClearsBothReportSources()
    {
        await Save(Now.AddDays(-1), "a", 100, 10);
        await SaveApp(Now.AddDays(-1), 1, "browser", 50, 5);
        await store.DeleteHistoryAsync(default);
        var report = await store.GetUsageReportAsync(Range(), default);
        Assert.Empty(report.Adapters);
        Assert.Empty(report.Applications);
    }

    [Fact] public async Task CancelledReportDoesNotReadDatabase() =>
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetUsageReportAsync(Range(), new CancellationToken(true)));

    [Fact] public async Task JsonExportMatchesSelectedAdapterAndKeepsEveryApplication()
    {
        await Save(Now.AddDays(-1), "a", 100, 10);
        await Save(Now.AddDays(-1), "b", 999, 99);
        for (int i = 0; i < 15; i++) await SaveApp(Now.AddDays(-1), i, $"app{i}", i + 1, i);
        var report = await store.GetUsageReportAsync(Range(), default);
        var path = Path.Combine(directory, "report.json");
        await ExportService.WriteUsageReportAsync(path, report, "a", true, default);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root = json.RootElement;
        Assert.Equal("a", root.GetProperty("Adapter").GetProperty("AdapterId").GetString());
        Assert.Equal(100, root.GetProperty("Adapter").GetProperty("DownloadBytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Days")[0].GetProperty("DownloadBytes").ValueKind);
        Assert.Equal(15, root.GetProperty("Applications").GetArrayLength());
        Assert.Contains("All interfaces", root.GetProperty("ApplicationScope").GetString());
        Assert.Equal("2026-09-16", root.GetProperty("Range").GetProperty("StartDate").GetString());
    }
    [Fact] public async Task CsvExportEscapesNamesAndUsesInvariantNumbers()
    {
        await Save(Now.AddDays(-1), "a", 100, 10, 2.5, "=adapter,\"test\"");
        await SaveApp(Now.AddDays(-1), 1, "@app,\"test\"", 7, 3);
        var report = await store.GetUsageReportAsync(Range(), default);
        var path = Path.Combine(directory, "report.csv");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            await ExportService.WriteUsageReportAsync(path, report, "a", false, default);
        }
        finally { CultureInfo.CurrentCulture = previous; }
        var csv = await File.ReadAllTextAsync(path);
        Assert.Contains("\"'=adapter,\"\"test\"\"\"", csv);
        Assert.Contains("\"'@app,\"\"test\"\"\"", csv);
        Assert.Contains(",2.5,2.5,86400,", csv);
        Assert.Contains(",2026-09-16,,,0,0,0,86400,0,\"No data\"", csv);
        Assert.Contains("Application,Custom", csv);
        Assert.Contains("All interfaces", csv);
    }
    [Fact] public async Task EmptyReportExportsUnavailableDaysRatherThanZeroUsage()
    {
        var report = await store.GetUsageReportAsync(Range(), default);
        var path = Path.Combine(directory, "empty.json");
        await ExportService.WriteUsageReportAsync(path, report, null, true, default);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Adapter").ValueKind);
        Assert.Equal(2, json.RootElement.GetProperty("Days").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Days")[0].GetProperty("DownloadBytes").ValueKind);
    }
    [Fact] public async Task UnknownAdapterCannotSilentlyExportAnotherAdapter()
    {
        var report = await store.GetUsageReportAsync(Range(), default);
        await Assert.ThrowsAsync<ArgumentException>(() => ExportService.WriteUsageReportAsync(Path.Combine(directory, "bad.json"), report, "missing", true, default));
    }
}
