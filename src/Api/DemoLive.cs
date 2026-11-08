using SolarInsight.Application;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

internal static class DemoLive
{
    public const string Source = "synthetic";
    public static readonly LiveMapping Mapping = new("demo_14_0_0", 1, 13119, 13149, 13126, 13141,
        true, true, true, true, true, GridExport: 13121, BatteryDischarge: 13150);

    static readonly Lazy<Interval[]> Replay = new(() => Demo.Generate().TakeLast(288).ToArray());
    static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    static Interval At(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, Demo.Zone);
        return Replay.Value[(int)(local.TimeOfDay.Ticks / Step.Ticks)];
    }

    static Dictionary<string, decimal> Values(Interval row) => new()
    {
        ["pv"] = row.Pv!.Value / row.Hours,
        ["load"] = row.Load!.Value / row.Hours,
        ["grid"] = (row.Import!.Value - row.Export!.Value) / row.Hours,
        ["battery"] = (row.Charge!.Value - row.Discharge!.Value) / row.Hours,
        ["soc"] = row.Soc!.Value
    };

    public static LiveBatch Probe(DateTimeOffset now, LiveMapping mapping)
    {
        mapping.Validate();
        if (mapping.DeviceKey != Mapping.DeviceKey)
            throw new ArgumentException("Das Gerät gehört nicht zur Demo-Anlage.");
        var start = Calendar.Utc(Calendar.Day(now, Demo.Zone), Demo.Zone);
        var at = start.AddTicks((now - start).Ticks / Step.Ticks * Step.Ticks);
        var values = Values(At(at)).Where(pair => mapping.Channels.TryGetValue(pair.Key, out var point) &&
            Mapping.Channels[pair.Key] == point &&
            (pair.Key != "grid" || mapping.GridExport == Mapping.GridExport) &&
            (pair.Key != "battery" || mapping.BatteryDischarge == Mapping.BatteryDischarge));
        return new(at, values.ToDictionary());
    }

    public static LiveState State(Guid plant, DateTimeOffset now)
    {
        var batch = Probe(now, Mapping);
        var at = batch.MeasuredAt!.Value;
        var rows = batch.Values.Select(pair => new LiveMeasurement(plant, pair.Key, Source, at, now, pair.Value, "confirmed")).ToArray();
        return LiveStateView.Create(new(plant, now, at, "confirmed", string.Join(',', batch.Values.Keys)),
            rows, Mapping, now, Step) with { Quality = Source };
    }

    public static DaySample[] Today(DateTimeOffset start, DateTimeOffset now)
    {
        var samples = new List<DaySample>();
        for (var at = start; at <= now; at += Step)
            samples.AddRange(Values(At(at)).Select(pair => new DaySample(pair.Key, at, null, pair.Value, Source)));
        return samples.ToArray();
    }
}
