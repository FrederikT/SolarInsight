using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public sealed class ImportWorker(IServiceScopeFactory scopes, SyntheticProvider synthetic, LiveProvider live,
    RuntimeOptions options, ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Store>();
                var plant = await db.Plants.AsNoTracking().SingleOrDefaultAsync(p => options.Demo
                    ? p.Id == Storage.DemoId : p.Active && !p.Synthetic && p.ProviderId != null, stoppingToken);
                if (plant is not null && (options.Demo || live.Configured))
                {
                    var job = await db.Imports.AsNoTracking().Where(j => j.PlantId == plant.Id &&
                        (j.Status == "pending" || j.Status == "running")).OrderBy(j => j.Start)
                        .Select(j => (Guid?)j.Id).FirstOrDefaultAsync(stoppingToken);
                    if (job is { } id)
                    {
                        if (await Importer.Advance(db, id, options.Demo ? synthetic : live, stoppingToken)) continue;
                    }
                    else if (!options.Demo) await ScheduleIncremental(db, plant, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Import fehlgeschlagen. Kategorie {Category}", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    async Task ScheduleIncremental(Store db, Plant plant, CancellationToken ct)
    {
        var last = await db.Imports.AsNoTracking().Where(j => j.PlantId == plant.Id)
            .OrderByDescending(j => j.LastAttempt).FirstOrDefaultAsync(ct);
        if (last?.Status != "completed" || last.LastSuccess >= DateTimeOffset.UtcNow.AddMinutes(-live.RefreshMinutes)) return;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        var latest = await db.Imports.Where(j => j.PlantId == plant.Id && j.Status == "completed")
            .MaxAsync(j => j.End, ct);
        var today = Calendar.Day(DateTimeOffset.UtcNow, zone);
        var day = Calendar.Day(latest, zone);
        var end = Calendar.Utc(today < day.AddDays(14) ? today : day.AddDays(14), zone);
        var start = Calendar.Utc(day.AddDays(-2), zone);
        if (start < end) await ProviderEndpoints.Enqueue(db, plant.Id, start, end, ct);
    }
}

