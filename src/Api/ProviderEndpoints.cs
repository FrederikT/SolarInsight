using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public record AuthorizationCode(string Code);
public record ImportRequest(DateOnly Start, DateOnly End);

public static class ProviderEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var provider = api.MapGroup("/provider").AddEndpointFilter(async (context, next) =>
            context.HttpContext.RequestServices.GetRequiredService<RuntimeOptions>().Demo
                ? Results.StatusCode(403) : await next(context));
        provider.MapGet("/status", async (LiveProvider live, Store db, CancellationToken ct) =>
            new { live.Configured, authorized = await db.Connections.AnyAsync(c => c.PlantId == Storage.LocalId, ct) });
        provider.MapPost("/authorize", async (AuthorizationCode input, LiveProvider live, CancellationToken ct) =>
        {
            await live.Authorize(input.Code, ct);
            return Results.NoContent();
        }).RequireRateLimiting("login");
        provider.MapGet("/plants", async (int? page, LiveProvider live, CancellationToken ct) =>
            await live.Plants(page ?? 1, ct)).RequireRateLimiting("simulate");
        provider.MapPut("/plant", async (LiveActivation input, LiveProvider live, CancellationToken ct) =>
        {
            await live.Activate(input, ct);
            return Results.NoContent();
        }).RequireRateLimiting("login");

        api.MapPost("/import-jobs", async (ImportRequest input, RuntimeOptions options, LiveProvider live,
            Queries queries, Store db, CancellationToken ct) =>
        {
            if (options.Demo) return Results.StatusCode(403);
            var plant = await queries.Plant(ct);
            if (plant.ProviderId is null || !live.Configured)
                return Results.Problem(statusCode: 409, detail: "Autorisierte Sungrow-Anlage mit bestätigten Messpunkten erforderlich.");
            var period = Queries.Validate(new(input.Start, input.End));
            var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
            if (period.End > Calendar.Day(DateTimeOffset.UtcNow, zone))
                throw new ArgumentException("Der aktuelle Tag ist über die historische API noch nicht verfügbar.");
            var job = await Enqueue(db, plant.Id, Calendar.Utc(period.Start, zone), Calendar.Utc(period.End, zone), ct);
            return job is null ? Results.Problem(statusCode: 409, detail: "Ein Import ist bereits aktiv.") :
                Results.Accepted("/api/v1/data-status?kind=month", new { job.Id });
        });
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

