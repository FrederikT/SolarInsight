using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public interface IHistorySource : IReadProvider, IAggregateProvider
{
    int RefreshMinutes { get; }
}

public sealed class HistoryWorker(IHistorySource provider, IConfiguration configuration)
{
    readonly int retryHours = Math.Clamp(configuration.GetValue("Sungrow:HistoryRetryHours", 24), 24, 48);
    const int MaxAttempts = 3;

    public async Task Tick(Store db, Plant plant, CancellationToken ct)
    {
        var initial = await db.Imports.AsNoTracking().Where(j => j.PlantId == plant.Id && j.Status == "completed" &&
            j.End - j.Start > ProviderEndpoints.MaxTestWindow).OrderBy(j => j.Start).FirstOrDefaultAsync(ct);
        if (initial is null) return;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        var now = DateTimeOffset.UtcNow;
        var today = Calendar.Day(now, zone);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Database.SqlQuery<int>(
            $"""SELECT CASE WHEN pg_try_advisory_xact_lock(hashtextextended({plant.Id.ToString()}, 0)) THEN 1 ELSE 0 END AS "Value" """)
            .SingleAsync(ct);
        if (locked == 0) return;
        if (await db.Imports.AnyAsync(j => j.PlantId == plant.Id && (j.Status == "pending" || j.Status == "running"), ct))
            return;

        var metadata = await db.Connections.AsNoTracking().SingleAsync(c => c.PlantId == plant.Id, ct);
        var mapping = JsonSerializer.Deserialize<SungrowMapping>(metadata.Capabilities)!;
        var scan = await db.HistoryScans.FindAsync([plant.Id], ct);
        if (scan is null)
        {
            scan = new() { PlantId = plant.Id, NextDay = Calendar.Day(initial.Start, zone), Due = now };
            db.HistoryScans.Add(scan);
        }
        if (scan.Due <= now)
        {
            // A long backlog advances in small transactions and survives process restarts.
            for (int count = 0; count < 3 && scan.NextDay < today; count++, scan.NextDay = scan.NextDay.AddDays(1))
                await QueueDay(db, plant.Id, scan.NextDay, zone, mapping, now, ct);
            scan.Due = now.AddMinutes(scan.NextDay < today ? 1 : provider.RefreshMinutes);
        }
        var gap = await db.HistoryGaps.Where(g => g.PlantId == plant.Id && g.Due <= now)
            .OrderBy(g => g.Open).ThenByDescending(g => g.Start).FirstOrDefaultAsync(ct);
        if (gap is not null)
        {
            try { await Repair(db, gap, mapping, ct); }
            catch (Exception ex) when (ex is ProviderFailure or HttpRequestException or TimeoutException)
            {
                gap.Error = ex is ProviderFailure failure ? failure.Category : ex is TimeoutException ? "timeout" : "network";
                Retry(gap, now, (ex as ProviderFailure)?.RetryAfter);
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    async Task QueueDay(Store db, Guid plant, DateOnly day, TimeZoneInfo zone, SungrowMapping mapping, DateTimeOffset now,
        CancellationToken ct)
    {
        var start = Calendar.Utc(day, zone);
        var end = Calendar.Utc(day.AddDays(1), zone);
        if (mapping.Points.Length > 0)
        {
            var rows = await HistoryReconciliation.Read(db, plant, start, end, ct);
            foreach (var (from, to) in HistoryReconciliation.Missing(rows, start, end, mapping, provider.Window))
                db.HistoryGaps.Add(new() { PlantId = plant, Start = from, End = to, Due = now });
        }
        if (mapping.Daily is { Points.Length: > 0 } daily &&
            !await HistoryReconciliation.DailyCovered(db, plant, start, end, daily, ct))
            db.HistoryGaps.Add(new() { PlantId = plant, Start = start, End = end, Kind = "daily", Due = now });
    }

    async Task Repair(Store db, HistoryGap gap, SungrowMapping mapping, CancellationToken ct)
    {
        if (gap.Kind == "daily")
        {
            var daily = mapping.Daily!;
            if (!gap.Open && !await HistoryReconciliation.DailyCovered(db, gap.PlantId, gap.Start, gap.End, daily, ct))
                await Storage.UpsertAggregates(db, gap.PlantId,
                    await provider.ReadAggregates(gap.PlantId, gap.Start, gap.End, ct), ct, fillOnly: true);
            if (await HistoryReconciliation.DailyCovered(db, gap.PlantId, gap.Start, gap.End, daily, ct))
            {
                db.HistoryGaps.Remove(gap);
                return;
            }
        }
        else
        {
            var rows = await HistoryReconciliation.Read(db, gap.PlantId, gap.Start, gap.End, ct);
            var missing = HistoryReconciliation.Missing(rows, gap.Start, gap.End, mapping, provider.Window);
            foreach (var (from, to) in gap.Open ? [] : missing)
            {
                var incoming = await provider.Read(gap.PlantId, from, to, ct);
                if (incoming.Any(r => r.Start < from || r.End > to)) throw new ProviderFailure("window", false);
                await Storage.Upsert(db, gap.PlantId, HistoryReconciliation.Merge(rows, incoming, mapping), ct);
            }
            if (missing.Length == 0 || HistoryReconciliation.Missing(
                await HistoryReconciliation.Read(db, gap.PlantId, gap.Start, gap.End, ct),
                gap.Start, gap.End, mapping, provider.Window).Length == 0)
            {
                db.HistoryGaps.Remove(gap);
                return;
            }
        }
        gap.Error = "incomplete-history";
        if (gap.Open) gap.Due = DateTimeOffset.UtcNow.AddDays(1);
        else Retry(gap, DateTimeOffset.UtcNow, null);
    }

    void Retry(HistoryGap gap, DateTimeOffset now, TimeSpan? providerDelay)
    {
        gap.Open = ++gap.Attempts >= MaxAttempts;
        var delay = TimeSpan.FromHours(retryHours);
        gap.Due = now + (providerDelay > delay ? providerDelay.Value : delay);
    }
}
