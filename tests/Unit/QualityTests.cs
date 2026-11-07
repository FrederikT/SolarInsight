using System.Text.Json;
using SolarInsight.Application;
using SolarInsight.Domain;
using Xunit;

namespace SolarInsight.Tests;

public class QualityTests
{
    [Fact]
    public void DemoHasCompleteSeasonalProductionAndDistinctQualityFixture()
    {
        var data = Demo.Generate();
        Assert.Equal(210240, data.Length);
        Assert.Equal(Demo.Generate()[210000], data[210000]);
        var byMonth = data.GroupBy(r => Calendar.Day(r.Start, Demo.Zone).Month).ToDictionary(g => g.Key);
        Assert.True(byMonth[7].Sum(r => r.Export) > byMonth[1].Sum(r => r.Export));
        Assert.True(byMonth[1].Sum(r => r.Import) > byMonth[7].Sum(r => r.Import));
        Assert.All(data.Take(600), row => Assert.Equal("synthetic", row.Quality));

        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "quality.json")));
        var rows = fixture.RootElement.GetProperty("samples").EnumerateArray().ToArray();
        var unique = rows.DistinctBy(row => row.GetProperty("utc").GetDateTimeOffset()).ToArray();
        Assert.Equal(1, rows.Length - unique.Length);
        var left = new Sample(unique[1].GetProperty("utc").GetDateTimeOffset(), unique[1].GetProperty("counterKwh").GetDecimal());
        var right = new Sample(unique[2].GetProperty("utc").GetDateTimeOffset(), unique[2].GetProperty("counterKwh").GetDecimal());
        Assert.Equal(2, (right.At - left.At).TotalMinutes / 5 - 1);
        Assert.Equal("reset", Normalization.Energy(Semantics.Counter, left, right, TimeSpan.FromMinutes(5)).Quality);
    }

    [Fact]
    public void A13_EndInventoryHasNoRealizedCredit()
    {
        Period period = new(new(2026, 1, 1), new(2026, 1, 2));
        var start = Calendar.Utc(period.Start, TimeZoneInfo.Utc);
        var rows = Enumerable.Range(0, 288).Select(i => new Interval(start.AddMinutes(5 * i),
            start.AddMinutes(5 * (i + 1)), i == 287 ? 2 : 0, 0, 0, i == 287 ? 2 : 0, Soc: 0)).ToArray();
        var zero = new Battery(0, 4, 4, 1, 1);
        var tariff = new Tariffs([new(Guid.NewGuid(), TariffKind.Import, period.Start, null, .4m),
            new(Guid.NewGuid(), TariffKind.Export, period.Start, null, .12m)]);
        var equal = Potential.Evaluate(new(period), rows, zero, TimeZoneInfo.Utc, tariff, "fixture");
        Assert.Equal(0, equal.Savings);
        var larger = Potential.Evaluate(new(period, zero with { Capacity = 2 }), rows, zero,
            TimeZoneInfo.Utc, tariff, "fixture");
        Assert.Equal(1m / 3, larger.Scenario.End, 20);
        Assert.Equal(-.04m, larger.Savings!.Value, 20);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A19_ZeroAndNegativeSavingsDoNotAmortize(bool paidExport)
    {
        Period year = new(new(2025, 1, 1), new(2026, 1, 1));
        var rows = Demo.Generate().Where(r => r.Start >= Calendar.Utc(year.Start, Demo.Zone) &&
            r.End <= Calendar.Utc(year.End, Demo.Zone)).ToArray();
        var tariffs = new Tariffs([new(Guid.NewGuid(), TariffKind.Import, year.Start, null, 0),
            new(Guid.NewGuid(), TariffKind.Export, year.Start, null, paidExport ? .3m : 0)]);
        var result = Potential.Evaluate(new(year, Demo.Battery with { Capacity = 25 }, Investment: 1000),
            rows, Demo.Battery, Demo.Zone, tariffs, "test");
        Assert.True(result.Validation.Sufficient);
        Assert.True(result.Validation.SocValidated);
        Assert.Null(result.PaybackYears);
        Assert.Equal("Unter diesen Annahmen keine statische Amortisation.", result.PaybackStatus);
        if (paidExport) Assert.True(result.Savings < 0);
        else Assert.Equal(0, result.Savings);
    }
}

