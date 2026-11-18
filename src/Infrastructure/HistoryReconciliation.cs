using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;

namespace SolarInsight.Infrastructure;

public sealed class HistoryGap
{
    public Guid PlantId { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public string Kind { get; set; } = "minutes";
    public int Attempts { get; set; }
    public DateTimeOffset Due { get; set; }
    public string? Error { get; set; }
    public bool Open { get; set; }
}

public sealed class HistoryScan
{
    public Guid PlantId { get; set; }
    public DateOnly NextDay { get; set; }
    public DateTimeOffset Due { get; set; }
    public DateTimeOffset? LastSuccess { get; set; }
    public DateOnly? CounterDay { get; set; }
    public DateTimeOffset? CounterUntil { get; set; }
    public DateTimeOffset? CounterDue { get; set; }
    public string? CounterData { get; set; }
    public int CounterAttempts { get; set; }
    public string? CounterProfile { get; set; }
}

public static class HistoryReconciliation
{
    public static bool Covers(Reading? row, DateTimeOffset end, SungrowMapping mapping) =>
        row is not null && Covers(row.Value(), end, mapping);

    static bool Covers(Interval row, DateTimeOffset end, SungrowMapping mapping) =>
        row.End == end && (row.SourceRevision == DeviceCounterHistory.Source ||
        row.SourceRevision == "sungrow-minutes-v1" && row.Quality == (mapping.Instantaneous ? "trapezoid" : "mean-power")) &&
        (mapping.Pv is null || row.Pv is >= 0) && (mapping.Load is null || row.Load is >= 0) &&
        (mapping.Grid is null || ValidPair(row.Import, row.Export)) &&
        (mapping.Battery is null || ValidPair(row.Charge, row.Discharge)) &&
        (mapping.Soc is null || row.Soc is >= 0 and <= 1);

    static bool ValidPair(decimal? positive, decimal? negative) =>
        positive is >= 0 && negative is >= 0 && (positive == 0 || negative == 0);

    public static Interval[] Merge(Reading[] existing, Interval[] incoming, SungrowMapping mapping)
    {
        var known = existing.ToDictionary(row => row.Start);
        var result = new List<Interval>();
        foreach (var candidate in incoming)
        {
            if (!known.TryGetValue(candidate.Start, out var reading))
            {
                result.Add(candidate);
                continue;
            }
            var old = reading.Value();
            if (old.End != candidate.End) continue;
            if (old.Quality != candidate.Quality || old.SourceRevision != candidate.SourceRevision)
            {
                if (old.SourceRevision != DeviceCounterHistory.Source && Covers(candidate, old.End, mapping))
                    result.Add(candidate);
                continue;
            }
            bool grid = ValidPair(old.Import, old.Export);
            bool battery = ValidPair(old.Charge, old.Discharge);
            var merged = candidate with
            {
                Pv = old.Pv ?? candidate.Pv,
                Load = old.Load ?? candidate.Load,
                Import = grid ? old.Import : candidate.Import,
                Export = grid ? old.Export : candidate.Export,
                Charge = battery ? old.Charge : candidate.Charge,
                Discharge = battery ? old.Discharge : candidate.Discharge,
                Soc = old.Soc ?? candidate.Soc,
                OpeningSoc = old.OpeningSoc ?? candidate.OpeningSoc
            };
            if (old != merged) result.Add(merged);
        }
        return result.ToArray();
    }

    public static (DateTimeOffset Start, DateTimeOffset End)[] Missing(Reading[] rows, DateTimeOffset start,
        DateTimeOffset end, SungrowMapping mapping, TimeSpan window)
    {
        var known = rows.ToDictionary(r => r.Start);
        var step = TimeSpan.FromMinutes(mapping.StepMinutes);
        var gaps = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        DateTimeOffset? from = null;
        for (var at = start; at < end; at += step)
        {
            var next = at + step;
            if (Covers(known.GetValueOrDefault(at), next, mapping))
            {
                if (from is { } missing) gaps.Add((missing, at));
                from = null;
                continue;
            }
            from ??= at;
            if (next - from < window && next < end) continue;
            gaps.Add((from.Value, next));
            from = null;
        }
        return gaps.ToArray();
    }

    public static Task<Reading[]> Read(Store db, Guid plant, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
        db.Readings.AsNoTracking().Where(r => r.PlantId == plant && r.Start >= start && r.Start < end).ToArrayAsync(ct);

    public static async Task<bool> DailyCovered(Store db, Guid plant, DateTimeOffset start, DateTimeOffset end,
        AggregateMapping mapping, CancellationToken ct)
    {
        var points = await db.Aggregates.AsNoTracking().Where(a => a.PlantId == plant && a.Start == start &&
            a.End == end && a.Origin == "provider" && a.Coverage == 1 && a.Kwh >= 0)
            .Select(a => a.Point).ToArrayAsync(ct);
        return mapping.Fields.All(field => points.Contains(field.Name));
    }
}
