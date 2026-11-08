using System.Globalization;
using System.Text.Json;

namespace SolarInsight.Infrastructure;

public sealed record LiveMapping(string DeviceKey, int? Pv, int? Load, int? Grid, int? Battery, int? Soc,
    bool PositiveGridIsImport, bool PositiveBatteryIsCharge, bool SocPercent, bool Confirmed,
    bool BatteryExportExcluded = false, int? GridExport = null, int? BatteryDischarge = null)
{
    public Dictionary<string, int> Channels => new Dictionary<string, int?>
        { ["pv"] = Pv, ["load"] = Load, ["grid"] = Grid, ["battery"] = Battery, ["soc"] = Soc }
        .Where(pair => pair.Value.HasValue).ToDictionary(pair => pair.Key, pair => pair.Value!.Value);

    public IEnumerable<int> Points => Channels.Values.Concat(
        new[] { GridExport, BatteryDischarge }.OfType<int>());

    public bool Extends(LiveMapping previous)
    {
        var channels = Channels;
        return DeviceKey == previous.DeviceKey &&
            previous.Channels.All(channel => channels.TryGetValue(channel.Key, out int id) && id == channel.Value) &&
            (previous.Grid is null || GridExport == previous.GridExport &&
                (GridExport is not null || PositiveGridIsImport == previous.PositiveGridIsImport)) &&
            (previous.Battery is null || BatteryDischarge == previous.BatteryDischarge &&
                (BatteryDischarge is not null || PositiveBatteryIsCharge == previous.PositiveBatteryIsCharge)) &&
            (previous.Soc is null || SocPercent == previous.SocPercent);
    }

    public void Validate()
    {
        var points = Points.ToArray();
        if (string.IsNullOrWhiteSpace(DeviceKey) || DeviceKey.Length > 100 || points.Length == 0 ||
            (GridExport is not null && Grid is null) || (BatteryDischarge is not null && Battery is null) ||
            points.Any(p => p <= 0) || points.Distinct().Count() != points.Length)
            throw new ArgumentException("Ein bekanntes Gerät und eindeutige Live-Messpunkte angeben.");
    }
}

public sealed record LiveMeasurement(Guid PlantId, string Channel, string Source, DateTimeOffset MeasuredAt,
    DateTimeOffset FetchedAt, decimal Value, string Quality);
public sealed record LivePoll(Guid PlantId, DateTimeOffset FetchedAt, DateTimeOffset? MeasuredAt, string Quality, string Channels, DateTimeOffset? RetryAt = null);
public sealed record LiveGap(Guid PlantId, string Channel, DateTimeOffset Start, DateTimeOffset End, string Reason);
public sealed record LiveBatch(DateTimeOffset? MeasuredAt, IReadOnlyDictionary<string, decimal> Values)
{
    public static LiveBatch Unknown { get; } = new(null, new Dictionary<string, decimal>());
}

public static class LiveNormalization
{
    public const string Source = "sungrow-device-moment-v1";

    public static LiveBatch Map(JsonElement payload, string plant, LiveMapping mapping, TimeZoneInfo zone, DateTimeOffset now)
    {
        mapping.Validate();
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("device_point_list", out var devices) || devices.ValueKind != JsonValueKind.Array)
            throw new ProviderFailure("provider-payload", false);
        var matching = devices.EnumerateArray().Select(row =>
            row.ValueKind == JsonValueKind.Object && row.TryGetProperty("device_point", out var nested) ? nested : row)
            .Where(row => Field(row, "ps_key") == mapping.DeviceKey && Field(row, "ps_id") == plant).ToArray();
        if (matching.Length != 1) return LiveBatch.Unknown;
        var device = matching[0];
        if (!DateTime.TryParseExact(Field(device, "device_time"), "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local) || zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            return LiveBatch.Unknown;
        var at = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        if (at > now) return LiveBatch.Unknown;
        var units = new Dictionary<int, string>();
        if (payload.TryGetProperty("point_dict", out var dictionary) && dictionary.ValueKind == JsonValueKind.Array)
            foreach (var item in dictionary.EnumerateArray())
                if (int.TryParse(Field(item, "point_id"), out int id))
                {
                    string unit = Field(item, "point_unit");
                    if (!units.TryAdd(id, unit) && units[id] != unit) units[id] = "";
                }
        decimal? Power(int point, decimal raw) => units.GetValueOrDefault(point) switch
        {
            "W" => raw * .001m,
            "kW" => raw,
            _ => null
        };

        var values = new Dictionary<string, decimal>();
        foreach (var (channel, point) in mapping.Channels)
        {
            if (!decimal.TryParse(Field(device, $"p{point}"), NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
                continue;
            if (channel == "soc")
            {
                decimal soc = mapping.SocPercent ? raw / 100 : raw;
                if (soc is >= 0 and <= 1) values[channel] = soc;
                continue;
            }
            var power = Power(point, raw);
            if (power is null || (channel is "pv" or "load" && power < 0)) continue;
            var oppositePoint = channel switch
            {
                "grid" => mapping.GridExport,
                "battery" => mapping.BatteryDischarge,
                _ => null
            };
            if (oppositePoint is { } opposite)
            {
                if (power < 0 || !decimal.TryParse(Field(device, $"p{opposite}"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var oppositeRaw)) continue;
                var oppositePower = Power(opposite, oppositeRaw);
                if (oppositePower is not (>= 0)) continue;
                values[channel] = power.Value - oppositePower.Value;
                continue;
            }
            bool invert = channel == "grid" && !mapping.PositiveGridIsImport ||
                channel == "battery" && !mapping.PositiveBatteryIsCharge;
            values[channel] = invert ? -power.Value : power.Value;
        }
        return new(at, values);
    }

    static string Field(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) ? value.ToString() : "";
}
