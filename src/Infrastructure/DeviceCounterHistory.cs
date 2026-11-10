using SolarInsight.Domain;

namespace SolarInsight.Infrastructure;

public static class DeviceCounterHistory
{
    public const string Source = "sungrow-device-counters-v1";
    public static readonly ProbePoint[] Points =
    [
        new(13134, "PV-Ertrag gesamt", "energy"), new(13130, "Hausverbrauch gesamt", "energy"),
        new(13148, "Netzbezug gesamt", "energy"), new(13125, "Einspeisung gesamt", "energy"),
        new(13034, "Batterieladung gesamt", "energy"), new(13035, "Batterieentladung gesamt", "energy")
    ];

    public static Interval[] Intervals(PointProbeResult report, DateTimeOffset start, DateTimeOffset end, TimeSpan step)
    {
        var series = report.Points.Where(p => p.Unit is "Wh" or "kWh").ToDictionary(p => p.Point, p =>
            p.Values.Where(v => v.MeasuredAt.HasValue && v.Value >= 0)
                .GroupBy(v => v.MeasuredAt!.Value)
                .Where(group => group.Select(v => v.Value).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => group.First().Value / (p.Unit == "Wh" ? 1000 : 1)));
        // The device route can repeat last-known counters while telemetry is missing.
        var telemetry = report.Points.Where(p => p.Unit is "W" or "kW").ToDictionary(p => p.Point,
            p => p.Values.Where(v => v.MeasuredAt.HasValue && v.Value >= 0)
                .Select(v => v.MeasuredAt!.Value).ToHashSet());
        var result = new List<Interval>();
        for (var at = start; at + step <= end; at += step)
        {
            var next = at + step;
            decimal? Delta(int point, int power)
            {
                if (!telemetry.TryGetValue(power, out var observed) || !observed.Contains(at) || !observed.Contains(next) ||
                    !series.TryGetValue(point, out var counter) ||
                    !counter.TryGetValue(at, out var first) || !counter.TryGetValue(next, out var last) || last < first)
                    return null;
                var delta = last - first;
                // Conservative ceiling for a validated residential profile, including counter rounding.
                return delta <= 25 * (decimal)step.TotalHours + .1m ? delta : null;
            }
            var pv = Delta(13134, 13003);
            var load = Delta(13130, 13119);
            var import = Delta(13148, 13149);
            var export = Delta(13125, 13121);
            var charge = Delta(13034, 13126);
            var discharge = Delta(13035, 13150);
            if (new[] { pv, load, import, export, charge, discharge }.Any(value => value.HasValue))
                result.Add(new(at, next, pv, load, import, export, charge, discharge, null, null, "counter-delta", Source));
        }
        return result.ToArray();
    }

    public static Interval[] Merge(Reading[] existing, Interval[] incoming)
    {
        var result = new List<Interval>();
        foreach (var row in incoming)
        {
            var overlapping = existing.Where(old => old.Start < row.End && old.End > row.Start).ToArray();
            if (overlapping.Length == 0) { result.Add(row); continue; }
            if (overlapping.Length != 1 || overlapping[0].Start != row.Start || overlapping[0].End != row.End) continue;
            var old = overlapping[0];
            bool retainedEstimate = old.Quality == "counter-mixed" || old.Quality == "trapezoid" &&
                (row.Pv is null && old.Pv.HasValue || row.Load is null && old.Load.HasValue ||
                 row.Import is null && old.Import.HasValue || row.Export is null && old.Export.HasValue ||
                 row.Charge is null && old.Charge.HasValue || row.Discharge is null && old.Discharge.HasValue);
            result.Add(row with
            {
                Pv = row.Pv ?? old.Pv, Load = row.Load ?? old.Load, Import = row.Import ?? old.Import,
                Export = row.Export ?? old.Export, Charge = row.Charge ?? old.Charge, Discharge = row.Discharge ?? old.Discharge,
                Soc = old.Soc, OpeningSoc = old.OpeningSoc, Quality = retainedEstimate ? "counter-mixed" : row.Quality
            });
        }
        return result.ToArray();
    }
}
