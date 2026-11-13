using CultureInfo = System.Globalization.CultureInfo;
using NumberStyles = System.Globalization.NumberStyles;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Application;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

/// <summary>Long-term analyses built on daily values: storage potential and data availability.</summary>
public static class AnalyticsEndpoints
{
    static readonly string[] Channels = ["pv", "load", "import", "export", "charge", "discharge"];

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/storage-potential", async ([AsParameters] ViewPeriod query, string? extra, decimal? efficiency,
            decimal? importPrice, decimal? exportPrice, decimal? investment, Queries q, Store store, CancellationToken ct) =>
        {
            var plant = await q.Plant(ct);
            var view = await q.Read(plant, await q.PeriodFor(plant, query, ct), ct);
            var capacities = (extra ?? "0,2.5,5,7.5,10,15").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? c :
                    throw new ArgumentException("Ungültige Kapazitätsliste.")).Prepend(0).ToArray();
            // Prefer stored tariffs valid at the end of the period; otherwise transparent defaults.
            var tariffs = await store.Prices.AsNoTracking().Where(t => t.PlantId == plant.Id).ToArrayAsync(ct);
            decimal? Price(TariffKind kind) => tariffs.Where(t => t.Kind == kind && t.Start < view.Period.End)
                .OrderByDescending(t => t.Start).FirstOrDefault()?.Price;
            var storedImport = Price(TariffKind.Import);
            var storedExport = Price(TariffKind.Export);
            var assumptions = new StorageAssumptions(capacities, efficiency ?? .9m,
                importPrice ?? storedImport ?? .30m, exportPrice ?? storedExport ?? .08m, investment);
            var daily = view.Daily().Where(d => d.Day < view.Today).ToArray();
            int days = Math.Max(0, Math.Min(view.Period.End.DayNumber, view.Today.DayNumber) - view.Period.Start.DayNumber);
            var result = StoragePotential.Evaluate(daily, assumptions, days);
            return new
            {
                view.Period, result,
                prices = new { import = assumptions.ImportPrice, export = assumptions.ExportPrice,
                    importSource = importPrice.HasValue ? "input" : storedImport.HasValue ? "tariff" : "default",
                    exportSource = exportPrice.HasValue ? "input" : storedExport.HasValue ? "tariff" : "default" },
                battery = view.Battery is { } configured ? new { configured.Capacity, usable = configured.Usable, estimated = false,
                    capacityEstimate = (decimal?)null, maxDailyCharge = (decimal?)null } :
                    view.Estimate is { } e ? new { e.Battery.Capacity, usable = e.Battery.Usable, estimated = true,
                        capacityEstimate = e.CapacityKwh, maxDailyCharge = e.MaxDailyChargeKwh } : null
            };
        });

        api.MapGet("/data-quality", async (Queries q, Store store, CancellationToken ct) =>
        {
            var plant = await q.Plant(ct);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
            var today = Calendar.Day(DateTimeOffset.UtcNow, zone);
            var aggregates = await store.Aggregates.AsNoTracking().Where(a => a.PlantId == plant.Id && a.Origin == "provider")
                .Select(a => new { a.Point, a.Start, a.Revision }).ToArrayAsync(ct);
            var readings = await store.Readings.AsNoTracking().Where(r => r.PlantId == plant.Id)
                .Select(r => new { r.Start, pv = r.Pv != null, load = r.Load != null, import = r.Import != null,
                    export = r.Export != null, charge = r.Charge != null, discharge = r.Discharge != null,
                    soc = r.Soc != null, r.SourceRevision }).ToArrayAsync(ct);
            var socSamples = await store.LiveMeasurements.AsNoTracking().Where(m => m.PlantId == plant.Id &&
                m.Source == DeviceCounterHistory.SocSource).Select(m => m.MeasuredAt).ToArrayAsync(ct);
            if (aggregates.Length == 0 && readings.Length == 0) return Results.Ok(new { first = (DateOnly?)null, months = Array.Empty<object>() });

            var dailyDays = aggregates.Select(a => (a.Point, Day: Calendar.Day(a.Start, zone))).ToHashSet();
            var first = new[] { aggregates.Length == 0 ? (DateOnly?)null : aggregates.Min(a => Calendar.Day(a.Start, zone)),
                readings.Length == 0 ? null : readings.Min(r => Calendar.Day(r.Start, zone)) }.Where(d => d.HasValue).Min()!.Value;
            string Month(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var readingMonths = readings.ToLookup(r => Month(Calendar.Day(r.Start, zone)));
            var socMonths = socSamples.ToLookup(at => Month(Calendar.Day(at, zone)));
            var months = new List<object>();
            var missing = Channels.ToDictionary(c => c, _ => new List<(DateOnly Start, DateOnly End)>());
            for (var month = new DateOnly(first.Year, first.Month, 1); month <= today; month = month.AddMonths(1))
            {
                var end = month.AddMonths(1) < today ? month.AddMonths(1) : today;
                var start = month < first ? first : month;
                int expected = Math.Max(0, end.DayNumber - start.DayNumber);
                var daily = Channels.ToDictionary(c => c, c =>
                {
                    int count = 0;
                    for (var d = start; d < end; d = d.AddDays(1))
                    {
                        if (dailyDays.Contains((c, d))) { count++; continue; }
                        var spans = missing[c];
                        // Merge consecutive missing days into ranges for the gap list.
                        if (spans.Count > 0 && spans[^1].End == d) spans[^1] = (spans[^1].Start, d.AddDays(1));
                        else spans.Add((d, d.AddDays(1)));
                    }
                    return count;
                });
                var rows = readingMonths[Month(month)].ToArray();
                months.Add(new
                {
                    month = Month(month), days = expected, daily,
                    intervals = new { total = rows.Length, pv = rows.Count(r => r.pv), load = rows.Count(r => r.load),
                        import = rows.Count(r => r.import), export = rows.Count(r => r.export), charge = rows.Count(r => r.charge),
                        discharge = rows.Count(r => r.discharge), complete = rows.Count(r => r.pv && r.load && r.import && r.export && r.charge && r.discharge) },
                    socSamples = socMonths[Month(month)].Count()
                });
            }
            var sources = aggregates.GroupBy(a => new { a.Point, a.Revision }).Select(g => new { g.Key.Point, g.Key.Revision,
                days = g.Count(), first = g.Min(a => Calendar.Day(a.Start, zone)), last = g.Max(a => Calendar.Day(a.Start, zone)) })
                .Concat(readings.GroupBy(r => r.SourceRevision).Select(g => new { Point = "intervals", Revision = g.Key,
                    days = g.Select(r => Calendar.Day(r.Start, zone)).Distinct().Count(),
                    first = g.Min(r => Calendar.Day(r.Start, zone)), last = g.Max(r => Calendar.Day(r.Start, zone)) }))
                .OrderBy(s => s.Point).ThenBy(s => s.first).ToArray();
            return Results.Ok(new
            {
                first, today, months, sources,
                gaps = missing.ToDictionary(p => p.Key, p => p.Value.Select(s => new { start = s.Start, end = s.End,
                    days = s.End.DayNumber - s.Start.DayNumber }).ToArray()),
                socSamples = socSamples.Length
            });
        });
    }
}
