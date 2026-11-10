using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;
using System.Text.Json;

namespace SolarInsight.Api;

public record AuthorizationCode(string Code);
public record ImportRequest(DateOnly Start, DateOnly End, TimeOnly? StartTime = null, TimeOnly? EndTime = null);
public record DeviceProbeRequest(string Device);
public record DeviceHistoryProbeRequest(string Device, DateOnly Day, TimeOnly Start, TimeOnly End, int StepMinutes = 15);

public static class ProviderEndpoints
{
    internal static readonly TimeSpan MaxTestWindow = TimeSpan.FromHours(6);
    static readonly ProbePoint[] HybridPoints =
    [
        new(13119, "Hausverbrauch", "power"), new(13149, "Netzbezug", "power"),
        new(13121, "Einspeisung", "power"), new(13126, "Batterieladung", "power"),
        new(13150, "Batterieentladung", "power"), new(13141, "Ladezustand", "soc"),
        new(13003, "DC-Gesamtleistung", "power"),
        new(13011, "Wechselrichter-Wirkleistung (Messort prüfen)", "power")
    ];
    static readonly ProbePoint[] HistoryPoints = [.. HybridPoints, .. DeviceCounterHistory.Points];

    public static void Map(RouteGroupBuilder api)
    {
        var provider = api.MapGroup("/provider").AddEndpointFilter(async (context, next) =>
            context.HttpContext.RequestServices.GetRequiredService<RuntimeOptions>().Demo
                ? Results.StatusCode(403) : await next(context));
        provider.MapGet("/status", async (LiveProvider live, SungrowSetup setup, Store db, CancellationToken ct) =>
        {
            var active = await (from plant in db.Plants
                join connection in db.Connections on plant.Id equals connection.PlantId
                where plant.Active && plant.ProviderId != null && connection.Status == "mapped"
                select new { id = plant.ProviderId, plant.Name, plant.Zone, connection.Capabilities }).SingleOrDefaultAsync(ct);
            return new
            {
                live.Configured,
                defaultZone = setup.DefaultZone,
                authorized = await db.Connections.AnyAsync(c => c.PlantId == Storage.LocalId, ct),
                activeMapping = active is null ? null : new { active.id, active.Name, active.Zone,
                    mapping = JsonSerializer.Deserialize<SungrowMapping>(active.Capabilities) }
            };
        });
        provider.MapPost("/authorize", async (AuthorizationCode input, LiveProvider live, CancellationToken ct) =>
        {
            await live.Authorize(input.Code, ct);
            return Results.NoContent();
        }).RequireRateLimiting("login");
        provider.MapGet("/plants", async (int? page, LiveProvider live, CancellationToken ct) =>
            await live.Plants(page ?? 1, ct)).RequireRateLimiting("simulate");
        provider.MapPost("/probe", async (ProbeRequest input, LiveProvider live, Queries queries, CancellationToken ct) =>
            await live.Probe(await queries.Plant(ct), input, ct)).RequireRateLimiting("login");
        provider.MapPost("/probe/live-candidates", async (DeviceProbeRequest input, LiveProvider live, Queries queries,
            Store db, CancellationToken ct) =>
        {
            var plant = await queries.Plant(ct);
            await CheckProbeDevice(db, live, plant, input.Device, ct);
            return Results.Ok(await live.ProbeLive(plant, input.Device, HybridPoints, ct));
        }).RequireRateLimiting("login");
        provider.MapPost("/probe/device-minutes", async (DeviceHistoryProbeRequest input, LiveProvider live, Queries queries,
            Store db, CancellationToken ct) =>
        {
            var plant = await queries.Plant(ct);
            await CheckProbeDevice(db, live, plant, input.Device, ct);
            return Results.Ok(await live.ProbeDeviceMinutes(plant, input, HistoryPoints, ct));
        }).RequireRateLimiting("login");
        provider.MapPost("/device-counters/backfill", BackfillCounters).RequireRateLimiting("login");
        provider.MapPut("/plant", async (LiveActivation input, LiveProvider live, CancellationToken ct) =>
        {
            await live.Activate(input, ct);
            return Results.NoContent();
        }).RequireRateLimiting("login");

        api.MapDelete("/import-jobs/{id:guid}", async (Guid id, RuntimeOptions options, Queries queries,
            Store db, ImportCancellation cancellations, CancellationToken ct) =>
        {
            if (options.Demo) return Results.StatusCode(403);
            var plant = await queries.Plant(ct);
            if (!await Cancel(db, plant.Id, id, ct)) return Results.NotFound();
            cancellations.Signal(id);
            return Results.NoContent();
        });

        api.MapPost("/import-jobs/{action?}", async (string? action, ImportRequest input, RuntimeOptions options, LiveProvider live,
            Queries queries, Store db, CancellationToken ct) =>
        {
            if (options.Demo) return Results.StatusCode(403);
            var plant = await queries.Plant(ct);
            if (plant.ProviderId is null || !live.Configured)
                return Results.Problem(statusCode: 409, detail: "Autorisierte Sungrow-Anlage mit bestätigten Messpunkten erforderlich.");
            var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
            var today = Calendar.Day(DateTimeOffset.UtcNow, zone);
            if (action is not (null or "estimate")) return Results.NotFound();
            var connection = await db.Connections.SingleAsync(c => c.PlantId == plant.Id, ct);
            var mapping = JsonSerializer.Deserialize<SungrowMapping>(connection.Capabilities)!;
            DateTimeOffset start, end;
            if (input.StartTime is not null || input.EndTime is not null)
            {
                if (input.StartTime is not { } first || input.EndTime is not { } last || input.Start != input.End ||
                    input.Start >= today || last <= first || last - first > MaxTestWindow)
                    throw new ArgumentException("Für den Testimport ein Zeitfenster bis sechs Stunden an einem abgeschlossenen Tag angeben.");
                if (mapping.Points.Length == 0)
                    throw new ArgumentException("Für den Testimport ist mindestens ein Minutenmesspunkt erforderlich.");
                var step = TimeSpan.FromMinutes(mapping.StepMinutes);
                if (first.Ticks % step.Ticks != 0 || last.Ticks % step.Ticks != 0)
                    throw new ArgumentException($"Die Uhrzeiten müssen im {mapping.StepMinutes}-Minuten-Raster liegen.");
                var from = input.Start.ToDateTime(first);
                var to = input.End.ToDateTime(last);
                if (zone.IsInvalidTime(from) || zone.IsInvalidTime(to) || zone.IsAmbiguousTime(from) ||
                    zone.IsAmbiguousTime(to) || zone.GetUtcOffset(from) != zone.GetUtcOffset(to))
                    throw new ArgumentException("Für den Testimport ein Zeitfenster außerhalb der Zeitumstellung wählen.");
                start = TimeZoneInfo.ConvertTimeToUtc(from, zone);
                end = TimeZoneInfo.ConvertTimeToUtc(to, zone);
            }
            else
            {
                var period = Queries.Validate(new(input.Start, input.End));
                if (period.End > today)
                    throw new ArgumentException("Der aktuelle Tag ist über die historische API noch nicht verfügbar.");
                start = Calendar.Utc(period.Start, zone);
                end = Calendar.Utc(period.End, zone);
            }
            if (action == "estimate")
            {
                var windows = ImportProgress.Windows(start, end, live.Window);
                int calls = mapping.Points.Length > 0 ? windows : 0;
                if (mapping.Daily?.Points.Length > 0 && input.StartTime is null)
                    calls += (int)Math.Ceiling((input.End.DayNumber - input.Start.DayNumber) / 100d);
                return Results.Ok(new { windows, seconds = (long)calls * live.CallSpacingSeconds });
            }
            var job = await Enqueue(db, plant.Id, start, end, ct);
            return job is null ? Results.Problem(statusCode: 409, detail: "Ein Import ist bereits aktiv.") :
                Results.Accepted("/api/v1/data-status?kind=month", new { job.Id });
        });
    }

    static async Task CheckProbeDevice(Store db, LiveProvider live, Plant plant, string device, CancellationToken ct)
    {
        if (plant.ProviderId is null || !live.Configured ||
            !await db.Connections.AnyAsync(c => c.PlantId == plant.Id && c.Status == "mapped", ct) ||
            !await db.Devices.AnyAsync(d => d.PlantId == plant.Id && d.ProviderId == device, ct))
            throw new ArgumentException("Ein Gerät der autorisierten Sungrow-Anlage auswählen.");
        if (!SupportsDeviceProbe(device))
            throw new ArgumentException("Für diesen Gerätetyp ist kein Kandidatenkatalog hinterlegt.");
    }

    internal static bool SupportsDeviceProbe(string device) => device.Split('_').ElementAtOrDefault(1) == "14";

    static async Task<IResult> BackfillCounters(DeviceHistoryProbeRequest input, bool? apply, LiveProvider live,
        Queries queries, Store db, CancellationToken ct)
    {
        var plant = await queries.Plant(ct);
        await CheckProbeDevice(db, live, plant, input.Device, ct);
        var metadata = await db.Connections.AsNoTracking().SingleAsync(c => c.PlantId == plant.Id, ct);
        var mapping = JsonSerializer.Deserialize<SungrowMapping>(metadata.Capabilities);
        if (mapping is not { DeviceCounters: true, Live: { Confirmed: true } configured } ||
            configured.DeviceKey != input.Device || mapping.StepMinutes != input.StepMinutes)
            return Results.Problem(statusCode: 409, detail: "Ein bestätigtes Gerätezählerprofil mit passendem Raster ist erforderlich.");

        var report = await live.ProbeDeviceMinutes(plant, input, HistoryPoints, ct);
        if (report.Failure is not null) return Results.Ok(new { report.Failure, report.HttpStatus, report.ResultCode });
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        var (start, end) = LiveProvider.ProbeWindow(input.Day, input.Start, input.End, zone, input.StepMinutes);
        var candidates = DeviceCounterHistory.Intervals(report, start, end, TimeSpan.FromMinutes(input.StepMinutes));
        var totals = new Dictionary<string, decimal>
        {
            ["pv"] = candidates.Sum(r => r.Pv ?? 0), ["load"] = candidates.Sum(r => r.Load ?? 0),
            ["import"] = candidates.Sum(r => r.Import ?? 0), ["export"] = candidates.Sum(r => r.Export ?? 0),
            ["charge"] = candidates.Sum(r => r.Charge ?? 0), ["discharge"] = candidates.Sum(r => r.Discharge ?? 0)
        };
        var day = Calendar.Utc(input.Day, zone);
        var daily = await db.Aggregates.AsNoTracking().Where(a => a.PlantId == plant.Id && a.Start == day &&
            a.Origin == "provider" && a.Coverage == 1).ToDictionaryAsync(a => a.Point, a => a.Kwh, ct);
        var inconsistent = totals.Where(pair => !daily.TryGetValue(pair.Key, out var value) || pair.Value > value + .1m)
            .Select(pair => pair.Key).ToArray();
        if (apply != true || inconsistent.Length > 0 || candidates.Length == 0)
            return Results.Ok(new { candidates, totals, inconsistent, written = 0 });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT pg_advisory_xact_lock(hashtextextended({plant.Id.ToString()}, 0))""", ct);
        if (!await db.Connections.AnyAsync(c => c.PlantId == plant.Id && c.Capabilities == metadata.Capabilities, ct) ||
            await db.Imports.AnyAsync(j => j.PlantId == plant.Id && (j.Status == "pending" || j.Status == "running"), ct))
            return Results.Problem(statusCode: 409, detail: "Profil geändert oder regulärer Import aktiv.");
        var existing = await db.Readings.AsNoTracking().Where(r => r.PlantId == plant.Id && r.Start < end && r.End > start)
            .ToArrayAsync(ct);
        var rows = DeviceCounterHistory.Merge(existing, candidates);
        var written = await Storage.Upsert(db, plant.Id, rows, ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { candidates, totals, inconsistent, written });
    }

    public static async Task<bool> Cancel(Store db, Guid plant, Guid id, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Imports.Where(j => j.Id == id && j.PlantId == plant &&
                (j.Status == "pending" || j.Status == "running"))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "cancelled")
                .SetProperty(j => j.Cancelled, now), ct) == 1;
    }

    public static async Task<ImportJob?> Enqueue(Store db, Guid plant, DateTimeOffset start, DateTimeOffset end,
        CancellationToken ct)
    {
        if (await db.Imports.AnyAsync(j => j.PlantId == plant && (j.Status == "pending" || j.Status == "running"), ct))
            return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT pg_advisory_xact_lock(hashtextextended({plant.ToString()}, 0))""", ct);
        if (await db.Imports.AnyAsync(j => j.PlantId == plant && (j.Status == "pending" || j.Status == "running"), ct))
            return null;
        var job = new ImportJob { Id = Guid.NewGuid(), PlantId = plant, Start = start, End = end, Checkpoint = start };
        db.Imports.Add(job);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return job;
    }
}

public record ProbeRequest(DateOnly Day, TimeOnly? Start = null, TimeOnly? End = null);

