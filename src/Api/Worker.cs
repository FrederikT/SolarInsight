using Microsoft.EntityFrameworkCore;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public sealed class ImportWorker(IServiceScopeFactory scopes, SyntheticProvider synthetic, LiveProvider live,
    RuntimeOptions options, ImportCancellation cancellations, HistoryWorker history, ILogger<ImportWorker> logger) : BackgroundService
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
                        if (await cancellations.Run(id, ct => Importer.Advance(db, id, options.Demo ? synthetic : live, ct), stoppingToken)) continue;
                    }
                    else if (!options.Demo) await history.Tick(db, plant, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Import fehlgeschlagen. Kategorie {Category}", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
