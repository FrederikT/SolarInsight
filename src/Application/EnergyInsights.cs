using SolarInsight.Domain;

namespace SolarInsight.Application;

public enum GridMix { Grid, GridPv, GridBattery, GridPvBattery }
/// <summary>
/// Allocation of one balanced interval. <see cref="GridCharge"/> is grid energy that went into the battery and
/// <see cref="BatteryExport"/> battery energy that went to the grid. Both are the minimum amounts consistent with
/// the meters; a single interval cannot distinguish simultaneous opposite flows any further.
/// </summary>
public record HouseholdFlow(decimal Direct, decimal Battery, decimal Grid, decimal GridCharge = 0,
    decimal BatteryExport = 0, decimal PvDirect = 0, decimal PvBattery = 0, decimal PvExport = 0);
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

    /// <summary>
    /// Splits one interval into household sources and PV destinations when all six energy channels exist and the
    /// balance Load + Charge + Export = PV + Import + Discharge closes within <see cref="Tolerance"/>.
    /// The balance is a metered identity, so unconfirmed internal AC paths do not block it; callers label the
    /// result as derived from the balance.
    /// </summary>
    public static HouseholdFlow? Allocate(Interval row, out decimal? difference) =>
        Allocate(row.Pv, row.Load, row.Import, row.Export, row.Charge, row.Discharge, Tolerance(row), out difference);

    public static HouseholdFlow? Allocate(decimal? pv, decimal? load, decimal? import, decimal? export,
        decimal? charge, decimal? discharge, decimal tolerance, out decimal? difference)
    {
        difference = null;
        if (pv is not >= 0 || load is not >= 0 || import is not >= 0 || export is not >= 0 ||
            charge is not >= 0 || discharge is not >= 0) return null;

        decimal consumedDirect = load.Value - import.Value - discharge.Value;
        decimal generatedDirect = pv.Value - export.Value - charge.Value;
        difference = Math.Abs(consumedDirect - generatedDirect);
        if (difference > tolerance) return null;
        // A negative direct share means grid and battery supplied more than the household used: the surplus
        // either charged the battery from the grid or left the battery towards the grid. Attribute the minimum
        // amount to each path that is actually present; anything left over is an implausible balance.
        decimal excess = Math.Max(0, -Math.Min(consumedDirect, generatedDirect));
        decimal gridCharge = Math.Min(excess, Math.Min(import.Value, charge.Value));
        decimal batteryExport = Math.Min(excess - gridCharge, Math.Min(discharge.Value, export.Value));
        decimal unexplained = excess - gridCharge - batteryExport;
        if (unexplained > tolerance)
        {
            difference = Math.Max(difference.Value, unexplained);
            return null;
        }
        decimal direct = Math.Max(0, consumedDirect);
        return new(direct, discharge.Value - batteryExport, import.Value - gridCharge, gridCharge, batteryExport,
            Math.Max(0, generatedDirect), charge.Value - gridCharge, export.Value - batteryExport);
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
        Interval[] rows, Battery? battery, decimal socTolerance, TimeZoneInfo zone, bool includePoints)
    {
        decimal hours = 0, missing = 0, rejected = 0, difference = 0;
        decimal load = 0, pv = 0, toLoad = 0, toGrid = 0, fromBattery = 0, fromGrid = 0, toBattery = 0, pvDirect = 0;
        var phases = new List<GridPhase>();
        var points = new List<FlowPoint>();
        foreach (var row in rows)
        {
            var flow = Allocate(row, out var residual);
            var mix = Mix(flow, row);
            if (flow is not null)
            {
                hours += row.Hours;
                load += row.Load!.Value;
                pv += row.Pv!.Value;
                toLoad += flow.Direct;
                fromBattery += flow.Battery;
                fromGrid += flow.Grid;
                toBattery += flow.PvBattery;
                toGrid += flow.PvExport;
                pvDirect += flow.PvDirect;
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
