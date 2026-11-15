using SolarInsight.Domain;

namespace SolarInsight.Application;

/// <summary>Daily energies of one local calendar day as delivered by the provider (null = not delivered).</summary>
public record DailyValues(DateOnly Day, decimal? Pv, decimal? Load, decimal? Import, decimal? Export,
    decimal? Charge, decimal? Discharge);

/// <summary>
/// One side of the balance. For the household: Direct = PV used immediately, Battery = battery discharge used
/// in the house, Grid = grid import used in the house. For PV: Direct = used immediately, Battery = charged
/// into the battery, Grid = exported. <see cref="Unsplit"/> is energy known to be PV-direct or battery but not
/// separable because the battery channels are missing for that day.
/// </summary>
public record BalanceRing(decimal Total, decimal Direct, decimal Battery, decimal Grid, decimal Unsplit);

/// <summary>Which data source each day of a ring came from. Every day is counted in exactly one bucket.</summary>
public record BalanceBasis(int Intervals, int Daily, int DailyUnsplit, int Running, int Missing, int Rejected,
    DateOnly[] MissingDays);

public record BalanceResult(BalanceRing? Household, BalanceRing? Production, BalanceBasis HouseholdBasis,
    BalanceBasis ProductionBasis, decimal GridCharge, decimal BatteryExport, int Days, bool PathsConfirmed);

/// <summary>
/// Combines interval and daily data into one period energy balance without double counting: per calendar day
/// it uses complete, plausible intervals when available (they resolve simultaneous flows), otherwise the
/// provider's daily energies. The balance identity is linear, so daily sums give the same household split as
/// summed intervals, except that simultaneous grid charging inside a day cannot be detected from daily values.
/// </summary>
public static class EnergyBalance
{
    /// <summary>Daily counters are rounded to 0.1 kWh per channel; allow that plus 2 % of the throughput.</summary>
    public static decimal DailyTolerance(decimal load, decimal pv) => Math.Max(.3m, .02m * Math.Max(load, pv));

    public static BalanceResult Evaluate(Interval[] rows, DailyValues[] daily, Period period, TimeZoneInfo zone,
        DateOnly today, bool pathsConfirmed)
    {
        var intervals = rows.ToLookup(r => Calendar.Day(r.Start, zone));
        var days = daily.ToDictionary(d => d.Day);
        var house = new Ring();
        var production = new Ring();
        decimal gridCharge = 0, batteryExport = 0;
        int count = 0;
        // Days before the first stored value are outside the data history, not gaps.
        var firstData = days.Keys.Concat(intervals.Select(g => g.Key)).DefaultIfEmpty(period.Start).Min();
        for (var day = period.Start; day < period.End; day = day.AddDays(1))
        {
            if (day < firstData) continue;
            // Future days are outside the available data, not missing data.
            if (day > today) break;
            count++;
            bool running = day == today;
            var dayRows = intervals[day].ToArray();
            var flows = dayRows.Select(r => EnergyInsights.Allocate(r, out _)).ToArray();
            decimal expected = (decimal)(Calendar.Utc(day.AddDays(1), zone) - Calendar.Utc(day, zone)).Ticks / TimeSpan.TicksPerHour;
            bool complete = dayRows.Length > 0 && flows.All(f => f is not null) &&
                Math.Abs(dayRows.Sum(r => r.Hours) - expected) < .0001m;
            if (complete || running && flows.Any(f => f is not null))
            {
                // A running day can only be described by the intervals recorded so far.
                var valid = dayRows.Zip(flows).Where(x => x.Second is not null).ToArray();
                house.Add(running ? "running" : "intervals", valid.Sum(x => x.First.Load!.Value),
                    valid.Sum(x => x.Second!.Direct), valid.Sum(x => x.Second!.Battery), valid.Sum(x => x.Second!.Grid), 0);
                production.Add(running ? "running" : "intervals", valid.Sum(x => x.First.Pv!.Value),
                    valid.Sum(x => x.Second!.PvDirect), valid.Sum(x => x.Second!.PvBattery), valid.Sum(x => x.Second!.PvExport), 0);
                gridCharge += valid.Sum(x => x.Second!.GridCharge);
                batteryExport += valid.Sum(x => x.Second!.BatteryExport);
                continue;
            }
            if (running)
            {
                house.Skip("running", day);
                production.Skip("running", day);
                continue;
            }
            if (!days.TryGetValue(day, out var d))
            {
                house.Skip("missing", day);
                production.Skip("missing", day);
                continue;
            }
            decimal tolerance = DailyTolerance(d.Load ?? 0, d.Pv ?? 0);
            var full = EnergyInsights.Allocate(d.Pv, d.Load, d.Import, d.Export, d.Charge, d.Discharge, tolerance, out var difference);
            if (full is not null)
            {
                house.Add("daily", d.Load!.Value, full.Direct, full.Battery, full.Grid, 0);
                production.Add("daily", d.Pv!.Value, full.PvDirect, full.PvBattery, full.PvExport, 0);
                gridCharge += full.GridCharge;
                batteryExport += full.BatteryExport;
                continue;
            }
            if (difference is not null)
            {
                // All six channels exist but do not balance: do not guess a split.
                house.Skip("rejected", day);
                production.Skip("rejected", day);
                continue;
            }
            // Without battery channels the grid side is still exact: import and export are metered, and the
            // rest of the household load came from PV directly or from the battery (not separable).
            if (d.Load is >= 0 && d.Import is >= 0 && d.Import <= d.Load + tolerance)
                house.Add("unsplit", d.Load.Value, 0, 0, d.Import.Value, Math.Max(0, d.Load.Value - d.Import.Value));
            else house.Skip(d.Load is null || d.Import is null ? "missing" : "rejected", day);
            if (d.Pv is >= 0 && d.Export is >= 0 && d.Export <= d.Pv + tolerance)
                production.Add("unsplit", d.Pv.Value, 0, 0, d.Export.Value, Math.Max(0, d.Pv.Value - d.Export.Value));
            else production.Skip(d.Pv is null || d.Export is null ? "missing" : "rejected", day);
        }
        return new(house.Result(), production.Result(), house.Basis(), production.Basis(), gridCharge, batteryExport,
            count, pathsConfirmed);
    }

    sealed class Ring
    {
        decimal total, direct, battery, grid, unsplit;
        int used;
        readonly Dictionary<string, int> sources = new();
        readonly List<DateOnly> missing = new();

        public void Add(string source, decimal t, decimal d, decimal b, decimal g, decimal u)
        {
            total += t; direct += d; battery += b; grid += g; unsplit += u;
            used++;
            sources[source] = sources.GetValueOrDefault(source) + 1;
        }

        public void Skip(string reason, DateOnly day)
        {
            sources[reason] = sources.GetValueOrDefault(reason) + 1;
            if (reason == "missing" && missing.Count < 60) missing.Add(day);
        }

        public BalanceRing? Result() => used == 0 ? null : new(total, direct, battery, grid, unsplit);

        public BalanceBasis Basis() => new(sources.GetValueOrDefault("intervals"), sources.GetValueOrDefault("daily"),
            sources.GetValueOrDefault("unsplit"), sources.GetValueOrDefault("running"), sources.GetValueOrDefault("missing"),
            sources.GetValueOrDefault("rejected"), missing.ToArray());
    }
}
