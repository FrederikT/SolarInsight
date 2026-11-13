using Microsoft.EntityFrameworkCore;
using SolarInsight.Application;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;
using CultureInfo = System.Globalization.CultureInfo;
using System.Text.Json;

namespace SolarInsight.Api;

public record ViewPeriod(string Kind, DateOnly? Anchor = null, DateOnly? Start = null, DateOnly? End = null);
public record Report(Period Period, string TimeZone, string Revision, double? ResolutionMinutes, Metrics Metrics,
    Cost Cost, bool Incomplete, string[] Warnings, Metrics? ProviderMetrics, decimal? ProviderExportDifference)
{
    public DeviceCounterHistory.CounterSpan[] CounterGaps { get; init; } = [];
    public SocSample[] HistoricalSoc { get; init; } = [];
}
public record SocSample(DateTimeOffset At, decimal Value);
public record DataView(Plant Plant, TimeZoneInfo Zone, Period Period, Interval[] Rows, Tariffs Tariffs,
    Battery? Battery, bool KnownPaths, bool ConfigurationBoundary, string Revision, Aggregate[] Aggregates)
{
    public (DateTimeOffset Since, Battery? Battery)[] BatteryTimeline { get; init; } = [];
    public DeviceCounterHistory.CounterSpan[] CounterGaps { get; init; } = [];
    public SocSample[] HistoricalSoc { get; init; } = [];
    /// <summary>Battery parameters estimated from SOC and battery energy when none are configured.</summary>
    public BatteryEstimate? Estimate { get; init; }

    /// <summary>Configured battery, or the estimate when the plant has no battery configuration at all.</summary>
    public Battery? EffectiveBattery => ConfigurationBoundary ? null : Battery ??
        (BatteryTimeline.Length == 0 ? Estimate?.Battery : null);
    public bool BatteryEstimated => !ConfigurationBoundary && Battery is null && BatteryTimeline.Length == 0 && Estimate is not null;
    public Func<Interval, Battery?>? Limits => BatteryTimeline.Length == 0 ? null : BatteryFor;
    public DateOnly Today => Calendar.Day(DateTimeOffset.UtcNow, Zone);

    /// <summary>
    /// Daily energies per local day: provider daily values first; for days without them, interval sums of each
    /// channel that covers the whole day (interval-only plants and the synthetic demo).
    /// </summary>
    public DailyValues[] Daily()
    {
        var provider = Aggregates.GroupBy(a => Calendar.Day(a.Start, Zone)).ToDictionary(g => g.Key, g => g.ToArray());
        var intervals = Rows.ToLookup(r => Calendar.Day(r.Start, Zone));
        var days = provider.Keys.Concat(intervals.Select(g => g.Key)).Distinct().Order();
        return days.Select(day =>
        {
            var aggregates = provider.GetValueOrDefault(day) ?? [];
            var rows = intervals[day].ToArray();
            decimal expected = (decimal)(Calendar.Utc(day.AddDays(1), Zone) - Calendar.Utc(day, Zone)).Ticks / TimeSpan.TicksPerHour;
            decimal? Value(string key, Func<Interval, decimal?> select)
            {
                if (aggregates.FirstOrDefault(a => a.Point == key) is { } a) return a.Kwh;
                var valid = rows.Where(r => select(r).HasValue).ToArray();
                return valid.Length > 0 && Math.Abs(valid.Sum(r => r.Hours) - expected) < .0001m ? valid.Sum(r => select(r)!.Value) : null;
            }
            return new DailyValues(day, Value("pv", r => r.Pv), Value("load", r => r.Load), Value("import", r => r.Import),
                Value("export", r => r.Export), Value("charge", r => r.Charge), Value("discharge", r => r.Discharge));
        }).ToArray();
    }

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
    /// <summary>Hours of the period that have already passed; equals <see cref="Hours"/> for past periods.</summary>
    public decimal ElapsedHours
    {
        get
        {
            var start = Calendar.Utc(Period.Start, Zone);
            var now = DateTimeOffset.UtcNow;
            return now <= start ? 0 : Math.Min(Hours, (decimal)(now - start).Ticks / TimeSpan.TicksPerHour);
        }
    }
    public Report Report() => CreateReport(Period, Rows);
    public Report CreateReport(Period period, Interval[] rows)
    {
        var start = Calendar.Utc(period.Start, Zone);
        var end = Calendar.Utc(period.End, Zone);
        // Coverage of a running period refers to the time that has already passed, not to future days.
        var available = end < DateTimeOffset.UtcNow ? end : DateTimeOffset.UtcNow > start ? DateTimeOffset.UtcNow : end;
        decimal hours = (decimal)(available - start).Ticks / TimeSpan.TicksPerHour;
        var metrics = Analysis.Summarize(rows, hours);
        var providerRows = Aggregates.Where(a => a.Start >= start && a.End <= end).GroupBy(a => (a.Start, a.End))
            .Select(g =>
            {
                decimal? Value(string key) => g.FirstOrDefault(a => a.Point == key)?.Kwh;
                return new Interval(g.Key.Start, g.Key.End, Value("pv"), Value("load"), Value("import"), Value("export"),
                    Value("charge"), Value("discharge"));
            }).ToArray();
        // Provider daily values only exist for finished days, so their basis ends at the start of today.
        var todayStart = Calendar.Utc(Calendar.Day(DateTimeOffset.UtcNow, Zone), Zone);
        var providerEnd = end < todayStart ? end : todayStart > start ? todayStart : start;
        decimal providerHours = (decimal)(providerEnd - start).Ticks / TimeSpan.TicksPerHour;
        var provider = providerRows.Length == 0 ? null : Analysis.Summarize(providerRows, providerHours > 0 ? providerHours : hours);
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
            rows.Max(r => (r.End - r.Start).TotalMinutes), metrics, cost, incomplete, warnings.ToArray(), provider, exportDifference)
        {
            CounterGaps = period == Period ? CounterGaps : [],
            HistoricalSoc = period == Period ? HistoricalSoc : []
        };
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

    public const int MaxDays = 7320;

    public static Period Validate(Period p)
    {
        // Long periods are aggregated server-side (monthly buckets, daily energy balance), so multi-year
        // ranges such as "seit Datenbeginn" are allowed. Interval-heavy views keep their own limits.
        if (p.Start.Year < 2000 || p.End.Year > 2100 || p.Days is < 1 or > MaxDays)
            throw new ArgumentException($"Zeitraum muss zwischen einem Tag und {MaxDays} Tagen liegen.");
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
        var counterGaps = await db.Aggregates.AsNoTracking().Where(a => a.PlantId == plant.Id &&
            a.Origin == DeviceCounterHistory.GapOrigin && a.Start >= start && a.End <= end).OrderBy(a => a.Start)
            .Select(a => new DeviceCounterHistory.CounterSpan(a.Point, a.Start, a.End, a.Kwh)).ToArrayAsync(ct);
        var socSamples = await db.LiveMeasurements.AsNoTracking().Where(m => m.PlantId == plant.Id &&
            m.Source == DeviceCounterHistory.SocSource && m.MeasuredAt >= start && m.MeasuredAt <= end)
            .OrderBy(m => m.MeasuredAt).Select(m => new SocSample(m.MeasuredAt, m.Value)).ToArrayAsync(ct);
        var connection = plant.ProviderId is null ? null : await db.Connections.AsNoTracking()
            .SingleOrDefaultAsync(c => c.PlantId == plant.Id, ct);
        bool knownPaths = plant.Synthetic || (connection is not null &&
            JsonSerializer.Deserialize<SungrowMapping>(connection.Capabilities)?.ConfirmedAcPaths == true);
        var intervals = WithSoc(rows.Select(r => r.Value()).ToArray(), socSamples);
        var result = new DataView(plant, zone, period, intervals, new(prices.Select(p => p.Value())),
            config is null ? null : JsonSerializer.Deserialize<Battery>(config.Parameters), knownPaths,
            configurations.Any(c => c.Since > period.Start), plant.Revision.ToString(CultureInfo.InvariantCulture), aggregates)
        {
            BatteryTimeline = configurations.Select(c => (Calendar.Utc(c.Since, zone),
                JsonSerializer.Deserialize<Battery>(c.Parameters))).ToArray(),
            CounterGaps = counterGaps, HistoricalSoc = period.Days == 1 ? socSamples.Where(s => s.At < end).ToArray() : []
        };
        result = result with { Estimate = BatteryEstimation.Estimate(intervals, result.Daily()) };
        await snapshot.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// Historical SOC is stored as separate device samples. Attach the sample at the interval end (the closing
    /// state, as in the synthetic data) or, failing that, at the interval start to intervals without SOC.
    /// </summary>
    public static Interval[] WithSoc(Interval[] rows, SocSample[] samples)
    {
        if (samples.Length == 0) return rows;
        var byTime = samples.GroupBy(s => s.At.UtcTicks).ToDictionary(g => g.Key, g => g.First().Value);
        return rows.Select(r => r.Soc is not null ? r :
            byTime.TryGetValue(r.End.UtcTicks, out var end) ? r with { Soc = end } :
            byTime.TryGetValue(r.Start.UtcTicks, out var begin) ? r with { Soc = begin } : r).ToArray();
    }

    public static object[] Buckets(DataView view, bool providerDaily = false)
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
            var m = providerDaily ? report.ProviderMetrics : report.Metrics;
            // SOC only exists in intervals; keep it visible when the energies come from daily values.
            result.Add(new { day = cursor, pv = m?.Pv.Kwh, load = m?.Load.Kwh, import = m?.Import.Kwh,
                export = m?.Export.Kwh, charge = m?.Charge.Kwh, discharge = m?.Discharge.Kwh,
                soc = report.Metrics.SocMean, socMin = report.Metrics.SocMin, socMax = report.Metrics.SocMax,
                autarky = m?.Autarky, retention = m?.Retention,
                cost = report.Cost.Net, coverage = m?.Coverage });
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

    public static object[] Heatmap(DataView view)
    {
        (DateOnly Day, int Hour, TimeSpan Offset) Key(DateTimeOffset at)
        {
            var local = TimeZoneInfo.ConvertTime(at, view.Zone);
            return (DateOnly.FromDateTime(local.DateTime), local.Hour, local.Offset);
        }
        var grouped = view.Rows.ToLookup(row => Key(row.Start));
        var result = new List<object>();
        var start = Calendar.Utc(view.Period.Start, view.Zone);
        var end = Calendar.Utc(view.Period.End, view.Zone);
        for (var at = start; at < end; at = at.AddHours(1))
        {
            var key = Key(at);
            var rows = grouped[key];
            result.Add(new { day = key.Day, hour = key.Hour, offset = key.Offset.ToString(),
                import = rows.Any(row => row.Import.HasValue) ? rows.Sum(row => row.Import) : null,
                export = rows.Any(row => row.Export.HasValue) ? rows.Sum(row => row.Export) : null });
        }
        return result.ToArray();
    }
}

