namespace SolarInsight.Domain;

public record Period(DateOnly Start, DateOnly End)
{
    public int Days => End.DayNumber - Start.DayNumber;
}

public static class Calendar
{
    public static DateTimeOffset Utc(DateOnly date, TimeZoneInfo zone)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local))
            return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static DateOnly Day(DateTimeOffset utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);

    public static Period Select(DateOnly anchor, string kind)
    {
        DateOnly month = new(anchor.Year, anchor.Month, 1);
        return kind switch
        {
            "day" => new(anchor, anchor.AddDays(1)),
            "week" => Week(anchor),
            "month" => new(month, month.AddMonths(1)),
            "quarter" => Months(month.AddMonths(-(month.Month - 1) % 3), 3),
            "season" => Months(month.AddMonths(-month.Month % 3), 3),
            "year" => Months(new(anchor.Year, 1, 1), 12),
            _ => throw new ArgumentException("Unbekannter Zeitraum.")
        };
    }

    static Period Months(DateOnly start, int months) => new(start, start.AddMonths(months));

    static Period Week(DateOnly day)
    {
        DateOnly monday = day.AddDays(-((int)day.DayOfWeek + 6) % 7);
        return new(monday, monday.AddDays(7));
    }

    public static Period Compare(Period period, string kind, bool previousYear, DateOnly? elapsedEnd)
    {
        DateOnly start = previousYear ? PreviousYearStart(period.Start, kind) : kind switch
        {
            "month" => period.Start.AddMonths(-1),
            "quarter" or "season" => period.Start.AddMonths(-3),
            "year" => period.Start.AddYears(-1),
            _ => period.Start.AddDays(-period.Days)
        };
        DateOnly end = kind is "day" or "week" or "month" or "quarter" or "season" or "year"
            ? Select(start, kind).End : start.AddDays(period.Days);
        if (elapsedEnd is { } elapsed && elapsed < period.End)
        {
            DateOnly matched = (previousYear, kind) switch
            {
                (true, "year" or "quarter" or "season" or "month") => elapsed.AddYears(-1),
                (false, "year") => elapsed.AddYears(-1),
                (false, "quarter" or "season") => elapsed.AddMonths(-3),
                (false, "month") => elapsed.AddMonths(-1),
                _ => start.AddDays(Math.Max(0, elapsed.DayNumber - period.Start.DayNumber))
            };
            end = DateOnly.FromDayNumber(Math.Clamp(matched.DayNumber, start.DayNumber, end.DayNumber));
        }
        return new(start, end);
    }

    static DateOnly PreviousYearStart(DateOnly start, string kind)
    {
        if (kind != "week") return start.AddYears(-1);
        var monday = start.ToDateTime(TimeOnly.MinValue);
        int year = System.Globalization.ISOWeek.GetYear(monday) - 1;
        int week = Math.Min(System.Globalization.ISOWeek.GetWeekOfYear(monday), System.Globalization.ISOWeek.GetWeeksInYear(year));
        return DateOnly.FromDateTime(System.Globalization.ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
    }
}

