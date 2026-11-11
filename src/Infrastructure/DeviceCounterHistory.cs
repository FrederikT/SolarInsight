using SolarInsight.Domain;

namespace SolarInsight.Infrastructure;

public static class DeviceCounterHistory
{
    public const string Source = "sungrow-device-counters-v1";
    public const string GapOrigin = "counter-gap";
    public const string SocSource = "sungrow-device-history-soc-v1";
    static readonly (int Counter, int Power, string Key)[] Channels =
        [(13134, 13003, "pv"), (13130, 13119, "load"), (13148, 13149, "import"),
         (13125, 13121, "export"), (13034, 13126, "charge"), (13035, 13150, "discharge")];
    public static readonly ProbePoint[] Points =
    [
        new(13134, "PV-Ertrag gesamt", "energy"), new(13130, "Hausverbrauch gesamt", "energy"),
        new(13148, "Netzbezug gesamt", "energy"), new(13125, "Einspeisung gesamt", "energy"),
        new(13034, "Batterieladung gesamt", "energy"), new(13035, "Batterieentladung gesamt", "energy")
    ];

    public static Interval[] Intervals(PointProbeResult report, DateTimeOffset start, DateTimeOffset end, TimeSpan step) =>
        Analyze(report, start, end, step).Intervals;

    public static (Interval[] Intervals, CounterSpan[] Gaps) Analyze(PointProbeResult report,
        DateTimeOffset start, DateTimeOffset end, TimeSpan step)
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
        var spans = new List<CounterSpan>();
        foreach (var (counter, power, key) in Channels)
        {
            if (!series.TryGetValue(counter, out var readings) || !telemetry.TryGetValue(power, out var observed)) continue;
            DateTimeOffset? previous = null;
            foreach (var at in observed.Where(at => at >= start && at <= end &&
                (at - start).Ticks % step.Ticks == 0 && readings.ContainsKey(at)).Order())
            {
                if (previous is { } from)
                {
                    var delta = readings[at] - readings[from];
                    // A reset reported inside an outage invalidates the entire span.
                    var values = readings.Where(pair => pair.Key >= from && pair.Key <= at)
                        .OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
                    bool reset = values.Zip(values.Skip(1)).Any(pair => pair.First > pair.Second);
                    if (!reset && delta >= 0 && delta <= 25 * (decimal)(at - from).TotalHours + .1m)
                        spans.Add(new(key, from, at, delta));
                }
                previous = at;
            }
        }
        var intervals = spans.Where(span => span.End - span.Start == step).GroupBy(span => span.Start).Select(group =>
        {
            decimal? Value(string key) => group.FirstOrDefault(span => span.Channel == key)?.Kwh;
            return new Interval(group.Key, group.Key + step, Value("pv"), Value("load"), Value("import"),
                Value("export"), Value("charge"), Value("discharge"), null, null, "counter-delta", Source);
        }).OrderBy(row => row.Start).ToArray();
        return (intervals, spans.Where(span => span.End - span.Start > step).ToArray());
    }

    public sealed record CounterSpan(string Channel, DateTimeOffset Start, DateTimeOffset End, decimal Kwh);

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
