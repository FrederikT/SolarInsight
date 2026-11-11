using SolarInsight.Domain;

namespace SolarInsight.Application;

public record LimitTotal(decimal Hours, decimal EligibleHours, decimal Kwh, decimal GridEligibleHours, decimal AtLimitGridHours);
public record HourDay(DateOnly Day, int Hour, decimal? UsableSoc, bool? Import, bool? Empty, bool? Full,
    bool? EmptyImport, bool? FullExport, decimal ExpectedHours);
public record Frequency(int Count, int Eligible);
public record HourProfile(int Hour, decimal? UsableSoc, int SocDays, Frequency Import, Frequency Empty,
    Frequency Full, Frequency EmptyImport, Frequency FullExport, Frequency EmptyAmongImports);
public record NightDetail(DateOnly Evening, decimal? EmptyHours, bool Complete, DateTimeOffset? FirstPv);
public record StorageProfile(LimitTotal Lower, LimitTotal Upper, HourProfile[] Hours, HourDay[] Days);

public static class StorageInsights
{
    public static string? Limit(Interval row, Battery? battery, decimal tolerance)
    {
        if (battery is not { Usable: > 0 } || row.Soc is not (>= 0 and <= 1)) return null;
        // Do not allow the tolerance bands to overlap on small usable windows.
        tolerance = Math.Min(tolerance, (battery.MaxSoc - battery.MinSoc) / 2);
        if (row.Soc <= battery.MinSoc + tolerance) return "lower";
        if (row.Soc >= battery.MaxSoc - tolerance) return "upper";
        return "middle";
    }

    sealed class Slot
    {
        public long Expected, Soc, Import, EmptyImport, FullExport;
        public decimal SocTicks;
        public bool HasImport, Empty, Full, EmptyWithImport, FullWithExport;
    }

    public static StorageProfile Profile(Interval[] rows, Period period, TimeZoneInfo zone, Battery? battery,
        decimal tolerance, int? detailHour, Func<Interval, Battery?>? limits = null)
    {
        var slots = new Dictionary<(DateOnly Day, int Hour), Slot>();
        var start = Calendar.Utc(period.Start, zone);
        var end = Calendar.Utc(period.End, zone);
        foreach (var part in Split(start, end, zone))
        {
            if (!slots.TryGetValue(part.Key, out var slot)) slots.Add(part.Key, slot = new());
            slot.Expected += part.Ticks;
        }

        decimal lowerJoint = 0, upperJoint = 0;
        decimal socHours = 0, importHours = 0, exportHours = 0, lowerHours = 0, upperHours = 0, lowerKwh = 0, upperKwh = 0;
        foreach (var row in rows)
        {
            var activeBattery = limits is null ? battery : limits(row);
            var limit = Limit(row, activeBattery, tolerance);
            bool hasImport = row.Import > EnergyInsights.Tolerance(row);
            bool hasExport = row.Export > EnergyInsights.Tolerance(row);
            if (limit is not null)
            {
                socHours += row.Hours;
                if (row.Import.HasValue) importHours += row.Hours;
                if (row.Export.HasValue) exportHours += row.Hours;
                if (limit == "lower")
                {
                    lowerHours += row.Hours;
                    if (row.Import is { } import) { lowerJoint += row.Hours; lowerKwh += import; }
                }
                if (limit == "upper")
                {
                    upperHours += row.Hours;
                    if (row.Export is { } export) { upperJoint += row.Hours; upperKwh += export; }
                }
            }

            foreach (var part in Split(row.Start, row.End, zone))
            {
                if (!slots.TryGetValue(part.Key, out var slot)) continue;
                if (row.Import.HasValue) { slot.Import += part.Ticks; slot.HasImport |= hasImport; }
                if (limit is null) continue;
                slot.Soc += part.Ticks;
                slot.SocTicks += Math.Clamp((row.Soc!.Value - activeBattery!.MinSoc) / (activeBattery.MaxSoc - activeBattery.MinSoc), 0, 1) * part.Ticks;
                slot.Empty |= limit == "lower";
                slot.Full |= limit == "upper";
                if (row.Import.HasValue)
                {
                    slot.EmptyImport += part.Ticks;
                    slot.EmptyWithImport |= limit == "lower" && hasImport;
                }
                if (row.Export.HasValue)
                {
                    slot.FullExport += part.Ticks;
                    slot.FullWithExport |= limit == "upper" && hasExport;
                }
            }
        }
        var days = slots.Select(pair =>
        {
            var s = pair.Value;
            return new HourDay(pair.Key.Day, pair.Key.Hour, s.Soc == s.Expected ? s.SocTicks / s.Soc : null,
                s.Import == s.Expected ? s.HasImport : null, s.Soc == s.Expected ? s.Empty : null,
                s.Soc == s.Expected ? s.Full : null, s.EmptyImport == s.Expected ? s.EmptyWithImport : null,
                s.FullExport == s.Expected ? s.FullWithExport : null, (decimal)s.Expected / TimeSpan.TicksPerHour);
        }).ToArray();
        var hours = Enumerable.Range(0, 24).Select(hour =>
        {
            var entries = days.Where(d => d.Hour == hour).ToArray();
            Frequency Count(Func<HourDay, bool?> select) => new(entries.Count(d => select(d) == true),
                entries.Count(d => select(d).HasValue));
            var eligible = entries.Where(d => d.UsableSoc.HasValue).ToArray();
            return new HourProfile(hour, eligible.Length == 0 ? null :
                eligible.Sum(d => d.UsableSoc!.Value * d.ExpectedHours) / eligible.Sum(d => d.ExpectedHours),
                eligible.Length, Count(d => d.Import), Count(d => d.Empty), Count(d => d.Full),
                Count(d => d.EmptyImport), Count(d => d.FullExport), Count(d => d.Import == true ? d.EmptyImport : null));
        }).ToArray();
        return new(new(lowerHours, socHours, lowerKwh, importHours, lowerJoint), new(upperHours, socHours, upperKwh, exportHours, upperJoint),
            hours, detailHour is { } selected ? days.Where(d => d.Hour == selected).OrderBy(d => d.Day).ToArray() : []);
    }

    public static NightDetail Night(Interval[] rows, DateOnly evening, TimeZoneInfo zone, Battery? battery,
        decimal tolerance, Func<Interval, Battery?>? limits = null)
    {
        var local = evening.ToDateTime(new TimeOnly(22, 0));
        var cursor = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        var end = Calendar.Utc(evening.AddDays(2), zone);
        decimal hours = 0;
        bool measured = false, complete = true;
        foreach (var row in rows.SkipWhile(r => r.End <= cursor))
        {
            if (cursor >= end || row.Start > cursor || row.Pv is null) break;
            if (row.Pv > EnergyInsights.Tolerance(row))
                return new(evening, measured ? hours : null, complete && measured, row.Start);
            var limit = Limit(row, limits is null ? battery : limits(row), tolerance);
            if (limit is null) complete = false;
            else
            {
                measured = true;
                if (limit == "lower") hours += (decimal)(Math.Min(row.End.UtcTicks, end.UtcTicks) - cursor.UtcTicks) / TimeSpan.TicksPerHour;
            }
            cursor = row.End;
        }
        return new(evening, measured ? hours : null, false, null);
    }

    static IEnumerable<((DateOnly Day, int Hour) Key, long Ticks)> Split(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        while (start < end)
        {
            var local = TimeZoneInfo.ConvertTime(start, zone);
            var boundary = start.AddTicks(TimeSpan.TicksPerHour - local.TimeOfDay.Ticks % TimeSpan.TicksPerHour);
            if (boundary > end) boundary = end;
            if (zone.GetUtcOffset(boundary.AddTicks(-1)) != local.Offset)
            {
                // A non-hour-aligned offset transition can end the local bin early.
                long low = start.UtcTicks, high = boundary.UtcTicks;
                while (high - low > 1)
                {
                    long mid = low + (high - low) / 2;
                    if (zone.GetUtcOffset(new DateTimeOffset(mid, TimeSpan.Zero)) == local.Offset) low = mid;
                    else high = mid;
                }
                boundary = new(high, TimeSpan.Zero);
            }
            yield return ((DateOnly.FromDateTime(local.DateTime), local.Hour), (boundary - start).Ticks);
            start = boundary;
        }
    }
}
