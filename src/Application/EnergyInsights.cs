using SolarInsight.Domain;

namespace SolarInsight.Application;

public enum GridMix { Grid, GridPv, GridBattery, GridPvBattery }
public record HouseholdFlow(decimal Direct, decimal Battery, decimal Grid);
public record FlowRing(decimal Total, decimal Direct, decimal Battery, decimal Grid);
public record FlowBreakdown(FlowRing? Household, FlowRing? Production, decimal ValidHours, decimal MissingHours,
    decimal RejectedHours, decimal RejectedDifferenceKwh);
public record GridPhase(GridMix Mix, DateTimeOffset Start, DateTimeOffset End, decimal Kwh);
public record GridTotal(GridMix Mix, decimal Hours, decimal Kwh);
public record FlowPoint(DateTimeOffset Start, DateTimeOffset End, decimal? DirectKw, decimal? BatteryKw, decimal? GridKw,
    decimal? ImportKw, decimal? ExportKw, decimal? Soc, string? Limit, GridMix? Mix);

public static class EnergyInsights
{
    public const decimal ZeroKw = .05m;
    public static decimal Tolerance(Interval row) => ZeroKw * row.Hours + .000000001m;

    public static HouseholdFlow? Allocate(Interval row, bool confirmed, out decimal? difference)
    {
        difference = null;
        if (!confirmed || row.Pv is not >= 0 || row.Load is not >= 0 || row.Import is not >= 0 ||
            row.Export is not >= 0 || row.Charge is not >= 0 || row.Discharge is not >= 0) return null;

        decimal consumedDirect = row.Load.Value - row.Import.Value - row.Discharge.Value;
        decimal generatedDirect = row.Pv.Value - row.Export.Value - row.Charge.Value;
        decimal tolerance = Tolerance(row);
        difference = Math.Max(Math.Abs(consumedDirect - generatedDirect),
            Math.Max(-consumedDirect, -generatedDirect));
        if (difference > tolerance) return null;
        return new(Math.Max(0, consumedDirect), row.Discharge.Value, row.Import.Value);
    }

    public static GridMix? Mix(HouseholdFlow? flow, Interval row)
    {
        decimal tolerance = Tolerance(row);
        if (flow is null || flow.Grid <= tolerance) return null;
        return (flow.Direct > tolerance, flow.Battery > tolerance) switch
        {
            (true, true) => GridMix.GridPvBattery,
            (true, false) => GridMix.GridPv,
            (false, true) => GridMix.GridBattery,
            _ => GridMix.Grid
        };
    }

    public static (FlowBreakdown Breakdown, GridPhase[] Phases, FlowPoint[] Points) Analyze(
        Interval[] rows, bool confirmed, Battery? battery, decimal socTolerance, TimeZoneInfo zone, bool includePoints)
    {
        decimal hours = 0, missing = 0, rejected = 0, difference = 0;
        decimal load = 0, pv = 0, toLoad = 0, toGrid = 0, fromBattery = 0, fromGrid = 0, toBattery = 0, pvDirect = 0;
        var phases = new List<GridPhase>();
        var points = new List<FlowPoint>();
        foreach (var row in rows)
        {
            var flow = Allocate(row, confirmed, out var residual);
            var mix = Mix(flow, row);
            if (flow is not null)
            {
                hours += row.Hours;
                load += row.Load!.Value;
                pv += row.Pv!.Value;
                toLoad += flow.Direct;
                fromBattery += flow.Battery;
                fromGrid += flow.Grid;
                toBattery += row.Charge!.Value;
                toGrid += row.Export!.Value;
                pvDirect += Math.Max(0, row.Pv.Value - row.Charge.Value - row.Export.Value);
            }
            else if (residual.HasValue) { rejected += row.Hours; difference += residual.Value; }
            else missing += row.Hours;
            if (mix is { } category)
            {
                if (phases.LastOrDefault() is { } previous && previous.Mix == category &&
                    previous.End == row.Start && Calendar.Day(previous.Start, zone) == Calendar.Day(row.Start, zone))
                    phases[^1] = previous with { End = row.End, Kwh = previous.Kwh + flow!.Grid };
                else phases.Add(new(category, row.Start, row.End, flow!.Grid));
            }
            if (includePoints) points.Add(new(row.Start, row.End, flow?.Direct / row.Hours, flow?.Battery / row.Hours,
                flow?.Grid / row.Hours, row.Import / row.Hours, row.Export / row.Hours, row.Soc,
                StorageInsights.Limit(row, battery, socTolerance), mix));
        }
        var breakdown = new FlowBreakdown(hours == 0 ? null : new(load, toLoad, fromBattery, fromGrid),
            hours == 0 ? null : new(pv, pvDirect, toBattery, toGrid), hours, missing, rejected, difference);
        return (breakdown, phases.ToArray(), points.ToArray());
    }
}
