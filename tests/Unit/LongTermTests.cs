using SolarInsight.Application;
using SolarInsight.Domain;
using Xunit;

namespace SolarInsight.Tests;

public class LongTermTests
{
    static readonly DateOnly First = new(2025, 8, 16);
    static DailyValues Day(int offset, decimal pv, decimal load, decimal import, decimal export, decimal? charge = null, decimal? discharge = null) =>
        new(First.AddDays(offset), pv, load, import, export, charge, discharge);

    [Fact]
    public void ZeroLoadDaysAreOutagesAndTheFollowingDayIsACatchUp()
    {
        var flags = DayFlags.Detect([Day(0, 8, 12.2m, 1.4m, .2m), Day(2, 0, 0, 0, 0), Day(3, 136.1m, 38.3m, 1.4m, 95.8m)]);

        Assert.Equal([new DayFlag(First.AddDays(2), DayFlags.Outage), new DayFlag(First.AddDays(3), DayFlags.CatchUp)], flags);
    }

    [Fact]
    public void CatchUpDaysAreNotReportedAsRecords()
    {
        var observations = Observations.FromDaily([Day(0, 48, 17.7m, .9m, 30.6m), Day(1, 0, 0, 0, 0), Day(2, 136.1m, 38.3m, 1.4m, 95.8m)],
            First.AddDays(10));

        Assert.Contains(observations, o => o.Kind == "pv-max" && o.Kwh == 48);
        Assert.Contains(observations, o => o.Kind == "outage-days");
    }

    [Fact]
    public void CostsComparePvAndBatteryWithTheSamePrices()
    {
        var tariffs = new Tariffs([new Tariff(Guid.NewGuid(), TariffKind.Import, First, null, .30m),
            new Tariff(Guid.NewGuid(), TariffKind.Export, First, null, .08m),
            new Tariff(Guid.NewGuid(), TariffKind.Monthly, First, null, 31m)]);
        // Balanced day: 15 load + 5 charge + 10 export = 20 PV + 6 import + 4 discharge.
        var result = Economics.Evaluate([Day(0, 20, 15, 6, 10, 5, 4)], tariffs, First.AddDays(1));
        var day = result.Total;

        Assert.Equal(1, day.PricedDays);
        Assert.Equal(1.80m, day.ImportCost);
        Assert.Equal(.80m, day.ExportRevenue);
        Assert.Equal(1m, day.Fixed);
        Assert.Equal(2.00m, day.Net);
        Assert.Equal(5.50m, day.WithoutPv);
        Assert.Equal(3.50m, day.PvSavings);
        // Without battery: import 10 × .30 − export 15 × .08 + 1 = 2.80.
        Assert.Equal(2.80m, day.WithoutBattery);
        Assert.Equal(.80m, day.BatterySavings);
    }

    [Fact]
    public void DaysWithoutImportPriceAreNotPriced()
    {
        var result = Economics.Evaluate([Day(0, 20, 15, 6, 10)], new Tariffs([]), First.AddDays(1));

        Assert.False(result.HasImportPrice);
        Assert.Equal(0, result.PricedDays);
        Assert.Equal(0, result.Total.Net);
    }

    [Fact]
    public void BatteryHealthReportsEfficiencyCyclesAndExcludesOutages()
    {
        var days = Enumerable.Range(0, 400).Select(i => Day(i, 30, 15, 3, 12, 10, 9)).ToList();
        days[100] = Day(100, 0, 0, 0, 0, 0, 0);
        days[101] = Day(101, 60, 30, 6, 24, 20, 18);

        var result = BatteryHealth.Evaluate([.. days], 10, true, First.AddDays(500));

        Assert.Equal(398, result.Days);
        Assert.All(result.Years, y => Assert.Equal(.9m, y.Efficiency));
        Assert.Equal(358, result.TotalCycles);
        Assert.Contains(result.Findings, f => f.StartsWith("Der Wirkungsgrad ist stabil") || f.Contains("Winter"));
        Assert.Equal(2, result.Years.Length);
    }
}
