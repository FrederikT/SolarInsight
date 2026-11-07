using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;
using Xunit;

namespace SolarInsight.Tests;

public class PostgresTests
{
    static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    static Store Open() => new(new DbContextOptionsBuilder<Store>().UseNpgsql(
        Environment.GetEnvironmentVariable("SOLAR_TEST_DB") ??
        throw new InvalidOperationException("SOLAR_TEST_DB muss auf eine isolierte PostgreSQL-Testdatenbank zeigen.")).Options);

    [Fact]
    public async Task A02_UpsertIsIdempotentAndCorrectsRevision()
    {
        using var db = Open();
        await Importer.Initialize(db, false, default);
        await using var tx = await db.Database.BeginTransactionAsync();
        var plant = new Plant { Id = Guid.NewGuid(), Name = "Fixture" };
        db.Plants.Add(plant);
        await db.SaveChangesAsync();
        Interval row = new(Start, Start.AddHours(1), 4, 2);
        await Storage.Upsert(db, plant.Id, [row, row], default);
        await Storage.Upsert(db, plant.Id, [row], default);
        Assert.Equal(4, await db.Readings.Where(r => r.PlantId == plant.Id).SumAsync(r => r.Pv));
        Assert.Equal(1, await db.Plants.Where(p => p.Id == plant.Id).Select(p => p.Revision).SingleAsync());
        db.Aggregates.Add(new(plant.Id, "pv", Start, row.End, 4, 1, "integrated", "1"));
        await db.SaveChangesAsync();
        await Storage.Upsert(db, plant.Id, [row with { Pv = 6 }], default);
        Assert.Equal(6, await db.Readings.Where(r => r.PlantId == plant.Id).SumAsync(r => r.Pv));
        Assert.Equal(2, await db.Plants.Where(p => p.Id == plant.Id).Select(p => p.Revision).SingleAsync());
        Assert.False(await db.Aggregates.AnyAsync(a => a.PlantId == plant.Id));
    }

    [Fact]
    public async Task A03_BackfillCheckpointRateLimitAndAuthFailure()
    {
        var plant = Guid.NewGuid();
        var id = Guid.NewGuid();
        using (var db = Open())
        {
            await Importer.Initialize(db, false, default);
            db.Plants.Add(new() { Id = plant, Name = "Importfixture" });
            db.Imports.Add(new() { Id = id, PlantId = plant, Start = Start, End = Start.AddHours(2), Checkpoint = Start });
            await db.SaveChangesAsync();
            var retry = new Queue<Exception>([new ProviderFailure("rate-limit", true, TimeSpan.FromSeconds(7)), new TimeoutException()]);
            var delays = new List<TimeSpan>();
            Assert.True(await Importer.Advance(db, id, new Fixture(retry), default, (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            }));
            Assert.Equal(2, delays.Count);
            Assert.Equal(TimeSpan.FromSeconds(7), delays[0]);
        }
        using (var restarted = Open())
        {
            var fixture = new Fixture(new([new ProviderFailure("auth", false)]));
            Assert.False(await Importer.Advance(restarted, id, fixture, default));
            Assert.Equal(new[] { Start.AddHours(1) }, fixture.Calls);
            var job = await restarted.Imports.SingleAsync(j => j.Id == id);
            Assert.Equal("auth", job.Error);
            Assert.Equal("failed", job.Status);
            Assert.Equal(Start.AddHours(1), job.Checkpoint);
            Assert.Equal(1, await restarted.Readings.CountAsync(r => r.PlantId == plant));
        }
    }

    [Fact]
    public async Task A03_CancelledWindowRollsBackAndRepeatedTimeoutStops()
    {
        var id = Guid.NewGuid();
        var plant = Guid.NewGuid();
        using (var db = Open())
        {
            await Importer.Initialize(db, false, default);
            db.Plants.Add(new() { Id = plant, Name = "Timeoutfixture" });
            db.Imports.Add(new() { Id = id, PlantId = plant, Start = Start, End = Start.AddHours(1), Checkpoint = Start });
            await db.SaveChangesAsync();
            var interrupted = new Fixture(new([new OperationCanceledException()]));
            await Assert.ThrowsAsync<OperationCanceledException>(() => Importer.Advance(db, id, interrupted, default));
        }
        using (var restarted = Open())
        {
            Assert.Equal(Start, await restarted.Imports.Where(j => j.Id == id).Select(j => j.Checkpoint).SingleAsync());
            Assert.False(await restarted.Readings.AnyAsync(r => r.PlantId == plant));
            var timeouts = new Fixture(new(Enumerable.Repeat<Exception>(new TimeoutException(), 4)));
            Assert.False(await Importer.Advance(restarted, id, timeouts, default, (_, _) => Task.CompletedTask));
            Assert.Equal(3, timeouts.Calls.Count);
            Assert.Equal("timeout", await restarted.Imports.Where(j => j.Id == id).Select(j => j.Error).SingleAsync());
        }
    }

    sealed class Fixture(Queue<Exception> failures) : IReadProvider
    {
        public List<DateTimeOffset> Calls { get; } = [];
        public TimeSpan Window => TimeSpan.FromHours(1);
        public Task<Interval[]> Read(Guid plant, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
        {
            Calls.Add(start);
            if (failures.TryDequeue(out var failure)) throw failure;
            return Task.FromResult<Interval[]>([new(start, end, 4, 2)]);
        }
    }
}

