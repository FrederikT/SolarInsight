using SolarInsight.Application;
using Xunit;

namespace SolarInsight.Tests;

public class BatteryCounterDaysTests
{
    static readonly DateOnly Day = new(2024, 7, 15);

    // Counter readings of 15.07.2024 00:00 from the device history probe; the next midnight adds one balanced day.
    static CounterSnapshot Start => new(Day, new Dictionary<string, decimal>
    {
        ["pv"] = 12474.9m, ["load"] = 7794.6m, ["import"] = 2867.1m, ["export"] = 7276.8m,
        ["charge"] = 2396.2m, ["discharge"] = 2125.6m
    });

    static CounterSnapshot End(decimal pv = 71.6m, decimal load = 15.3m, decimal import = 4.4m, decimal export = 54.2m,
        decimal charge = 12.5m, decimal discharge = 6.0m) =>
        new(Day.AddDays(1), new Dictionary<string, decimal>
        {
            ["pv"] = 12474.9m + pv, ["load"] = 7794.6m + load, ["import"] = 2867.1m + import,
            ["export"] = 7276.8m + export, ["charge"] = 2396.2m + charge, ["discharge"] = 2125.6m + discharge
        });

    static readonly Dictionary<string, decimal> Stored = new()
    {
        ["pv"] = 71.6m, ["load"] = 15.3m, ["import"] = 4.4m, ["export"] = 54.2m
    };

    [Fact]
    public void BatteryChannelsComeFromCounterDifferencesWhenTheStoredDayAgrees()
    {
        var day = BatteryCounterDays.Evaluate(Start, End(), Day, Stored);

        Assert.Equal("accepted", day.Status);
        Assert.Equal(["charge", "discharge"], day.Missing);
        Assert.Equal(12.5m, day.Values["charge"]);
        Assert.Equal(6.0m, day.Values["discharge"]);
        Assert.All(day.Deviations.Values, d => Assert.Equal(0, d));
    }

    [Fact]
    public void DaysWithoutAnyStoredValueCanBeFilledCompletely()
    {
        var day = BatteryCounterDays.Evaluate(Start, End(), Day, new Dictionary<string, decimal>());

        Assert.Equal("accepted", day.Status);
        Assert.Equal(BatteryCounterDays.Channels, day.Missing);
    }

    [Fact]
    public void StoredValuesThatDisagreeRejectTheWholeDay()
    {
        var stored = new Dictionary<string, decimal>(Stored) { ["load"] = 16.0m };

        var day = BatteryCounterDays.Evaluate(Start, End(), Day, stored);

        Assert.Equal("rejected", day.Status);
        Assert.Contains("load", day.Reason);
        Assert.Empty(day.Missing);
    }

    [Fact]
    public void AnUnbalancedDayIsRejected()
    {
        var day = BatteryCounterDays.Evaluate(Start, End(charge: 20m), Day, Stored);

        Assert.Equal("rejected", day.Status);
        Assert.StartsWith("Bilanz", day.Reason);
    }

    [Fact]
    public void FallingCountersAndMissingReadingsAreNotUsed()
    {
        Assert.Equal("rejected", BatteryCounterDays.Evaluate(Start, End(discharge: -1m, charge: 5.5m), Day, Stored).Status);
        Assert.Equal("missing", BatteryCounterDays.Evaluate(Start, null, Day, Stored).Status);
        var partial = new CounterSnapshot(Day.AddDays(1), new Dictionary<string, decimal> { ["pv"] = 1 });
        Assert.Equal("missing", BatteryCounterDays.Evaluate(Start, partial, Day, Stored).Status);
    }

    [Fact]
    public void ADayWithAllSixStoredValuesIsReportedAsComplete()
    {
        var stored = new Dictionary<string, decimal>(Stored) { ["charge"] = 12.5m, ["discharge"] = 6.0m };

        Assert.Equal("complete", BatteryCounterDays.Evaluate(Start, End(), Day, stored).Status);
    }
}
