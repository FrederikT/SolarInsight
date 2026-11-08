using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;
using System.Text.Json;

namespace SolarInsight.Api;

public record AuthorizationCode(string Code);
public record ImportRequest(DateOnly Start, DateOnly End, TimeOnly? StartTime = null, TimeOnly? EndTime = null);

public static class ProviderEndpoints
{
    internal static readonly TimeSpan MaxTestWindow = TimeSpan.FromHours(6);

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
            await live.Probe(await queries.Plant(ct), input.Day, ct)).RequireRateLimiting("login");
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

public record ProbeRequest(DateOnly Day);

