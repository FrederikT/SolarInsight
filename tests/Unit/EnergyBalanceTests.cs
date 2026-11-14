using SolarInsight.Application;
using SolarInsight.Domain;
using Xunit;

namespace SolarInsight.Tests;

public class EnergyBalanceTests
{
    static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    static readonly DateOnly First = new(2026, 8, 1);
    static DailyValues Day(int offset, decimal? pv, decimal? load, decimal? import, decimal? export,
        decimal? charge = null, decimal? discharge = null) =>
        new(First.AddDays(offset), pv, load, import, export, charge, discharge);

    static Interval Row(DateOnly day, int hour, decimal pv, decimal load, decimal import, decimal export,
        decimal charge, decimal discharge, decimal? soc = null)
    {
        var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
        return new(start, start.AddHours(1), pv, load, import, export, charge, discharge, soc);
    }

    [Fact]
    public void RatiosUsePairedValuesAndReportTheirBasis()
    {
        // Two days with load and import, one day without import: the ratio stays defined on the paired days.
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        Interval D(int i, decimal? pv, decimal? load, decimal? import, decimal? export) =>
            new(start.AddDays(i), start.AddDays(i + 1), pv, load, import, export);
        var m = Analysis.Summarize([D(0, 10, 20, 5, 2), D(1, 10, 20, 15, 0), D(2, 10, 20, null, 1)], 4 * 24);
        Assert.Equal(1 - 20m / 40, m.Autarky);
        Assert.Equal(.5m, m.AutarkyCoverage);
        Assert.Equal(1 - 3m / 30, m.Retention);
        Assert.Equal(.75m, m.RetentionCoverage);
    }

    [Fact]
    public void DailyValuesSplitHouseholdAndPvWithoutDoubleCounting()
    {
        // Day 0 has complete intervals (wins over its daily values), day 1 only daily values with battery,
        // day 2 daily values without battery channels, day 3 nothing.
        var intervals = Enumerable.Range(0, 24).Select(h => h is >= 10 and < 14
            ? Row(First, h, 3, 1, 0, 1, 1, 0) : Row(First, h, 0, .5m, .25m, 0, 0, .25m)).ToArray();
        DailyValues[] daily =
        [
            Day(0, 999, 999, 999, 999, 0, 0),
            Day(1, 20, 15, 1, 5, 9, 8),
            Day(2, 30, 12, 2, 18),
        ];
        var result = EnergyBalance.Evaluate(intervals, daily, new(First, First.AddDays(4)), Zone, First.AddDays(10), false);

        var house = result.Household!;
        // Intervals: load 4·1 + 20·0.5 = 14, of which grid 5, battery 5, PV 4.
        // Day 1: direct = 15 − 1 − 8 = 6. Day 2: grid 2, remaining 10 unsplit.
        Assert.Equal(14 + 15 + 12, house.Total);
        Assert.Equal(4 + 6, house.Direct);
        Assert.Equal(5 + 8, house.Battery);
        Assert.Equal(5 + 1 + 2, house.Grid);
        Assert.Equal(10, house.Unsplit);
        Assert.Equal(house.Total, house.Direct + house.Battery + house.Grid + house.Unsplit);

        var pv = result.Production!;
        Assert.Equal(12 + 20 + 30, pv.Total);
        Assert.Equal(pv.Total, pv.Direct + pv.Battery + pv.Grid + pv.Unsplit);
        var basis = result.HouseholdBasis;
        Assert.Equal((1, 1, 1, 0, 1, 0), (basis.Intervals, basis.Daily, basis.DailyUnsplit, basis.Running, basis.Missing, basis.Rejected));
        Assert.Equal([First.AddDays(3)], basis.MissingDays);
    }

    [Fact]
    public void MissingSingleChannelOnlyBlocksTheAffectedRing()
    {
        var result = EnergyBalance.Evaluate([], [Day(0, 20, null, 1, 5)], new(First, First.AddDays(1)), Zone, First.AddDays(5), false);
        Assert.Null(result.Household);
        Assert.Equal(1, result.HouseholdBasis.Missing);
        Assert.Equal(new BalanceRing(20, 0, 0, 5, 15), result.Production);
    }

    [Fact]
    public void FutureDaysAreNotCountedAsMissing()
    {
        var result = EnergyBalance.Evaluate([], [Day(0, 20, 15, 1, 5, 9, 8)], new(First, First.AddDays(30)), Zone, First.AddDays(1), false);
        Assert.Equal(2, result.Days);
        Assert.Equal(1, result.HouseholdBasis.Daily);
        Assert.Equal(1, result.HouseholdBasis.Running);
        Assert.Equal(0, result.HouseholdBasis.Missing);
    }

    [Fact]
    public void DaysBeforeTheFirstStoredValueAreNotCountedAsMissing()
    {
        var result = EnergyBalance.Evaluate([], [Day(10, 20, 15, 1, 5, 9, 8), Day(12, 20, 15, 1, 5, 9, 8)],
            new(First, First.AddDays(14)), Zone, First.AddDays(30), false);
        Assert.Equal(4, result.Days);
        Assert.Equal(2, result.HouseholdBasis.Daily);
        Assert.Equal([First.AddDays(11), First.AddDays(13)], result.HouseholdBasis.MissingDays);
    }

    [Fact]
    public void UnbalancedDailyValuesAreRejectedInsteadOfGuessed()
    {
        var result = EnergyBalance.Evaluate([], [Day(0, 20, 15, 1, 5, 2, 8)], new(First, First.AddDays(1)), Zone, First.AddDays(5), false);
        Assert.Null(result.Household);
        Assert.Equal(1, result.HouseholdBasis.Rejected);
    }

    [Fact]
    public void GridChargingIsSeparatedFromHouseholdImport()
    {
        // Night: 2 kWh import, of which 1 kWh charges the battery; the house uses 1 kWh.
        var flow = EnergyInsights.Allocate(0, 1, 2, 0, 1, 0, .01m, out var difference);
        Assert.NotNull(flow);
        Assert.Equal(0, difference);
        Assert.Equal(1, flow!.GridCharge);
        Assert.Equal(1, flow.Grid);
        Assert.Equal(0, flow.Direct);
        Assert.Equal(0, flow.PvBattery);
    }

    [Fact]
    public void StoragePotentialIsBoundedByExportAndImport()
    {
        DailyValues[] days = Enumerable.Range(0, 30).Select(i => Day(i, 30, 15, 4, 12)).ToArray();
        var result = StoragePotential.Evaluate(days, new([0, 2, 10]), 30);
        var none = result.Scenarios.Single(s => s.ExtraCapacity == 0);
        Assert.Equal(0, none.AvoidedImport);
        var small = result.Scenarios.Single(s => s.ExtraCapacity == 2);
        // Capacity limits: 2 kWh per day after the first fill.
        Assert.Equal(60, decimal.Round(small.AvoidedImport, 6));
        var large = result.Scenarios.Single(s => s.ExtraCapacity == 10);
        // Import limits: never more than the 4 kWh imported per day.
        Assert.Equal(120, large.AvoidedImport);
        Assert.True(large.LostExport <= 30 * 12);
        Assert.Equal(30, large.DaysDemandLimited);
        Assert.Equal(result.Current.Import - large.AvoidedImport, large.Totals.Import);
        Assert.True(large.Totals.Autarky > result.Current.Autarky);
        Assert.Equal(decimal.Round(120 * .30m - large.LostExport * .08m, 2), large.Savings);
    }

    [Fact]
    public void StoragePotentialWithoutSurplusYieldsNothing()
    {
        DailyValues[] days = Enumerable.Range(0, 10).Select(i => Day(i, 2, 15, 13, 0)).ToArray();
        var scenario = StoragePotential.Evaluate(days, new([5]), 10).Scenarios.Single(s => s.ExtraCapacity == 5);
        Assert.Equal(0, scenario.AvoidedImport);
        Assert.Equal(10, scenario.DaysSurplusLimited);
    }

    [Fact]
    public void BatteryEstimateRecoversCapacityAndLimits()
    {
        // 10 kWh battery: charge 1 kWh per hour from 10 % to 90 %, then discharge back, repeated for 3 days.
        var rows = new List<Interval>();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        decimal soc = .1m;
        for (int i = 0; i < 72; i++)
        {
            bool charging = i % 16 < 8;
            soc += charging ? .1m : -.1m;
            rows.Add(new(start.AddHours(i), start.AddHours(i + 1), 0, 0, 0, 0, charging ? 1 : 0, charging ? 0 : 1, soc));
        }
        var estimate = BatteryEstimation.Estimate(rows.ToArray(), []);
        Assert.NotNull(estimate);
        Assert.Equal(10, estimate!.CapacityKwh);
        Assert.Equal(.1m, estimate.MinSoc);
        Assert.Equal(.9m, estimate.MaxSoc);
    }

    [Fact]
    public void DailyObservationsIgnoreTheRunningDay()
    {
        DailyValues[] days = [Day(0, 40, 10, 0, 30), Day(1, 5, 20, 15, 0), Day(2, 80, 10, 0, 70)];
        var result = Observations.FromDaily(days, First.AddDays(2));
        Assert.Contains(result, o => o.Kind == "pv-max" && o.Day == First);
        Assert.Contains(result, o => o.Kind == "import-max" && o.Day == First.AddDays(1));
        Assert.Contains(result, o => o.Kind == "low-autarky");
    }
}
