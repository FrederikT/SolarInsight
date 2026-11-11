using CultureInfo = System.Globalization.CultureInfo;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Application;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public record PlantUpdate(string Name, string Zone, DateOnly Since, decimal PvKwp, Battery? Battery, EventRule Rule);
public record TariffInput(TariffKind Kind, DateOnly Start, DateOnly? End, decimal Price);

public static class Endpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/plant", async (Queries queries, Store db, CancellationToken ct) =>
        {
            var plant = await queries.Plant(ct);
            var devices = await db.Devices.AsNoTracking().Where(d => d.PlantId == plant.Id)
                .Select(d => new { d.Model, d.Type }).ToArrayAsync(ct);
            bool mapped = plant.ProviderId is not null;
            var connection = mapped ? await db.Connections.AsNoTracking().SingleOrDefaultAsync(c => c.PlantId == plant.Id, ct) : null;
            var mapping = connection is null ? null : JsonSerializer.Deserialize<SungrowMapping>(connection.Capabilities);
            return new { plant.Name, plant.Zone, plant.Region, plant.Synthetic, devices,
                defaultAnchor = plant.Synthetic ? Demo.End.AddDays(-1) :
                    Calendar.Day(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(plant.Zone)),
                capabilities = new { energy = plant.Synthetic || mapped, timeseries = plant.Synthetic || mapping?.Points.Length > 0,
                    events = plant.Synthetic || mapping?.Grid is not null,
                    simulation = plant.Synthetic || mapping is { Pv: not null, Load: not null, ConfirmedAcPaths: true } },
                providerStatus = plant.Synthetic ? "Synthetische Demodaten" : mapped ? "Sungrow-Anlage zugeordnet" : "Sungrow-Verbindung ausstehend" };
        });
        api.MapGet("/energy-insights", async ([AsParameters] ViewPeriod query, int? hour, int? mix, int? offset,
            Queries q, CancellationToken ct) =>
        {
            if (hour is < 0 or > 23 || mix is < 0 or > 3 || offset is < 0)
                return Results.BadRequest();
            var view = await Read(q, query, ct);
            var rule = JsonSerializer.Deserialize<EventRule>(view.Plant.Rules) ?? new();
            var battery = view.ConfigurationBoundary ? null : view.Battery;
            var flow = EnergyInsights.Analyze(view.Rows, view.KnownPaths, battery, rule.SocTolerance, view.Zone, view.Period.Days == 1);
            var storage = StorageInsights.Profile(view.Rows, view.Period, view.Zone, battery, rule.SocTolerance, hour, view.BatteryFor);
            var phases = mix is { } category ? flow.Phases.Where(p => (int)p.Mix == category).ToArray() : [];
            NightDetail[] nights = [];
            if (hour is >= 22 or < 6)
            {
                var byDay = view.Rows.ToLookup(r => Calendar.Day(r.Start, view.Zone));
                nights = storage.Days.Where(d => d.Import == true).Select(d => hour < 6 ? d.Day.AddDays(-1) : d.Day)
                    .Distinct().Select(d => StorageInsights.Night(byDay[d].Concat(byDay[d.AddDays(1)]).ToArray(),
                        d, view.Zone, battery, rule.SocTolerance, view.BatteryFor)).ToArray();
            }
            return Results.Ok(new
            {
                view.Period, zone = view.Zone.Id, view.Hours, view.KnownPaths,
                latest = view.Rows.Length == 0 ? (DateTimeOffset?)null : view.Rows[^1].End,
                start = Calendar.Utc(view.Period.Start, view.Zone), end = Calendar.Utc(view.Period.End, view.Zone),
                flow.Breakdown, flow.Points, storage,
                totals = flow.Phases.GroupBy(p => p.Mix).Select(g => new GridTotal(g.Key,
                    g.Sum(p => (decimal)(p.End - p.Start).Ticks / TimeSpan.TicksPerHour), g.Sum(p => p.Kwh))),
                phases = phases.Skip(offset ?? 0).Take(40), phaseCount = phases.Length, nights
            });
        });
        api.MapGet("/overview", async ([AsParameters] ViewPeriod query, string? compare, Queries q, CancellationToken ct) =>
        {
            var view = await Read(q, query, ct);
            Report? comparison = null;
            if (compare is "previous" or "year")
            {
                var today = Calendar.Day(DateTimeOffset.UtcNow, view.Zone);
                var previous = Calendar.Compare(view.Period, query.Kind, compare == "year", today);
                if (previous.Days > 0) comparison = (await q.Read(view.Plant, previous, ct)).Report();
            }
            return new { report = view.Report(), comparison, buckets = Queries.Buckets(view),
                providerBuckets = view.Period.Days > 1 && view.Aggregates.Length > 0 ? Queries.Buckets(view, true) : null };
        });
        api.MapGet("/timeseries", async ([AsParameters] ViewPeriod query, Queries q, CancellationToken ct) =>
        {
            var view = await Read(q, query, ct);
            return new { report = view.Report(), points = Queries.Buckets(view),
                unit = view.Period.Days == 1 ? "kW" : "kWh" };
        });
        api.MapGet("/heatmap", async ([AsParameters] ViewPeriod query, Queries q, CancellationToken ct) =>
        {
            var view = await Read(q, query, ct);
            if (view.Period.Days > 93) throw new ArgumentException("Heatmap benötigt höchstens 93 Tage.");
            return new { view.Period, timeZone = view.Zone.Id, view.Revision, points = Queries.Heatmap(view) };
        });
        api.MapGet("/events", async ([AsParameters] ViewPeriod query, int? offset, int? limit, string? type,
            decimal? threshold, Queries q, CancellationToken ct) =>
        {
            var view = await Read(q, query, ct);
            var rule = JsonSerializer.Deserialize<EventRule>(view.Plant.Rules) ?? new();
            if (threshold.HasValue) rule = rule with { Kw = threshold.Value };
            Validate(rule);
            var events = Analysis.Events(view.Rows, view.ConfigurationBoundary ? null : view.Battery, rule)
                .Where(e => type is null || e.Type == type).ToArray();
            return new { view.Period, timeZone = view.Zone.Id, view.Revision, total = events.Length,
                events = events.Skip(Math.Clamp(offset ?? 0, 0, 100000)).Take(Math.Clamp(limit ?? 30, 1, 100)).ToArray() };
        });
        api.MapGet("/surplus", async ([AsParameters] ViewPeriod query, decimal? kw, int? from, int? to, Queries q,
            CancellationToken ct) =>
        {
            var view = await Read(q, query, ct);
            var window = new Window(from ?? 0, to ?? 1440);
            window.Validate();
            decimal limit = kw ?? 1;
            if (limit is < 0 or > 100) throw new ArgumentException("Ungültige Aufnahmeleistung.");
            var valid = view.Rows.Where(r => r.Export.HasValue).ToArray();
            decimal Captured(Interval row) => Math.Min(row.Export!.Value / row.Hours, limit) * window.Hours(row, view.Zone);
            var days = valid.GroupBy(row => Calendar.Day(row.Start, view.Zone)).OrderBy(group => group.Key)
                .Select(group => new { day = group.Key, export = group.Sum(row => row.Export), captured = group.Sum(Captured) });
            var months = valid.GroupBy(row => Calendar.Day(row.Start, view.Zone).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture))
                .OrderBy(group => group.Key).Select(group => new { month = group.Key, export = group.Sum(row => row.Export),
                    captured = group.Sum(Captured), hours = group.Sum(row => row.Hours) });
            var seasons = valid.GroupBy(row => Calendar.Select(Calendar.Day(row.Start, view.Zone), "season").Start)
                .OrderBy(group => group.Key).Select(group => new { start = group.Key, export = group.Sum(row => row.Export),
                    captured = group.Sum(Captured), hours = group.Sum(row => row.Hours) });
            var hourTotals = new decimal[24];
            var hourCoverage = new decimal[24];
            var hourWindows = Enumerable.Range(0, 24).Select(hour => new Window(hour * 60, (hour + 1) * 60)).ToArray();
            foreach (var row in valid)
            {
                var local = TimeZoneInfo.ConvertTime(row.Start, view.Zone);
                for (int hour = local.Hour; hour < 24 && hour * 60 < local.TimeOfDay.TotalMinutes + (double)row.Hours * 60; hour++)
                {
                    var hours = hourWindows[hour].Hours(row, view.Zone);
                    hourCoverage[hour] += hours;
                    hourTotals[hour] += row.Export!.Value / row.Hours * hours;
                }
            }
            var hoursOfDay = hourTotals.Select((energy, hour) => new { hour, export = hourCoverage[hour] == 0 ? (decimal?)null : energy });
            return new { view.Period, timeZone = view.Zone.Id, view.Revision,
                export = valid.Length == 0 ? null : valid.Sum(r => r.Export),
                captured = valid.Length == 0 ? (decimal?)null :
                    valid.Sum(Captured),
                coverage = valid.Sum(r => r.Hours) / view.Hours,
                dailyExport = view.Aggregates.Any(a => a.Point == "export")
                    ? view.Aggregates.Where(a => a.Point == "export").Sum(a => (decimal?)a.Kwh) : null,
                days, months, seasons, hoursOfDay,
                assumption = "Flexibel regelbare Last ohne Mindestleistung. Export nach bisheriger Speichernutzung." };
        });
        api.MapGet("/data-status", async ([AsParameters] ViewPeriod query, Queries q, Store db, LiveProvider live, SyntheticProvider synthetic, CancellationToken ct) =>
        {
            var view = await Read(q, query, ct);
            var latestMinute = await db.Readings.Where(r => r.PlantId == view.Plant.Id).MaxAsync(r => (DateTimeOffset?)r.End, ct);
            var latestDaily = await db.Aggregates.Where(a => a.PlantId == view.Plant.Id && a.Origin == "provider")
                .MaxAsync(a => (DateTimeOffset?)a.End, ct);
            var latest = new[] { latestMinute, latestDaily }.Max();
            var job = await db.Imports.AsNoTracking().Where(j => j.PlantId == view.Plant.Id)
                .OrderByDescending(j => j.Status == "pending" || j.Status == "running")
                .ThenByDescending(j => j.Cancelled ?? j.LastAttempt ?? j.Start).FirstOrDefaultAsync(ct);
            var progress = job is null ? null : ImportProgress.Describe(job,
                view.Plant.Synthetic ? synthetic.Window : live.Window, DateTimeOffset.UtcNow);
            var lastImport = await db.Imports.Where(j => j.PlantId == view.Plant.Id).MaxAsync(j => j.LastSuccess, ct);
            var lastRepair = await db.HistoryScans.Where(s => s.PlantId == view.Plant.Id)
                .Select(s => s.LastSuccess).SingleOrDefaultAsync(ct);
            var lastLive = await db.LivePolls.Where(p => p.PlantId == view.Plant.Id &&
                (p.Quality == "confirmed" || p.Quality == "repeated")).MaxAsync(p => (DateTimeOffset?)p.FetchedAt, ct);
            var lastSuccess = new[] { lastImport, lastRepair, lastLive }.Max();
            var gaps = new List<object>();
            var cursor = Calendar.Utc(view.Period.Start, view.Zone);
            foreach (var row in view.Rows.Where(r => r.Pv.HasValue && r.Load.HasValue))
            {
                if (row.Start > cursor && gaps.Count < 100) gaps.Add(new { start = cursor, end = row.Start });
                cursor = row.End;
            }
            var end = Calendar.Utc(view.Period.End, view.Zone);
            if (cursor < end && gaps.Count < 100) gaps.Add(new { start = cursor, end });
            var periodStart = Calendar.Utc(view.Period.Start, view.Zone);
            var repair = await db.HistoryGaps.AsNoTracking().Where(g => g.PlantId == view.Plant.Id &&
                g.Start < end && g.End > periodStart)
                .OrderByDescending(g => g.Open).ThenBy(g => g.Start)
                .Select(g => new { g.Start, g.End, g.Kind, g.Attempts, g.Due, g.Open, g.Error }).Take(100).ToArrayAsync(ct);
            var intervalCounts = new { total = view.Rows.Length, pv = view.Rows.Count(r => r.Pv.HasValue),
                load = view.Rows.Count(r => r.Load.HasValue), both = view.Rows.Count(r => r.Pv.HasValue && r.Load.HasValue) };
            return new { report = view.Report(), latest, lastSuccess, job, progress, gaps, repair, intervalCounts, provider = view.Plant.Synthetic ?
                "Synthetische Demodaten" : view.Plant.ProviderId is null ? "Sungrow-Freigabe und Messpunktvalidierung ausstehend" : "Sungrow-Leseimport" };
        });
        api.MapGet("/settings/plant", async (Queries q, Store db, CancellationToken ct) =>
        {
            var plant = await q.Plant(ct);
            var configs = await db.Configurations.AsNoTracking().Where(c => c.PlantId == plant.Id)
                .OrderByDescending(c => c.Since).ToArrayAsync(ct);
            return new { plant.Name, plant.Zone, plant.Region, plant.Synthetic,
                rule = JsonSerializer.Deserialize<EventRule>(plant.Rules),
                configurations = configs.Select(c => new { c.Since, c.PvKwp, c.Origin,
                    battery = JsonSerializer.Deserialize<Battery>(c.Parameters) }) };
        });
        api.MapPut("/settings/plant", async (PlantUpdate input, RuntimeOptions options, Queries q, Store db,
            CancellationToken ct) =>
        {
            if (options.Demo) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100 || input.PvKwp is < 0 or > 1000 ||
                input.Since.Year is < 2000 or > 2100 || input.Zone is null || !input.Zone.Contains('/') || input.Rule is null)
                throw new ArgumentException("Ungültige Anlagenparameter.");
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(input.Zone, out _)) throw new ArgumentException("Ungültige IANA-Zeitzone.");
            input.Battery?.Validate();
            Validate(input.Rule);
            var current = await q.Plant(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT pg_advisory_xact_lock(hashtextextended({current.Id.ToString()}, 0))""", ct);
            var plant = await LockPlant(db, current.Id, ct);
            plant.Name = input.Name.Trim();
            if (plant.ProviderId is not null && plant.Zone != input.Zone &&
                await db.Imports.AnyAsync(j => j.PlantId == plant.Id && (j.Status == "pending" || j.Status == "running"), ct))
                throw new ArgumentException("Die Anlagenzeitzone kann nach Abschluss des laufenden Imports geändert werden.");
            if (plant.ProviderId is not null && plant.Zone != input.Zone &&
                (await db.Readings.AnyAsync(r => r.PlantId == plant.Id, ct) ||
                 await db.LiveMeasurements.AnyAsync(r => r.PlantId == plant.Id, ct) ||
                 await db.Aggregates.AnyAsync(r => r.PlantId == plant.Id, ct)))
                throw new ArgumentException("Eine Änderung der Anlagenzeitzone erfordert eine Datenmigration.");
            plant.Zone = input.Zone;
            plant.Rules = JsonSerializer.Serialize(input.Rule);
            plant.Revision++;
            var existing = await db.Configurations.FindAsync([plant.Id, input.Since], ct);
            var replacement = new Configuration(plant.Id, input.Since, input.PvKwp, "manual", JsonSerializer.Serialize(input.Battery));
            if (existing is null) db.Configurations.Add(replacement);
            else db.Entry(existing).CurrentValues.SetValues(replacement);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });

        api.MapGet("/tariffs", async (Queries q, Store db, CancellationToken ct) =>
        {
            var plant = await q.Plant(ct);
            return (await db.Prices.AsNoTracking().Where(t => t.PlantId == plant.Id).OrderBy(t => t.Kind)
                .ThenBy(t => t.Start).ToArrayAsync(ct)).Select(t => t.Value());
        });
        api.MapGet("/tariffs/{id:guid}", async (Guid id, Queries q, Store db, CancellationToken ct) =>
        {
            var plant = await q.Plant(ct);
            var tariff = await db.Prices.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.PlantId == plant.Id, ct);
            return tariff is null ? Results.NotFound() : Results.Ok(tariff.Value());
        });
        api.MapPost("/tariffs", (TariffInput input, RuntimeOptions opt, Queries q, Store db, CancellationToken ct) =>
            ChangeTariff(null, input, opt, q, db, ct));
        api.MapPut("/tariffs/{id:guid}", (Guid id, TariffInput input, RuntimeOptions opt, Queries q, Store db,
            CancellationToken ct) => ChangeTariff(id, input, opt, q, db, ct));
        api.MapDelete("/tariffs/{id:guid}", (Guid id, RuntimeOptions opt, Queries q, Store db, CancellationToken ct) =>
            ChangeTariff(id, null, opt, q, db, ct));
        api.MapPost("/simulations", async (ScenarioParameters input, Queries q, Jobs jobs, Visitor visitors,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (input.Period is null) throw new ArgumentException("Zeitraum fehlt.");
            Queries.Validate(input.Period);
            if (input.Period.Days > 366) throw new ArgumentException("Simulationen sind auf 366 Tage begrenzt.");
            var plant = await q.Plant(ct);
            var job = jobs.Submit(visitors.Get(ctx, true), plant.Id, input, Revision(plant));
            return job is null ? Results.Problem(statusCode: 429, detail: "Rechenplätze belegt oder eigener Lauf aktiv.") :
                Results.Accepted($"/api/v1/simulations/{job.Id}", new { job.Id });
        }).RequireRateLimiting("simulate");
        api.MapGet("/simulations/{id:guid}", async (Guid id, DateOnly? day, Queries q, Jobs jobs, Visitor visitors,
            HttpContext ctx, CancellationToken ct) =>
        {
            var job = jobs.Find(visitors.Get(ctx, false), id);
            if (job is null) return Results.NotFound();
            var plant = await q.Plant(ct);
            if (plant.Id != job.Plant || job.Revision != Revision(plant))
                return Results.Problem(statusCode: 409, detail: "Datengrundlage geändert. Bitte neu rechnen.");
            var result = job.Result;
            return Results.Ok(new { job.State, job.Progress, job.Error,
                result = result is null ? null : result with { Model = [] },
                model = day is { } d ? result?.Model.GetValueOrDefault(d) : null });
        });
        api.MapDelete("/simulations/{id:guid}", (Guid id, Jobs jobs, Visitor visitors, HttpContext ctx) =>
            jobs.Remove(visitors.Get(ctx, false), id) ? Results.NoContent() : Results.NotFound());
        api.MapGet("/exports/simulations/{id:guid}", async (Guid id, Queries q, Jobs jobs, Visitor visitors,
            HttpContext ctx, CancellationToken ct) =>
        {
            var job = jobs.Find(visitors.Get(ctx, false), id);
            if (job?.Result is not { } result) return Results.NotFound();
            var plant = await q.Plant(ct);
            if (plant.Id != job.Plant || job.Revision != Revision(plant))
                return Results.Problem(statusCode: 409, detail: "Datengrundlage geändert.");
            return Results.File(JsonSerializer.SerializeToUtf8Bytes(result with { Model = [] },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), "application/json", "solar-insight-simulation.json");
        });
        api.MapGet("/exports/overview", async ([AsParameters] ViewPeriod query, Queries q, CancellationToken ct) =>
        {
            var view = await Read(q, query, ct);
            var report = view.Report();
            var csv = new StringBuilder();
            csv.AppendLine("Beginn;Ende_exklusiv;Zeitzone;Revision;PV_kWh;Verbrauch_kWh;Bezug_kWh;Export_kWh;Abdeckung;Netto_EUR");
            csv.AppendLine(FormattableString.Invariant($"{view.Period.Start:yyyy-MM-dd};{view.Period.End:yyyy-MM-dd};{view.Zone.Id};{view.Revision};{report.Metrics.Pv.Kwh};{report.Metrics.Load.Kwh};{report.Metrics.Import.Kwh};{report.Metrics.Export.Kwh};{report.Metrics.Coverage};{report.Cost.Net}"));
            csv.AppendLine();
            bool day = view.Period.Days == 1;
            string unit = day ? "kW" : "kWh";
            csv.AppendLine($"Zeit;PV_{unit};Verbrauch_{unit};Bezug_{unit};Export_{unit};Ladung_{unit};Entladung_{unit};SOC;Netto_EUR");
            foreach (var bucket in Queries.Buckets(view))
            {
                ct.ThrowIfCancellationRequested();
                var item = JsonSerializer.SerializeToElement(bucket);
                csv.AppendLine(string.Join(';', new[] { day ? "start" : "day", "pv", "load", "import",
                    "export", "charge", "discharge", "soc", "cost" }.Select(key =>
                    item.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "")));
            }
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "solar-insight-auswertung.csv");
        });
    }

    static async Task<DataView> Read(Queries queries, ViewPeriod query, CancellationToken ct)
    {
        var plant = await queries.Plant(ct);
        return await queries.Read(plant, await queries.PeriodFor(plant, query, ct), ct);
    }

    static string Revision(Plant plant) => plant.Revision.ToString(CultureInfo.InvariantCulture);

    static void Validate(EventRule rule)
    {
        if (rule.Kw is < .001m or > 100 || rule.SocTolerance is < 0 or > .25m ||
            rule.Minutes is < 1 or > 1440 || rule.HighLoadKw is <= 0 or > 100)
            throw new ArgumentException("Ungültige Ereignisschwellen.");
    }

    internal static async Task<Plant> LockPlant(Store db, Guid id, CancellationToken ct) =>
        (await db.Plants.FromSqlInterpolated($"""SELECT * FROM "Plants" WHERE "Id" = {id} FOR UPDATE""")
            .ToListAsync(ct)).Single();

    static async Task<IResult> ChangeTariff(Guid? id, TariffInput? input, RuntimeOptions options, Queries q,
        Store db, CancellationToken ct)
    {
        if (options.Demo) return Results.StatusCode(403);
        var current = await q.Plant(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var plant = await LockPlant(db, current.Id, ct);
        var tariffs = await db.Prices.Where(t => t.PlantId == plant.Id).ToArrayAsync(ct);
        var previous = tariffs.FirstOrDefault(t => t.Id == id);
        var previousValue = previous?.Value();
        if (id.HasValue && previous is null) return Results.NotFound();
        StoredTariff? next = input is null ? null : new(id ?? Guid.NewGuid(), plant.Id, input.Kind,
            input.Start, input.End, input.Price);
        if (input is not null && (input.Start.Year is < 2000 or > 2100 || input.End?.Year is < 2000 or > 2100))
            throw new ArgumentException("Tarifdatum außerhalb des zulässigen Bereichs.");
        _ = new Tariffs(tariffs.Where(t => t.Id != id).Select(t => t.Value()).Concat(next is null ? [] : [next.Value()]));
        if (next is null) db.Prices.Remove(previous!);
        else if (previous is null) db.Prices.Add(next);
        else db.Entry(previous).CurrentValues.SetValues(next);
        plant.Revision++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { previous = previousValue, current = next?.Value() });
    }
}

