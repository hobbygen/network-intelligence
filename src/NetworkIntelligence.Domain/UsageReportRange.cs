namespace NetworkIntelligence.Domain;

public enum UsageReportPeriod { Today, ThisWeek, ThisMonth, Custom, ThisYear }

public sealed record UsageReportDay(DateOnly Date, DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    public double ExpectedSeconds => Math.Max(0, (ToUtc - FromUtc).TotalSeconds);
}

/// <summary>Local calendar dates, with an exclusive UTC upper bound clipped to the report's as-of time.</summary>
public sealed record UsageReportRange(UsageReportPeriod Period, DateOnly StartDate, DateOnly EndDate,
    string TimeZoneId, DateTimeOffset AsOfUtc, IReadOnlyList<UsageReportDay> Days)
{
    public DateTimeOffset FromUtc => Days[0].FromUtc;
    public DateTimeOffset ToUtc => Days[^1].ToUtc;
    public double ExpectedSeconds => Days.Sum(day => day.ExpectedSeconds);

    public static UsageReportRange Create(UsageReportPeriod period, DateTimeOffset now, TimeZoneInfo zone,
        DateOnly? customStart = null, DateOnly? customEnd = null)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var start = period switch
        {
            UsageReportPeriod.Today => today,
            UsageReportPeriod.ThisWeek => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            UsageReportPeriod.ThisMonth => new DateOnly(today.Year, today.Month, 1),
            UsageReportPeriod.ThisYear => new DateOnly(today.Year, 1, 1),
            UsageReportPeriod.Custom => customStart ?? throw new ArgumentException("Choose a start date."),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
        var end = period == UsageReportPeriod.Custom ? customEnd ?? throw new ArgumentException("Choose an end date.") : today;
        if (start > end) throw new ArgumentException("The start date must be on or before the end date.");
        if (end > today) throw new ArgumentException("Choose dates on or before today.");
        if (end.DayNumber - start.DayNumber >= 366) throw new ArgumentException("Choose a range of at most 366 days.");
        var days = new List<UsageReportDay>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var from = StartOfDayUtc(date, zone);
            var to = StartOfDayUtc(date.AddDays(1), zone);
            if (to > now) to = now.ToUniversalTime();
            if (to < from) to = from; // A skipped civil date contributes no expected time.
            days.Add(new(date, from, to));
        }
        return new(period, start, end, zone.Id, now.ToUniversalTime(), days.AsReadOnly());
    }

    private static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones change offset at midnight, or skip an entire civil date.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
