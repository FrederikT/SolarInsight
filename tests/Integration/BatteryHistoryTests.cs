using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SolarInsight.Api;
using SolarInsight.Application;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;
using Xunit;

namespace SolarInsight.Tests;

public class BatteryHistoryTests
{
    static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    static readonly DateOnly First = new(2024, 7, 15);
    static readonly string Connection = Environment.GetEnvironmentVariable("SOLAR_TEST_DB") ??
        throw new InvalidOperationException("SOLAR_TEST_DB fehlt.");

    /// <summary>Lifetime counters (Wh) at the start of each day; every day adds the same balanced energies.</summary>
    sealed class Counters : IDeviceCounterSource
    {
        public List<DateTimeOffset> Calls { get; } = [];
        static readonly (int Point, decimal Base, decimal PerDay)[] Values =
            [(13134, 12474900, 71600), (13130, 7794600, 15300), (13148, 2867100, 4400),
             (13125, 7276800, 54200), (13034, 2396200, 12500), (13035, 2125600, 6000)];

        public Task<PointProbeResult> ReadDeviceHistory(Plant plant, string device, ProbePoint[] points,
            DateTimeOffset start, DateTimeOffset end, int step, CancellationToken ct)
        {
            Calls.Add(start);
            int day = Calendar.Day(start, Zone).DayNumber - First.DayNumber;
            return Task.FromResult(new PointProbeResult("Gerät", end, Values.Select(v => new PointObservation(v.Point, "", "Wh",
                true, 1, "available", [new ProbeValue(start, v.Base + day * v.PerDay)])).ToArray()));
        }
    }

    static Aggregate Daily(Guid plant, DateOnly day, string point, decimal kwh, string revision = "sungrow-portal-csv-day-v1") =>
        new(plant, point, Calendar.Utc(day, Zone), Calendar.Utc(day.AddDays(1), Zone), kwh, 1, "provider", revision);

    static async Task<BatteryHistory.Status> Finish(BatteryHistory runner)
    {
        for (int i = 0; i < 200 && runner.Running; i++) await Task.Delay(50);
        return runner.Snapshot();
    }

    [Fact]
    public async Task FillsOnlyMissingDailyValuesFromMidnightCountersAndSkipsCompleteDaysOnRerun()
    {
        var services = new ServiceCollection().AddDbContext<Store>(o => o.UseNpgsql(Connection)).BuildServiceProvider();
        await using (var setup = services.CreateScope().ServiceProvider.GetRequiredService<Store>())
        {
            await Importer.Initialize(setup, false, default);
        }
        var plant = new Plant { Id = Guid.NewGuid(), Name = "Battery history fixture", Zone = Zone.Id };
        await using var db = services.CreateScope().ServiceProvider.GetRequiredService<Store>();
        db.Plants.Add(plant);
        await db.SaveChangesAsync();
        // Day 1 has the four CSV channels (one deliberately kept different within tolerance), day 2 has nothing.
        await Storage.UpsertAggregates(db, plant.Id, [Daily(plant.Id, First, "pv", 71.6m), Daily(plant.Id, First, "load", 15.4m),
            Daily(plant.Id, First, "import", 4.4m), Daily(plant.Id, First, "export", 54.2m)], default);

        var source = new Counters();
        var runner = new BatteryHistory(services.GetRequiredService<IServiceScopeFactory>(), source,
            new ConfigurationBuilder().Build(), NullLogger<BatteryHistory>.Instance);
        Assert.NotNull(runner.Start(plant, "42_14_1_1", new(First, First.AddDays(40))));
        Assert.Null(runner.Start(plant, "42_14_1_1", new(First, First.AddDays(1), Apply: false)));
        var preview = await Finish(runner);
        Assert.Equal("completed", preview.State);
        Assert.Equal(2, preview.Accepted);
        Assert.Equal(0, preview.WrittenValues);
        Assert.Equal(4, await db.Aggregates.CountAsync(a => a.PlantId == plant.Id));

        Assert.Null(runner.Start(plant, "42_14_1_1", new(First, First.AddDays(1), Apply: true)));
        var applied = await Finish(runner);
        Assert.Equal("completed", applied.State);
        Assert.Equal(8, applied.WrittenValues);
        Assert.Equal(new BatteryHistory.BatteryTotals(25m, 12m), applied.Totals);
        var stored = await db.Aggregates.AsNoTracking().Where(a => a.PlantId == plant.Id).ToArrayAsync();
        Assert.Equal(12, stored.Length);
        Assert.Equal(15.4m, stored.Single(a => a.Start == Calendar.Utc(First, Zone) && a.Point == "load").Kwh);
        Assert.Equal("sungrow-portal-csv-day-v1", stored.Single(a => a.Start == Calendar.Utc(First, Zone) && a.Point == "pv").Revision);
        Assert.All(stored.Where(a => a.Point is "charge" or "discharge"), a => Assert.Equal(BatteryCounterDays.Revision, a.Revision));
        Assert.Equal(6.0m, stored.Single(a => a.Start == Calendar.Utc(First.AddDays(1), Zone) && a.Point == "discharge").Kwh);

        int calls = source.Calls.Count;
        Assert.Null(runner.Start(plant, "42_14_1_1", new(First, First.AddDays(1), Apply: true)));
        var rerun = await Finish(runner);
        Assert.Equal(0, rerun.Days);
        Assert.Equal(calls, source.Calls.Count);
    }
}
