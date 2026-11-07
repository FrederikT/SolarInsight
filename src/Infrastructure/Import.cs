using Microsoft.EntityFrameworkCore;
using SolarInsight.Application;
using SolarInsight.Domain;
using System.Text.Json;

namespace SolarInsight.Infrastructure;

public interface IReadProvider
{
    TimeSpan Window { get; }
    Task<Interval[]> Read(Guid plant, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
}

public interface IAggregateProvider
{
    Task<Aggregate[]> ReadAggregates(Guid plant, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
}

public sealed class SyntheticProvider : IReadProvider
{
    readonly Lazy<Interval[]> data = new(Demo.Generate);
    public TimeSpan Window => TimeSpan.FromDays(14);
    public Task<Interval[]> Read(Guid plant, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        if (plant != Storage.DemoId) throw new InvalidOperationException("Demo-Anlage erforderlich.");
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(data.Value.Where(r => r.Start >= start && r.Start < end).ToArray());
    }
}

public sealed class ProviderFailure(string category, bool retryable, TimeSpan? retryAfter = null) : Exception(category)
{
    public string Category => Message;
    public bool Retryable => retryable;
    public TimeSpan? RetryAfter => retryAfter;
}

public static class Importer
{
    public static async Task<bool> Advance(Store db, Guid jobId, IReadProvider provider, CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        db.Database.SetCommandTimeout(180);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var job = await db.Imports.SingleAsync(j => j.Id == jobId, ct);
        if (job.Checkpoint >= job.End || job.Status == "failed") return false;
        int locked = await db.Database.SqlQuery<int>(
            $"""SELECT CASE WHEN pg_try_advisory_xact_lock(hashtextextended({job.PlantId.ToString()}, 0)) THEN 1 ELSE 0 END AS "Value" """)
            .SingleAsync(ct);
        if (locked == 0) return false;
        await db.Entry(job).ReloadAsync(ct);
        if (job.Checkpoint >= job.End || job.Status == "failed") return false;

        var end = job.Checkpoint + provider.Window;
        if (end > job.End) end = job.End;
        for (int attempt = 0; ; attempt++)
        {
            job.LastAttempt = DateTimeOffset.UtcNow;
            try
            {
                var rows = await provider.Read(job.PlantId, job.Checkpoint, end, ct);
                if (rows.Any(r => r.Start < job.Checkpoint || r.End > end))
                    throw new ProviderFailure("window", false);
                var aggregates = provider is IAggregateProvider source
                    ? await source.ReadAggregates(job.PlantId, job.Checkpoint, end, ct) : [];
                await Storage.Upsert(db, job.PlantId, rows, ct);
                await Storage.UpsertAggregates(db, job.PlantId, aggregates, ct);
                job.LastSuccess = DateTimeOffset.UtcNow;
                job.Checkpoint = end;
                job.Status = end == job.End ? "completed" : "running";
                job.Error = null;
                break;
            }
            catch (Exception ex) when (ex is ProviderFailure or TimeoutException or HttpRequestException)
            {
                var failure = ex as ProviderFailure ?? new ProviderFailure(ex is TimeoutException ? "timeout" : "network", true);
                if (!failure.Retryable || attempt == 2 || failure.RetryAfter > TimeSpan.FromMinutes(2))
                {
                    job.Error = failure.Category;
                    job.Status = "failed";
                    break;
                }
                await delay(failure.RetryAfter ?? TimeSpan.FromMilliseconds(400 * (1 << attempt) +
                    Random.Shared.Next(250)), ct);
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return job.Status == "running";
    }

    public static async Task Initialize(Store db, bool demo, CancellationToken ct)
    {
        await db.Database.MigrateAsync(ct);
        Guid plantId = demo ? Storage.DemoId : Storage.LocalId;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(702928349)", ct);
        var existing = await db.Plants.FindAsync([plantId], ct);
        bool activate = !await db.Plants.AnyAsync(p => p.Active && (demo || !p.Synthetic), ct);
        if (activate) await db.Plants.Where(p => p.Active).ExecuteUpdateAsync(s => s.SetProperty(p => p.Active, false), ct);
        if (existing is null)
        {
            db.Plants.Add(new() { Id = plantId, Name = demo ? "Demonstrationsanlage" : "Meine Anlage",
                Active = activate, Synthetic = demo });
            if (demo)
            {
                db.Configurations.Add(new(plantId, Demo.Start, 12, "synthetic", JsonSerializer.Serialize(Demo.Battery)));
                db.Prices.AddRange(Demo.Prices.Select(t => new StoredTariff(t.Id, plantId, t.Kind, t.Start, t.End, t.Price)));
                var start = Calendar.Utc(Demo.Start, Demo.Zone);
                db.Imports.Add(new() { Id = Guid.NewGuid(), PlantId = plantId, Start = start, Checkpoint = start,
                    End = Calendar.Utc(Demo.End, Demo.Zone) });
            }
        }
        else if (activate) existing.Active = true;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

