using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SolarInsight.Infrastructure;

public static class LiveStorage
{
    public static async Task<LiveMeasurement[]> At(Store db, Guid plant, DateTimeOffset? measured, CancellationToken ct) =>
        measured is null ? [] : await db.LiveMeasurements.AsNoTracking().Where(m =>
            m.PlantId == plant && m.MeasuredAt == measured && m.Source == LiveNormalization.Source).ToArrayAsync(ct);

    public static async Task<LivePoll?> Save(Store db, Guid plant, IEnumerable<string> channels, LiveBatch batch,
        DateTimeOffset fetched, TimeSpan cadence, string? failure, DateTimeOffset? retryAt, CancellationToken ct, LiveMapping? expectedMapping = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT pg_advisory_xact_lock(hashtextextended({plant.ToString()}, 1))""", ct);
        if (expectedMapping is not null)
        {
            var capabilities = await db.Connections.AsNoTracking().Where(c => c.PlantId == plant)
                .Select(c => c.Capabilities).SingleOrDefaultAsync(ct);
            if (capabilities is null || JsonSerializer.Deserialize<SungrowMapping>(capabilities)?.Live != expectedMapping)
                return null;
        }
        var previous = await db.LivePolls.AsNoTracking().Where(p => p.PlantId == plant)
            .OrderByDescending(p => p.FetchedAt).FirstOrDefaultAsync(ct);
        var latest = await db.LiveMeasurements.Where(m => m.PlantId == plant)
            .MaxAsync(m => (DateTimeOffset?)m.MeasuredAt, ct);
        bool old = latest is not null && batch.MeasuredAt <= latest;
        bool regressed = latest is { } last && batch.MeasuredAt < last;
        var poll = new LivePoll(plant, fetched, regressed ? latest : batch.MeasuredAt ?? latest,
            failure ?? (batch.Values.Count == 0 ? "missing" : regressed ? "regressed" : old ? "repeated" : "confirmed"),
            string.Join(',', batch.Values.Keys), retryAt);
        db.LivePolls.Add(poll);
        if (batch.MeasuredAt is { } at && batch.Values.Count > 0 && (latest is null || at >= latest))
        {
            var json = JsonSerializer.Serialize(batch.Values.Select(pair =>
                new LiveMeasurement(plant, pair.Key, LiveNormalization.Source, at, fetched, pair.Value, "confirmed")));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "LiveMeasurements"
                SELECT * FROM jsonb_populate_recordset(NULL::"LiveMeasurements", {json}::jsonb)
                ON CONFLICT ("PlantId", "MeasuredAt", "Channel", "Source") DO NOTHING
                """, ct);
        }
        if (previous is not null && previous.FetchedAt < fetched)
        {
            var start = previous.FetchedAt;
            foreach (var channel in channels)
            {
                string? reason = failure ?? (batch.MeasuredAt is null || !batch.Values.ContainsKey(channel) ? "missing-channel" :
                    old ? "repeated-timestamp" : fetched - start > cadence + TimeSpan.FromMinutes(1) ? "poll-outage" : null);
                if (reason is not null) db.LiveGaps.Add(new(plant, channel, start, fetched, reason));
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return poll;
    }
}
