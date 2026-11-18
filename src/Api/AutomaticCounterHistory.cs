using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public interface IDeviceCounterSource
{
    Task<PointProbeResult> ReadDeviceHistory(Plant plant, string device, ProbePoint[] points,
        DateTimeOffset start, DateTimeOffset end, int step, CancellationToken ct);
}

internal static class AutomaticCounterHistory
{
    internal static async Task<bool> Tick(Store db, Plant plant, HistoryScan scan, SungrowMapping mapping,
        IDeviceCounterSource source, IHistorySource history, TimeZoneInfo zone, DateTimeOffset now, CancellationToken ct)
    {
        if (mapping is not { DeviceCounters: true, Live: { Confirmed: true } live } ||
            !ProviderEndpoints.SupportsDeviceProbe(live.DeviceKey) || scan.CounterDue > now)
            return false;
        var today = Calendar.Day(now, zone);
        var identity = JsonSerializer.Serialize(new { Version = 2, live.DeviceKey, live.Soc, live.SocPercent, mapping.StepMinutes, plant.Zone });
        if (scan.CounterProfile != identity)
        {
            if (scan.CounterProfile is not null && scan.CounterDay > today.AddDays(-7))
                scan.CounterDay = today.AddDays(-7);
            scan.CounterUntil = null;
            scan.CounterData = null;
            scan.CounterAttempts = 0;
            scan.CounterProfile = identity;
        }
        var day = scan.CounterDay ??= today.AddDays(-30);
        if (day >= today) return false;
        var start = Calendar.Utc(day, zone);
        var end = Calendar.Utc(day.AddDays(1), zone);
        var from = scan.CounterUntil ?? start;
        var step = TimeSpan.FromMinutes(mapping.StepMinutes);
        var collected = scan.CounterData is { } data ? JsonSerializer.Deserialize<PointObservation[]>(data)! : [];
        scan.CounterDue = now.AddMinutes(1);
        try
        {
            if (from < end)
            {
                var to = WindowEnd(from, end, step, zone);
                if (to > from)
                {
                    var report = await source.ReadDeviceHistory(plant, live.DeviceKey, ProviderEndpoints.HistoryPoints,
                        from, to, mapping.StepMinutes, ct);
                    scan.CounterData = JsonSerializer.Serialize(Combine(collected, report.Points));
                }
                scan.CounterUntil = to > from ? to : from + step;
                scan.CounterAttempts = 0;
                return true;
            }

            var daily = await db.Aggregates.AsNoTracking().Where(a => a.PlantId == plant.Id && a.Start == start &&
                a.End == end && a.Origin == "provider" && a.Coverage == 1).ToArrayAsync(ct);
            if (mapping.Daily is { Points.Length: > 0 } dailyMapping &&
                !dailyMapping.Fields.All(field => daily.Any(a => a.Point == field.Name)))
            {
                var supplied = await history.ReadAggregates(plant.Id, start, end, ct);
                await Storage.UpsertAggregates(db, plant.Id, supplied, ct, fillOnly: true);
                daily = daily.Concat(supplied.Where(a => a.Start == start && a.End == end && a.Coverage == 1))
                    .DistinctBy(a => a.Point).ToArray();
            }
            var reportForDay = new PointProbeResult("Gerätehistorie", now, collected);
            var intervals = await SaveDay(db, plant.Id, live, reportForDay, daily, start, end, step, ct);
            bool dailyComplete = mapping.Daily is not { Points.Length: > 0 } requiredDaily ||
                requiredDaily.Fields.All(field => daily.Any(a => a.Point == field.Name));
            if (!dailyComplete && day >= today.AddDays(-2) && ++scan.CounterAttempts < 12)
            {
                if (!intervals.Any(r => r.End == end))
                    scan.CounterUntil = end - TimeSpan.FromHours(3);
                scan.CounterDue = now.AddHours(1);
                return true;
            }
            scan.LastSuccess = now;
            Advance(scan, day);
        }
        catch (Exception ex) when (ex is ProviderFailure or HttpRequestException or TimeoutException)
        {
            var failure = ex as ProviderFailure;
            var retry = failure?.RetryAfter;
            var delay = retry > TimeSpan.FromHours(1) ? retry.Value : TimeSpan.FromHours(1);
            scan.CounterDue = now + delay;
            if (++scan.CounterAttempts >= 3 && failure?.Category is not ("auth" or "key-restore" or "rate-limit"))
                Advance(scan, day);
        }
        return true;
    }

    static void Advance(HistoryScan scan, DateOnly day)
    {
        scan.CounterDay = day.AddDays(1);
        scan.CounterUntil = null;
        scan.CounterData = null;
        scan.CounterAttempts = 0;
    }

    internal static DateTimeOffset WindowEnd(DateTimeOffset start, DateTimeOffset end, TimeSpan step, TimeZoneInfo zone)
    {
        var first = TimeZoneInfo.ConvertTime(start, zone);
        if (zone.IsAmbiguousTime(first.DateTime)) return start;
        var last = start;
        for (var at = start + step; at <= end && at - start <= TimeSpan.FromHours(3); at += step)
        {
            var local = TimeZoneInfo.ConvertTime(at, zone);
            if (local.Offset != first.Offset || zone.IsAmbiguousTime(local.DateTime)) break;
            last = at;
        }
        return last;
    }

    static PointObservation[] Combine(PointObservation[] old, PointObservation[] fresh) =>
        old.Concat(fresh).GroupBy(p => p.Point).Select(group =>
        {
            var observed = group.Where(p => p.Values.Length > 0).ToArray();
            var first = observed.FirstOrDefault() ?? group.First();
            return first with
            {
                Unit = observed.Select(p => p.Unit).Distinct().Count() > 1 ? "conflicting" : first.Unit,
                Values = observed.SelectMany(p => p.Values).Distinct().ToArray()
            };
        }).ToArray();

    static async Task<Interval[]> SaveDay(Store db, Guid plant, LiveMapping live, PointProbeResult report, Aggregate[] daily,
        DateTimeOffset start, DateTimeOffset end, TimeSpan step, CancellationToken ct)
    {
        var (intervals, gaps) = DeviceCounterHistory.Analyze(report, start, end, step);
        var selectors = new (string Key, Func<Interval, decimal?> Value)[]
        {
            ("pv", r => r.Pv), ("load", r => r.Load), ("import", r => r.Import),
            ("export", r => r.Export), ("charge", r => r.Charge), ("discharge", r => r.Discharge)
        };
        var limits = daily.ToDictionary(a => a.Point, a => a.Kwh + .1m);
        var totals = selectors.ToDictionary(channel => channel.Key, channel => intervals.Sum(row => channel.Value(row) ?? 0));
        var accepted = totals.Where(pair => limits.TryGetValue(pair.Key, out var limit) && pair.Value <= limit)
            .Select(pair => pair.Key).ToHashSet();
        var filtered = intervals.Select(row => row with
        {
            Pv = accepted.Contains("pv") ? row.Pv : null, Load = accepted.Contains("load") ? row.Load : null,
            Import = accepted.Contains("import") ? row.Import : null, Export = accepted.Contains("export") ? row.Export : null,
            Charge = accepted.Contains("charge") ? row.Charge : null, Discharge = accepted.Contains("discharge") ? row.Discharge : null
        }).Where(row => selectors.Any(channel => channel.Value(row).HasValue)).ToArray();
        var existing = await db.Readings.AsNoTracking().Where(row => row.PlantId == plant && row.Start < end && row.End > start)
            .ToArrayAsync(ct);
        await Storage.Upsert(db, plant, DeviceCounterHistory.Merge(existing, filtered), ct);
        var gapTotals = gaps.GroupBy(g => g.Channel).ToDictionary(group => group.Key, group => group.Sum(g => g.Kwh));
        await Storage.UpsertAggregates(db, plant, gaps.Where(g => accepted.Contains(g.Channel) &&
            totals[g.Channel] + gapTotals[g.Channel] <= limits[g.Channel])
            .Select(g => new Aggregate(plant, g.Channel, g.Start, g.End, g.Kwh, 1,
                DeviceCounterHistory.GapOrigin, DeviceCounterHistory.Source)).ToArray(), ct, origin: DeviceCounterHistory.GapOrigin);

        // Only the previously confirmed device, point and fractional scale are eligible.
        if (live is not { Soc: 13141, SocPercent: false }) return intervals;
        var soc = report.Points.SingleOrDefault(point => point.Point == live.Soc && string.IsNullOrEmpty(point.Unit));
        var values = soc?.Values.Where(v => v.MeasuredAt >= start && v.MeasuredAt < end && v.Value is >= 0 and <= 1)
            .GroupBy(v => v.MeasuredAt!.Value).Where(group => group.Select(v => v.Value).Distinct().Count() == 1)
            .Select(group => new LiveMeasurement(plant, "soc", DeviceCounterHistory.SocSource, group.Key,
                report.FetchedAt, group.First().Value, "confirmed")).ToArray() ?? [];
        if (values.Length > 0)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "LiveMeasurements" SELECT * FROM jsonb_populate_recordset(NULL::"LiveMeasurements",
                    {JsonSerializer.Serialize(values)}::jsonb)
                ON CONFLICT ("PlantId", "MeasuredAt", "Channel", "Source") DO NOTHING
                """, ct);
        return intervals;
    }
}
