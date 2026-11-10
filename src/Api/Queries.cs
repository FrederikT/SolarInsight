using Microsoft.EntityFrameworkCore;
using SolarInsight.Application;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;
using CultureInfo = System.Globalization.CultureInfo;
using System.Text.Json;

namespace SolarInsight.Api;

public record ViewPeriod(string Kind, DateOnly? Anchor = null, DateOnly? Start = null, DateOnly? End = null);
public record Report(Period Period, string TimeZone, string Revision, double? ResolutionMinutes, Metrics Metrics,
    Cost Cost, bool Incomplete, string[] Warnings, Metrics? ProviderMetrics, decimal? ProviderExportDifference);
public record DataView(Plant Plant, TimeZoneInfo Zone, Period Period, Interval[] Rows, Tariffs Tariffs,
    Battery? Battery, bool KnownPaths, bool ConfigurationBoundary, string Revision, Aggregate[] Aggregates)
{
    public (DateTimeOffset Since, Battery? Battery)[] BatteryTimeline { get; init; } = [];

    public Battery? BatteryFor(Interval row)
    {
        foreach (var (since, battery) in BatteryTimeline)
        {
            if (row.Start >= since) return battery;
            if (row.End > since) return null;
        }
        return null;
    }

    public decimal Hours => (decimal)(Calendar.Utc(Period.End, Zone) - Calendar.Utc(Period.Start, Zone)).Ticks /
        TimeSpan.TicksPerHour;
    public Report Report() => CreateReport(Period, Rows);
    public Report CreateReport(Period period, Interval[] rows)
    {
        decimal hours = (decimal)(Calendar.Utc(period.End, Zone) - Calendar.Utc(period.Start, Zone)).Ticks /
            TimeSpan.TicksPerHour;
        var metrics = Analysis.Summarize(rows, hours, KnownPaths);
        var start = Calendar.Utc(period.Start, Zone);
        var end = Calendar.Utc(period.End, Zone);
        var providerRows = Aggregates.Where(a => a.Start >= start && a.End <= end).GroupBy(a => (a.Start, a.End))
            .Select(g =>
            {
                decimal? Value(string key) => g.FirstOrDefault(a => a.Point == key)?.Kwh;
                return new Interval(g.Key.Start, g.Key.End, Value("pv"), Value("load"), Value("import"), Value("export"),
                    Value("charge"), Value("discharge"));
            }).ToArray();
        var provider = providerRows.Length == 0 ? null : Analysis.Summarize(providerRows, hours, KnownPaths);
        decimal? exportDifference = provider?.Export.Coverage >= .999999m && metrics.Export.Coverage >= .999999m
            ? provider.Export.Kwh - metrics.Export.Kwh : null;
        var cost = Tariffs.Evaluate(rows.Length > 0 ? rows : providerRows, period, Zone);
        if (rows.Length == 0 && provider is not null) metrics = provider;
        bool incomplete = period.End > Calendar.Day(DateTimeOffset.UtcNow, Zone);
        var warnings = new List<string>();
        if (metrics.Coverage < .999999m) warnings.Add("Teildaten. Energiemengen beziehen sich auf erfasste Intervalle.");
        if (!cost.Complete) warnings.Add("Kosten teilweise erfasst. Messwerte oder Tarife fehlen.");
        if (cost.Estimated) warnings.Add("Energie an Tarifgrenzen zeitanteilig verteilt.");
        if (metrics.Retention is < 0 or > 1 || metrics.Autarky is < 0 or > 1)
            warnings.Add("Energieherkunft oder Periodengrenze prüfen.");
        if (incomplete) warnings.Add("Laufender Zeitraum.");
        if (rows.Any(r => r.Quality is "trapezoid" or "counter-mixed")) warnings.Add("Zeitreihenenergie aus Momentanwerten trapezförmig angenähert.");
        if (rows.Any(r => r.SourceRevision == DeviceCounterHistory.Source))
            warnings.Add("Gerätezähler liefern Energiedifferenzen im Messraster. PV-Ertrag daraus bestätigt keine getrennte AC-PV-Leistung.");
        if (provider is not null) warnings.Add("Sungrow-Tagesaggregate werden separat von integrierten Zeitreihen ausgewiesen.");
        return new(period, Zone.Id, Revision, rows.Length == 0 ? null :
            rows.Max(r => (r.End - r.Start).TotalMinutes), metrics, cost, incomplete, warnings.ToArray(), provider, exportDifference);
    }
}

public sealed class Queries(Store db, RuntimeOptions options)
{
    public async Task<Plant> Plant(CancellationToken ct) => await db.Plants.AsNoTracking().SingleAsync(p =>
        options.Demo ? p.Id == Storage.DemoId : p.Active && !p.Synthetic, ct);

    public async Task<Period> PeriodFor(Plant plant, ViewPeriod query, CancellationToken ct)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        var today = Calendar.Day(DateTimeOffset.UtcNow, zone);
        if (query.Kind == "all")
        {
            if (plant.Synthetic) return new(Demo.Start, Demo.End);
            var firstReading = await db.Readings.Where(r => r.PlantId == plant.Id).MinAsync(r => (DateTimeOffset?)r.Start, ct);
            var firstAggregate = await db.Aggregates.Where(r => r.PlantId == plant.Id).MinAsync(r => (DateTimeOffset?)r.Start, ct);
            var first = firstAggregate is not null && (firstReading is null || firstAggregate < firstReading)
                ? firstAggregate : firstReading;
            return new(first is { } at ? Calendar.Day(at, zone) : today.AddDays(-1), today.AddDays(1));
        }
        if (query.Kind == "custom" && query.Start.HasValue && query.End.HasValue)
            return Validate(new(query.Start.Value, query.End.Value));
        return Calendar.Select(query.Anchor ?? (plant.Synthetic ? Demo.End.AddDays(-1) : today), query.Kind);
    }

    public static Period Validate(Period p)
    {
        if (p.Start.Year < 2000 || p.End.Year > 2100 || p.Days is < 1 or > 732)
            throw new ArgumentException("Zeitraum muss zwischen einem Tag und 732 Tagen liegen.");
        return p;
    }

    public async Task<DataView> Read(Plant plant, Period period, CancellationToken ct)
    {
        Validate(period);
        await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        plant = await db.Plants.AsNoTracking().SingleAsync(p => p.Id == plant.Id, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        var start = Calendar.Utc(period.Start, zone);
        var end = Calendar.Utc(period.End, zone);
        var rows = await db.Readings.AsNoTracking().Where(r => r.PlantId == plant.Id && r.Start >= start &&
            r.End <= end).OrderBy(r => r.Start).Take(220001).ToArrayAsync(ct);
        if (rows.Length > 220000) throw new ArgumentException("Zu viele Messpunkte. Bitte Zeitraum verkleinern.");
        var configurations = await db.Configurations.AsNoTracking().Where(c => c.PlantId == plant.Id &&
            c.Since < period.End).OrderByDescending(c => c.Since).ToArrayAsync(ct);
        var config = configurations.FirstOrDefault(c => c.Since <= period.Start);
        var prices = await db.Prices.AsNoTracking().Where(t => t.PlantId == plant.Id).ToArrayAsync(ct);
        var aggregates = await db.Aggregates.AsNoTracking().Where(a => a.PlantId == plant.Id && a.Origin == "provider" &&
            a.Start >= start && a.End <= end).ToArrayAsync(ct);
        var connection = plant.ProviderId is null ? null : await db.Connections.AsNoTracking()
            .SingleOrDefaultAsync(c => c.PlantId == plant.Id, ct);
        bool knownPaths = plant.Synthetic || (connection is not null &&
            JsonSerializer.Deserialize<SungrowMapping>(connection.Capabilities)?.ConfirmedAcPaths == true);
        var result = new DataView(plant, zone, period, rows.Select(r => r.Value()).ToArray(), new(prices.Select(p => p.Value())),
            config is null ? null : JsonSerializer.Deserialize<Battery>(config.Parameters), knownPaths,
            configurations.Any(c => c.Since > period.Start), plant.Revision.ToString(CultureInfo.InvariantCulture), aggregates)
        {
            BatteryTimeline = configurations.Select(c => (Calendar.Utc(c.Since, zone),
                JsonSerializer.Deserialize<Battery>(c.Parameters))).ToArray()
        };
        await snapshot.CommitAsync(ct);
        return result;
    }

    public static object[] Buckets(DataView view)
    {
        if (view.Period.Days <= 1)
            return DayRows(view).Select(r => (object)new
            {
                start = r.Start, end = r.End, local = TimeZoneInfo.ConvertTime(r.Start, view.Zone).ToString("HH:mm zzz"),
                pv = r.Pv / r.Hours, load = r.Load / r.Hours, import = r.Import / r.Hours, export = r.Export / r.Hours,
                charge = r.Charge / r.Hours, discharge = r.Discharge / r.Hours, soc = r.Soc, r.Quality, r.SourceRevision
            }).ToArray();

        bool monthly = view.Period.Days > 93;
        DateOnly Bucket(DateOnly day) => monthly ? new(day.Year, day.Month, 1) : day;
        var byPeriod = view.Rows.ToLookup(r => Bucket(Calendar.Day(r.Start, view.Zone)));
        var result = new List<object>();
        for (var cursor = view.Period.Start; cursor < view.Period.End;)
        {
            var key = Bucket(cursor);
            var next = monthly ? key.AddMonths(1) : key.AddDays(1);
            var end = next < view.Period.End ? next : view.Period.End;
            var report = view.CreateReport(new(cursor, end), byPeriod[key].ToArray());
            var m = report.Metrics;
            result.Add(new { day = cursor, pv = m.Pv.Kwh, load = m.Load.Kwh, import = m.Import.Kwh,
                export = m.Export.Kwh, charge = m.Charge.Kwh, discharge = m.Discharge.Kwh, soc = m.SocMean,
                cost = report.Cost.Net, coverage = m.Coverage });
            cursor = end;
        }
        return result.ToArray();
    }

    static IEnumerable<Interval> DayRows(DataView view)
    {
        if (view.Rows.Length == 0) yield break;
        var cursor = Calendar.Utc(view.Period.Start, view.Zone);
        foreach (var row in view.Rows)
        {
            if (cursor < row.Start) yield return new(cursor, row.Start, null, null, Quality: "gap");
            yield return row;
            cursor = row.End;
        }
        var end = Calendar.Utc(view.Period.End, view.Zone);
        if (cursor < end) yield return new(cursor, end, null, null, Quality: "gap");
    }

    public static object[] Heatmap(DataView view) => view.Rows.GroupBy(row =>
    {
        var local = TimeZoneInfo.ConvertTime(row.Start, view.Zone);
        return (Day: DateOnly.FromDateTime(local.DateTime), local.Hour, local.Offset);
    }).Select(g => (object)new { day = g.Key.Day, hour = g.Key.Hour,
        offset = g.Key.Offset.ToString(), import = g.Any(r => r.Import.HasValue) ? g.Sum(r => r.Import) : null,
        export = g.Any(r => r.Export.HasValue) ? g.Sum(r => r.Export) : null }).ToArray();
}

