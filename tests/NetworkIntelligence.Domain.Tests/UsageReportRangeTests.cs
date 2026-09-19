using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Domain.Tests;

public sealed class UsageReportRangeTests
{
    private static readonly TimeZoneInfo Dubai = TimeZoneInfo.CreateCustomTimeZone("Test Dubai", TimeSpan.FromHours(4), "Test Dubai", "Test Dubai");

    [Fact] public void TodayUsesLocalMidnightAndOnlyElapsedTime()
    {
        var now = DateTimeOffset.Parse("2026-09-17T22:30:00Z"); // September 18 locally
        var range = UsageReportRange.Create(UsageReportPeriod.Today, now, Dubai);
        Assert.Equal(new DateOnly(2026, 9, 18), range.StartDate);
        Assert.Equal(DateTimeOffset.Parse("2026-09-17T20:00:00Z"), range.FromUtc);
        Assert.Equal(now, range.ToUtc);
        Assert.Equal(9000, range.ExpectedSeconds);
        Assert.Single(range.Days);
    }
    [Theory]
    [InlineData("2026-09-14T12:00:00Z", 1)] // Monday
    [InlineData("2026-09-20T12:00:00Z", 7)] // Sunday
    public void WeekStartsMondayAndStopsToday(string timestamp, int days)
    {
        var range = UsageReportRange.Create(UsageReportPeriod.ThisWeek, DateTimeOffset.Parse(timestamp), TimeZoneInfo.Utc);
        Assert.Equal(new DateOnly(2026, 9, 14), range.StartDate);
        Assert.Equal(days, range.Days.Count);
    }
    [Fact] public void MonthIncludesLeapDay()
    {
        var range = UsageReportRange.Create(UsageReportPeriod.ThisMonth, DateTimeOffset.Parse("2024-02-29T12:00:00Z"), TimeZoneInfo.Utc);
        Assert.Equal(29, range.Days.Count);
        Assert.Equal(new DateOnly(2024, 2, 1), range.StartDate);
        Assert.Equal(28.5 * 86400, range.ExpectedSeconds);
    }
    [Theory]
    [InlineData("2024-12-31T12:00:00Z", 366)]
    [InlineData("2026-12-31T12:00:00Z", 365)]
    [InlineData("2026-01-01T12:00:00Z", 1)]
    public void YearStartsJanuaryFirstAndStopsAtElapsedTime(string timestamp, int days)
    {
        var now = DateTimeOffset.Parse(timestamp);
        var range = UsageReportRange.Create(UsageReportPeriod.ThisYear, now, TimeZoneInfo.Utc);
        Assert.Equal(new DateOnly(now.Year, 1, 1), range.StartDate);
        Assert.Equal(days, range.Days.Count);
        Assert.Equal(now, range.ToUtc);
        Assert.Equal((days - 0.5) * 86400, range.ExpectedSeconds);
    }

    [Theory]
    [InlineData(2026, 3, 8, 23)]
    [InlineData(2026, 11, 1, 25)]
    public void DaylightSavingDaysUseRealElapsedHours(int year, int month, int day, int hours)
    {
        var date = new DateOnly(year, month, day);
        var range = UsageReportRange.Create(UsageReportPeriod.Custom, DateTimeOffset.Parse("2027-01-01T00:00:00Z"),
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York"), date, date);
        Assert.Equal(hours * 3600, range.ExpectedSeconds);
    }
    [Fact] public void CustomEndIsInclusiveButUtcUpperBoundIsExclusive()
    {
        var range = UsageReportRange.Create(UsageReportPeriod.Custom, DateTimeOffset.Parse("2026-09-18T12:00:00Z"), Dubai,
            new(2026, 8, 31), new(2026, 9, 1));
        Assert.Equal(2, range.Days.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T20:00:00Z"), range.ToUtc);
        Assert.Equal(2 * 86400, range.ExpectedSeconds);
        Assert.Equal(range.Days[0].ToUtc, range.Days[1].FromUtc);
    }
    [Fact] public void MidnightHasNoFutureCoverage()
    {
        var range = UsageReportRange.Create(UsageReportPeriod.Today, DateTimeOffset.Parse("2026-09-18T00:00:00Z"), TimeZoneInfo.Utc);
        Assert.Equal(0, range.ExpectedSeconds);
    }
    [Theory]
    [InlineData("2026-09-19", "2026-09-19")]
    [InlineData("2026-09-18", "2026-09-17")]
    [InlineData("2025-01-01", "2026-09-18")]
    public void InvalidCustomDatesAreRejected(string start, string end) =>
        Assert.Throws<ArgumentException>(() => UsageReportRange.Create(UsageReportPeriod.Custom, DateTimeOffset.Parse("2026-09-18T12:00:00Z"),
            TimeZoneInfo.Utc, DateOnly.Parse(start), DateOnly.Parse(end)));

    [Fact] public void MissingCustomDateIsRejected() =>
        Assert.Throws<ArgumentException>(() => UsageReportRange.Create(UsageReportPeriod.Custom, DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
}
