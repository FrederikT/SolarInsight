using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using System.Text.Json;

namespace SolarInsight.Infrastructure;

public sealed class Plant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? ProviderId { get; set; }
    public string Zone { get; set; } = "Europe/Berlin";
    public string Region { get; set; } = "EU";
    public bool Active { get; set; }
    public bool Synthetic { get; set; }
    public long Revision { get; set; }
    public string Rules { get; set; } = "{}";
}
public sealed record Configuration(Guid PlantId, DateOnly Since, decimal PvKwp, string Origin, string Parameters);
public sealed record Device(Guid PlantId, string ProviderId, string Model, string Type);
public sealed record ProviderConnection(Guid PlantId, string Status, string ProtectedSettings, string Capabilities);
public sealed record Aggregate(Guid PlantId, string Point, DateTimeOffset Start, DateTimeOffset End,
    decimal Kwh, decimal Coverage, string Origin, string Revision);
public sealed record StoredTariff(Guid Id, Guid PlantId, TariffKind Kind, DateOnly Start, DateOnly? End, decimal Price)
{
    public Tariff Value() => new(Id, Kind, Start, End, Price);
}
public sealed class ImportJob
{
    public Guid Id { get; set; }
    public Guid PlantId { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public DateTimeOffset Checkpoint { get; set; }
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Cancelled { get; set; }
    public int Windows { get; set; }
    public int EmptyWindows { get; set; }
    public long ReadingRows { get; set; }
    public long AggregateRows { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
    public DateTimeOffset? LastSuccess { get; set; }
    public bool AutomaticInitial { get; set; }
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
}

public sealed record Reading(Guid PlantId, DateTimeOffset Start, DateTimeOffset End, decimal? Pv, decimal? Load,
    decimal? Import, decimal? Export, decimal? Charge, decimal? Discharge, decimal? Soc, decimal? OpeningSoc,
    string Quality, string SourceRevision)
{
    public Interval Value() => new(Start, End, Pv, Load, Import, Export, Charge, Discharge, Soc, OpeningSoc, Quality, SourceRevision);
}

public sealed class Store(DbContextOptions<Store> options) : DbContext(options)
{
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<StoredTariff> Prices => Set<StoredTariff>();
    public DbSet<Configuration> Configurations => Set<Configuration>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<ProviderConnection> Connections => Set<ProviderConnection>();
    public DbSet<Aggregate> Aggregates => Set<Aggregate>();
    public DbSet<ImportJob> Imports => Set<ImportJob>();
    public DbSet<LiveMeasurement> LiveMeasurements => Set<LiveMeasurement>();
    public DbSet<LivePoll> LivePolls => Set<LivePoll>();
    public DbSet<LiveGap> LiveGaps => Set<LiveGap>();
    public DbSet<HistoryGap> HistoryGaps => Set<HistoryGap>();
    public DbSet<HistoryScan> HistoryScans => Set<HistoryScan>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<LiveMeasurement>().HasKey(m => new { m.PlantId, m.MeasuredAt, m.Channel, m.Source });
        builder.Entity<LivePoll>().HasKey(p => new { p.PlantId, p.FetchedAt });
        builder.Entity<LiveGap>().HasKey(g => new { g.PlantId, g.Channel, g.Start });
        builder.Entity<HistoryGap>().HasKey(g => new { g.PlantId, g.Start, g.Kind });
        builder.Entity<HistoryGap>().HasIndex(g => new { g.PlantId, g.Open, g.Due });
        builder.Entity<HistoryScan>().HasKey(s => s.PlantId);
        builder.Entity<Reading>().HasKey(r => new { r.PlantId, r.Start });
        builder.Entity<StoredTariff>().HasIndex(t => new { t.PlantId, t.Kind, t.Start });
        builder.Entity<Plant>().HasIndex(p => p.Active).IsUnique().HasFilter("\"Active\" = TRUE");
        builder.Entity<Configuration>().HasKey(x => new { x.PlantId, x.Since });
        builder.Entity<Device>().HasKey(x => new { x.PlantId, x.ProviderId });
        builder.Entity<ProviderConnection>().HasKey(x => x.PlantId);
        builder.Entity<Aggregate>().HasKey(x => new { x.PlantId, x.Point, x.Start, x.Origin });
        foreach (var type in new[] { typeof(Reading), typeof(StoredTariff), typeof(Configuration), typeof(Device),
            typeof(ProviderConnection), typeof(Aggregate), typeof(ImportJob), typeof(LiveMeasurement),
            typeof(LivePoll), typeof(LiveGap), typeof(HistoryGap), typeof(HistoryScan) })
            builder.Entity(type).HasOne(typeof(Plant)).WithMany().HasForeignKey("PlantId");
    }
}

public static class Storage
{
    public static readonly Guid DemoId = Guid.Parse("92b5a13b-6607-4182-a240-65092f605bc3");
    public static readonly Guid LocalId = Guid.Parse("6ad1526e-319f-4dc7-89bd-f30101e26254");

    public static async Task<int> UpsertAggregates(Store db, Guid plant, Aggregate[] rows, CancellationToken ct, bool fillOnly = false)
    {
        if (rows.Length == 0) return 0;
        if (rows.Any(r => r.PlantId != plant || r.Origin != "provider" || r.Kwh < 0 || r.End <= r.Start))
            throw new ArgumentException("Ungültiges Anlagenaggregat.");
        string json = JsonSerializer.Serialize(rows);
        int changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Aggregates" SELECT * FROM jsonb_populate_recordset(NULL::"Aggregates", {json}::jsonb)
            ON CONFLICT ("PlantId", "Point", "Start", "Origin") DO UPDATE SET
              "End" = excluded."End", "Kwh" = excluded."Kwh", "Coverage" = excluded."Coverage",
              "Revision" = excluded."Revision"
            WHERE "Aggregates" IS DISTINCT FROM excluded AND (NOT {fillOnly} OR
              "Aggregates"."Coverage" < 1 OR "Aggregates"."End" <> excluded."End")
            """, ct);
        if (changed > 0)
            await db.Plants.Where(p => p.Id == plant).ExecuteUpdateAsync(s => s.SetProperty(p => p.Revision,
                p => p.Revision + 1), ct);
        return changed;
    }

    public static async Task<int> Upsert(Store db, Guid plant, Interval[] input, CancellationToken ct)
    {
        if (input.Length == 0) return 0;
        if (input.Any(r => r.Start.Offset != TimeSpan.Zero || r.End.Offset != TimeSpan.Zero || r.End <= r.Start ||
            new[] { r.Pv, r.Load, r.Import, r.Export, r.Charge, r.Discharge }.Any(x => x < 0) ||
            r.Soc is < 0 or > 1 || r.OpeningSoc is < 0 or > 1))
            throw new ArgumentException("Ungültige normalisierte Daten.");
        // One row per UTC interval, with independently nullable canonical channels.
        string json = JsonSerializer.Serialize(input.Select(r => new Reading(plant, r.Start, r.End, r.Pv, r.Load,
            r.Import, r.Export, r.Charge, r.Discharge, r.Soc, r.OpeningSoc, r.Quality, r.SourceRevision))
            .GroupBy(r => r.Start).Select(g => g.Last()));
        int changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Readings" SELECT * FROM jsonb_populate_recordset(NULL::"Readings", {json}::jsonb)
            ON CONFLICT ("PlantId", "Start") DO UPDATE SET
              "End" = excluded."End", "Pv" = excluded."Pv", "Load" = excluded."Load",
              "Import" = excluded."Import", "Export" = excluded."Export", "Charge" = excluded."Charge",
              "Discharge" = excluded."Discharge", "Soc" = excluded."Soc", "OpeningSoc" = excluded."OpeningSoc",
              "Quality" = excluded."Quality", "SourceRevision" = excluded."SourceRevision"
            WHERE "Readings" IS DISTINCT FROM excluded
              AND ("Readings"."SourceRevision" <> {DeviceCounterHistory.Source} OR
                   excluded."SourceRevision" = {DeviceCounterHistory.Source})
            """, ct);
        if (changed > 0)
        {
            await db.Plants.Where(p => p.Id == plant).ExecuteUpdateAsync(s => s.SetProperty(p => p.Revision,
                p => p.Revision + 1), ct);
            var start = input.Min(r => r.Start);
            var end = input.Max(r => r.End);
            await db.Aggregates.Where(a => a.PlantId == plant && a.Origin == "integrated" &&
                a.Start < end && a.End > start).ExecuteDeleteAsync(ct);
        }
        return changed;
    }
}

