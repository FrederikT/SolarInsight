using SolarInsight.Application;
using SolarInsight.Domain;
using Xunit;

namespace SolarInsight.Tests;

public class EnergyInsightsTests
{
    static readonly DateTimeOffset Origin = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly Battery Battery = new(10, 5, 5, 1, 1, .1m, .9m);
    static Interval Row(DateTimeOffset start, decimal? pv, decimal load, decimal? import = 0,
        decimal export = 0, decimal charge = 0, decimal? discharge = 0, decimal? soc = .1m) =>
        new(start, start.AddHours(1), pv, load, import, export, charge, discharge, soc);

    [Fact]
    public void ProductionAndLoadRingsUseSamePlausibleIntervals()
    {
        Interval[] rows = [Row(Origin, 100, 80, export: 10, charge: 10, soc: .9m),
            Row(Origin.AddHours(1), 0, 10, discharge: 10),
            Row(Origin.AddHours(2), null, 5, 5),
            Row(Origin.AddHours(3), 20, 5, 5)];
        var result = EnergyInsights.Analyze(rows, Battery, .02m, TimeZoneInfo.Utc, true);
        Assert.Equal(new FlowRing(100, 80, 10, 10), result.Breakdown.Production);
        Assert.Equal(new FlowRing(90, 80, 10, 0), result.Breakdown.Household);
        Assert.Equal(2, result.Breakdown.ValidHours);
        Assert.Equal(1, result.Breakdown.MissingHours);
        Assert.Equal(1, result.Breakdown.RejectedHours);
        Assert.Null(result.Points[3].DirectKw);
        // Unconfirmed AC paths no longer block a split that closes the metered balance.
        Assert.Equal(new FlowRing(90, 80, 10, 0), EnergyInsights.Analyze(rows, null, 0, TimeZoneInfo.Utc, false).Breakdown.Household);
        var zero = EnergyInsights.Analyze([Row(Origin, 0, 0)], null, 0, TimeZoneInfo.Utc, false);
        Assert.Equal(0, zero.Breakdown.Household!.Total);
        Assert.Equal(0, zero.Breakdown.Production!.Total);
    }

    [Fact]
    public void GridIntervalsAreExclusiveAndMissingDischargeDoesNotBecomeZero()
    {
        var result = EnergyInsights.Analyze([
            Row(Origin, .02m, 1.02m, 1),
            Row(Origin.AddHours(1), 1, 2, 1),
            Row(Origin.AddHours(2), 0, 2, 1, discharge: 1),
            Row(Origin.AddHours(3), 1, 3, 1, discharge: 1),
            Row(Origin.AddHours(4), 0, 1, 1, discharge: null)
        ], Battery, .02m, TimeZoneInfo.Utc, true);
        Assert.Equal(Enum.GetValues<GridMix>(), result.Phases.Select(p => p.Mix));
        Assert.All(result.Phases, p => Assert.Equal(1, p.Kwh));
        Assert.Null(result.Points[4].Mix);
    }

    [Fact]
    public void MeanSocAndSimultaneousFrequenciesHaveIndependentDenominators()
    {
        var start = new DateOnly(2026, 10, 1);
        var rows = Enumerable.Range(0, 11).Select(i => Row(Origin.AddDays(i).AddHours(3), 0, 1,
            import: i == 7 ? null : i % 2, export: i == 8 ? 1 : 0,
            soc: i < 8 ? .1m : i < 10 ? .5m : null)).ToArray();
        var profile = StorageInsights.Profile(rows, new(start, start.AddDays(11)), TimeZoneInfo.Utc, Battery, .02m, 3);
        var hour = profile.Hours[3];
        Assert.Equal(.1m, hour.UsableSoc);
        Assert.Equal(new Frequency(8, 10), hour.Empty);
        Assert.Equal(new Frequency(3, 9), hour.EmptyImport);
        Assert.Equal(new Frequency(3, 4), hour.EmptyAmongImports);
        Assert.Equal(new Frequency(0, 10), hour.FullExport);
        Assert.Equal(7, profile.Lower.AtLimitGridHours);
        Assert.Null(profile.Days[^1].Empty);
        Assert.False(profile.Days[^1].Import);
    }

    [Fact]
    public void StorageLimitsFollowConfigurationEffectiveOnEachDay()
    {
        var day = new DateOnly(2026, 10, 1);
        var changed = Origin.AddDays(1);
        var replacement = Battery with { MinSoc = .3m };
        Battery? Limits(Interval row) => row.Start < changed ? Battery : replacement;
        var rows = new[] { Row(Origin.AddHours(3), 0, 1, 1, soc: .3m),
            Row(changed.AddHours(3), 0, 1, 1, soc: .3m) };
        var profile = StorageInsights.Profile(rows, new(day, day.AddDays(2)), TimeZoneInfo.Utc, null, .02m, 3, Limits);
        Assert.Equal(1, profile.Lower.Hours);
        Assert.Equal(2, profile.Lower.EligibleHours);
        Assert.Equal(new Frequency(1, 2), profile.Hours[3].EmptyImport);
        Assert.Equal(.125m, profile.Hours[3].UsableSoc);
    }

    [Fact]
    public void DstUsesActualHoursAndCountsRepeatedHourOncePerDay()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        foreach (var day in new[] { new DateOnly(2026, 3, 29), new DateOnly(2026, 10, 25) })
        {
            var period = new Period(day, day.AddDays(1));
            var from = Calendar.Utc(period.Start, zone);
            var to = Calendar.Utc(period.End, zone);
            var rows = Enumerable.Range(0, (int)(to - from).TotalHours).Select(i => Row(from.AddHours(i), 0, 1, 1)).ToArray();
            var profile = StorageInsights.Profile(rows, period, zone, Battery, .02m, 2);
            Assert.Equal((decimal)(to - from).TotalHours, profile.Lower.Hours);
            if (day.Month == 3) Assert.Empty(profile.Days);
            else
            {
                Assert.Equal(2, Assert.Single(profile.Days).ExpectedHours);
                Assert.Equal(new Frequency(1, 1), profile.Hours[2].EmptyImport);
            }
        }
    }

    [Fact]
    public void NightDurationStopsAtFirstPvOrDataGap()
    {
        var start = Origin.AddHours(22);
        var day = new DateOnly(2026, 10, 1);
        var rows = Enumerable.Range(0, 9).Select(i => Row(start.AddHours(i), i == 8 ? 1 : 0, 1, 1,
            soc: i == 0 ? .5m : .1m)).ToArray();
        var night = StorageInsights.Night(rows, day, TimeZoneInfo.Utc, Battery, .02m);
        Assert.Equal(7, night.EmptyHours);
        Assert.Equal(start.AddHours(8), night.FirstPv);
        Assert.True(night.Complete);
        Assert.Equal(new NightDetail(day, 1, false, null), StorageInsights.Night(
            rows.Where((_, i) => i != 2).ToArray(), day, TimeZoneInfo.Utc, Battery, .02m));
        Assert.Null(StorageInsights.Night(rows, day, TimeZoneInfo.Utc, null, .02m).EmptyHours);
        var charged = StorageInsights.Night(rows.Select(r => r with { Soc = .5m }).ToArray(), day, TimeZoneInfo.Utc, Battery, .02m);
        Assert.Equal(0, charged.EmptyHours);
        Assert.True(charged.Complete);
    }
}
