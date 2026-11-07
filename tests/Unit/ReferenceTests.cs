using SolarInsight.Application;
using SolarInsight.Domain;
using Xunit;

namespace SolarInsight.Tests;

public class ReferenceTests
{
    static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    static readonly Battery None = new(0, 0, 0, 1, 1);
    static readonly Battery Four = new(4, 2, 3, .9m, .9m);
    static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    static readonly Period Day = new(new(2026, 1, 1), new(2026, 1, 2));
    static Interval At(int hour, decimal? pv, decimal? load, decimal? import = null, decimal? export = null) =>
        new(Noon.AddHours(hour), Noon.AddHours(hour + 1), pv, load, import, export);
    static Tariff Price(TariffKind kind, decimal value, DateOnly? start = null, DateOnly? end = null) =>
        new(Guid.NewGuid(), kind, start ?? Day.Start, end, value);

    [Fact]
    public void A04_PowerSemantics()
    {
        var start = new Sample(Noon, 4);
        var end = new Sample(Noon.AddMinutes(15), 2);
        var step = TimeSpan.FromMinutes(15);
        Assert.Equal(1, Normalization.Energy(Semantics.MeanPower, start, end, step).Kwh);
        Assert.Equal(.75m, Normalization.Energy(Semantics.InstantaneousPower, start, end, step).Kwh);
        Assert.Null(Normalization.Energy(Semantics.InstantaneousPower, start, end with { At = Noon.AddHours(1) }, step).Kwh);
        Assert.Equal("reset", Normalization.Energy(Semantics.Counter, start, end, step).Quality);
    }

    [Fact]
    public void A05_CostExample()
    {
        var tariffs = new Tariffs([Price(TariffKind.Import, .30m, end: Day.End),
            Price(TariffKind.Import, .40m, Day.End), Price(TariffKind.Export, .08m)]);
        var rows = new[] { At(0, null, null, 10, 4), At(24, null, null, 5, 0) };
        var cost = tariffs.Evaluate(rows, new(Day.Start, Day.End.AddDays(1)), Zone);
        Assert.Equal(5, cost.Import);
        Assert.Equal(.32m, cost.Export);
        Assert.Equal(4.68m, cost.CoveredNet);
    }

    [Fact]
    public void A06_OverlapGapBoundaryAndFixedPrice()
    {
        Assert.Throws<ArgumentException>(() => new Tariffs([Price(TariffKind.Import, .3m), Price(TariffKind.Import, .4m)]));
        Assert.Throws<ArgumentException>(() => new Tariffs([Price(TariffKind.Import, -.1m)]));
        var start = new DateOnly(2026, 1, 31);
        var end = new DateOnly(2026, 2, 2);
        var tariffs = new Tariffs([Price(TariffKind.Import, .3m, start, start.AddDays(1)),
            Price(TariffKind.Export, .08m, start), Price(TariffKind.Monthly, 31, start, start.AddDays(1)),
            Price(TariffKind.Monthly, 56, start.AddDays(1))]);
        var row = new Interval(Calendar.Utc(start.AddDays(1), Zone).AddHours(-1),
            Calendar.Utc(start.AddDays(1), Zone).AddHours(1), null, null, 10, 0);
        var cost = tariffs.Evaluate([row], new(start, end), Zone);
        Assert.Equal(1.5m, cost.Import);
        Assert.Equal(3, cost.Fixed);
        Assert.True(cost.Estimated);
        Assert.False(cost.Complete);
        Assert.Null(cost.Net);
    }

    [Fact]
    public void A07_TimeZoneAndWinter()
    {
        var zone = Demo.Zone;
        var generated = Demo.Generate();
        foreach (var (day, hours) in new[] { (new DateOnly(2026, 3, 29), 23), (new DateOnly(2026, 10, 25), 25) })
        {
            var start = Calendar.Utc(day, zone);
            var end = Calendar.Utc(day.AddDays(1), zone);
            Assert.Equal(hours, (end - start).TotalHours);
            var rows = generated.Where(r => r.Start >= start && r.End <= end).ToArray();
            Assert.Equal(hours * 12, rows.Length);
            Assert.Equal(rows.Length, rows.DistinctBy(r => r.Start).Count());
            Assert.Equal(hours, rows.Sum(r => r.Hours), 20);
        }
        Assert.Equal(new Period(new(2025, 12, 1), new(2026, 3, 1)), Calendar.Select(new(2026, 2, 12), "season"));
        Assert.Equal(new Period(new(2025, 10, 13), new(2025, 10, 20)),
            Calendar.Compare(Calendar.Select(new(2026, 10, 14), "week"), "week", true, null));
        Assert.Equal(new DateOnly(2023, 2, 28),
            Calendar.Compare(Calendar.Select(new(2024, 2, 29), "day"), "day", true, null).Start);
    }

    [Fact]
    public void A08_MissingDataAndSegmentReset()
    {
        var rows = new[] { At(0, 6, 1), At(1, null, null), At(2, 0, 3) };
        var m = Analysis.Summarize(rows, 3, true);
        Assert.Equal(4, m.Load.Kwh);
        Assert.Equal(2m / 3, m.Coverage);
        Assert.Null(m.Autarky);
        var run = Scenario.Run([rows[0], rows[2]], Four, Zone);
        Assert.Equal(1, run.Resets);
        Assert.Equal(3, run.Flows[1].Import);
        Assert.Throws<ArgumentException>(() => Potential.Evaluate(new(Day), rows, Four, Zone, new([]), "1"));
    }

    [Fact]
    public void A09_ZeroBattery()
    {
        var result = Dispatch.Step(None, 0, 1, 5, 2);
        Assert.Equal(0, result.Import);
        Assert.Equal(3, result.Export);
        Assert.Equal(2, result.Direct);
    }

    [Fact]
    public void A10_A11_ChargeAndDischargeReference()
    {
        var first = Dispatch.Step(Four, 0, 1, 6, 1);
        Assert.Equal(2, first.Charge);
        Assert.Equal(1.8m, first.Stored);
        Assert.Equal(3, first.Export);
        Assert.Equal(.2m, first.ChargeLoss);
        var second = Dispatch.Step(Four, first.Stored, 1, 0, 3);
        Assert.Equal(1.62m, second.Discharge);
        Assert.Equal(1.38m, second.Import);
        Assert.Equal(0, second.Stored);
        Assert.Equal(.18m, second.DischargeLoss);
    }

    [Fact]
    public void A12_DeterministicConservation()
    {
        var random = new Random(24);
        foreach (var battery in new[] { None, Four, new Battery(10, 2, 5, 1, 1, .15m, .9m) })
        {
            decimal stored = 0;
            for (int i = 0; i < 2000; i++)
            {
                decimal hours = random.Next(1, 60) / 60m, pv = random.Next(0, 900) / 100m;
                decimal load = random.Next(0, 900) / 100m, added = random.Next(0, 300) / 100m;
                var f = Dispatch.Step(battery, stored, hours, pv, load, added);
                Assert.Equal(pv + f.Import, load + added + f.Export + f.Stored - stored + f.ChargeLoss + f.DischargeLoss, 20);
                Assert.Equal(added, f.AddedPv + f.AddedBattery + f.AddedGrid, 20);
                Assert.InRange(f.Stored, 0, battery.Usable);
                Assert.InRange(f.Charge, 0, battery.ChargeKw * hours);
                Assert.InRange(f.Discharge, 0, battery.DischargeKw * hours);
                Assert.True(f.Charge == 0 || f.Discharge == 0);
                stored = f.Stored;
            }
        }
    }

    [Fact]
    public void A13_IdenticalReferenceAndAbsoluteInitialInventory()
    {
        var rows = new[] { At(0, 0, 3), At(1, 6, 1) };
        var a = Scenario.Run(rows, Four, Zone, 1);
        var b = Scenario.Run(rows, Four, Zone, 1);
        var large = Scenario.Run(rows, Four with { Capacity = 10 }, Zone, 1);
        Assert.Equal(a.Flows, b.Flows);
        Assert.Equal(1, large.Initial);
        Assert.Equal(2.1m, large.Flows[0].Import);
    }

    [Fact]
    public void A14_A15_ShiftConservesEnergyAndAvoidsImport()
    {
        var rows = new[] { At(0, 3, 1), At(6, 0, 2) };
        var shift = new Shift(1, .5m, 1, new(18 * 60, 19 * 60), new(12 * 60, 13 * 60));
        var run = Scenario.Run(rows, None, Zone, shift: shift);
        Assert.Equal(3, run.Input.Sum(r => r.Load));
        Assert.Equal(1, run.Flows.Sum(f => f.Import));
        Assert.Equal(1, run.Flows.Sum(f => f.Export));
        Assert.All(run.Moves, m =>
        {
            Assert.Equal(rows[1].Start, m.Source);
            Assert.Equal(rows[0].Start, m.Target);
            Assert.InRange(m.Kwh, 0, .05m);
        });
        Assert.Equal(1, run.Moves.Sum(m => m.Kwh));
        Assert.Empty(Scenario.Run(rows, None, Zone, shift: shift with { Share = 0 }).Moves);
    }

    [Fact]
    public void A16_OnlyExportProtectsHousehold()
    {
        var rows = new[] { At(0, 6, 1), At(1, 0, 3) };
        var input = new Added(5, 10, new(0, 1440), [4], false);
        var house = Scenario.Run(rows, Four, Zone);
        var run = Scenario.Run(rows, Four, Zone, added: input);
        Assert.Equal(3, run.Added.Sum());
        Assert.Equal(5, run.Requested);
        Assert.Equal(house.Flows.Select(f => f.Stored), run.Flows.Select(f => f.Stored));
        Assert.Equal(house.Flows.Select(f => f.Import), run.Flows.Select(f => f.Import));
        Assert.Equal(0, run.Flows.Sum(f => f.AddedGrid));
    }

    [Fact]
    public void A17_FullCoverageAndImpossibleRequest()
    {
        var rows = new[] { At(0, 3, 1), At(1, 0, 2) };
        var input = new Added(3, 2, new(0, 1440), [4], true);
        var run = Scenario.Run(rows, None, Zone, added: input);
        Assert.Equal(3, run.Added.Sum());
        Assert.Equal(2, run.Flows.Sum(f => f.AddedPv));
        Assert.Equal(1, run.Flows.Sum(f => f.AddedGrid));
        Assert.Throws<ArgumentException>(() => Scenario.Run(rows, None, Zone, added: input with { Daily = 5 }));
        Assert.Equal(0, Scenario.Run(rows, None, Zone, added: input with { Daily = 0 }).Added.Sum());
    }

    [Fact]
    public void A18_CombinedBalance()
    {
        var rows = new[] { At(0, 8, 1), At(1, 0, 4) };
        var run = Scenario.Run(rows, Four, Zone, shift: new(1, .5m, 1, new(780, 840), new(720, 780)),
            added: new(3, 3, new(720, 840), [4], true));
        Assert.NotEmpty(run.Moves);
        Assert.Equal(3, run.Added.Sum());
        Assert.Equal(8 + run.Flows.Sum(f => f.Import), 8 + run.Flows.Sum(f => f.Export + f.ChargeLoss + f.DischargeLoss) +
            run.Flows[^1].Stored, 20);
    }

    [Fact]
    public void A19_NoAnnualizationOfShortPeriod()
    {
        var rows = Demo.Generate().Where(r => Calendar.Day(r.Start, Demo.Zone) == Day.Start).ToArray();
        var outcome = Potential.Evaluate(new(Day, Demo.Battery with { Capacity = 14 }, Investment: 1000), rows,
            Demo.Battery, Demo.Zone, new(Demo.Prices), "1");
        Assert.Null(outcome.PaybackYears);
        Assert.False(outcome.Validation.Sufficient);
    }

    [Fact]
    public void A20_EventsUseContiguousMeasuredEvidence()
    {
        var rows = new[] { At(0, 5, 1, 0, 4) with { Soc = 1 }, At(1, 2, 1, 0, 1) with { Soc = 1 },
            At(3, 3, 1, 0, 2) with { Soc = 1 } };
        var result = Analysis.Events(rows, Four, new()).Where(e => e.Type == "Einspeisung an oberer Ladegrenze").ToArray();
        Assert.Equal(2, result.Length);
        Assert.Equal(Noon, result[0].Start);
        Assert.Equal(Noon.AddHours(2), result[0].End);
        Assert.Equal(5, result[0].Kwh);
        Assert.Contains("120 Minuten · 5,00 kWh", result[0].Text);
    }
}

