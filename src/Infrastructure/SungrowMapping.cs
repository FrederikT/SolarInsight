using System.Globalization;
using System.Text.Json;
using SolarInsight.Domain;

namespace SolarInsight.Infrastructure;

public record SungrowMapping(int? Pv, int? Load, int? Grid, int? Battery, int? Soc, bool PositiveGridIsImport,
    bool PositiveBatteryIsCharge, bool Instantaneous, bool EndStamped, bool SocPercent, bool ConfirmedAcPaths,
    AggregateMapping? Daily = null, int StepMinutes = 5, LiveMapping? Live = null)
{
    public int[] Points => new int?[] { Pv, Load, Grid, Battery, Soc }.OfType<int>().ToArray();

    public void Validate()
    {
        if (Points.Any(p => p <= 0) || Points.Distinct().Count() != Points.Length || Points.Length + (Daily?.Points.Length ?? 0) == 0)
            throw new ArgumentException("Mindestens ein eindeutiger, bestätigter Messpunkt ist erforderlich.");
        if (StepMinutes is not (5 or 15)) throw new ArgumentException("Historische Auflösung muss 5 oder 15 Minuten sein.");
        Daily?.Validate();
        Live?.Validate();
    }
}

public record MinuteDiagnostics(JsonValueKind Payload, JsonValueKind PlantSeries, JsonValueKind PointDictionary,
    int RawSamples, int ValidTimestamps, int PowerPointsWithKnownUnit, int Intervals,
    int OtherNonemptyArrays = 0, int OtherObjects = 0);

public static class SungrowNormalization
{
    public static Interval[] Map(JsonElement data, string plant, SungrowMapping mapping, TimeZoneInfo zone,
        DateTimeOffset start, DateTimeOffset end, Action<MinuteDiagnostics>? report = null)
    {
        mapping.Validate();
        if (data.ValueKind != JsonValueKind.Object)
        {
            report?.Invoke(new(data.ValueKind, JsonValueKind.Undefined, JsonValueKind.Undefined, 0, 0, 0, 0));
            throw new ProviderFailure("provider-payload", false);
        }
        data.TryGetProperty(plant, out var plantData);
        data.TryGetProperty("point_dict", out var dict);
        int rawCount = plantData.ValueKind == JsonValueKind.Array ? plantData.GetArrayLength() : 0;
        var units = PowerUnits(dict);
        int knownUnits = units.Keys.Count(mapping.Points.Contains);
        void Report(int timestamps = 0, int intervals = 0)
        {
            if (report is null) return;
            int otherArrays = 0, otherObjects = 0;
            foreach (var property in data.EnumerateObject())
            {
                if (property.NameEquals(plant) || property.NameEquals("point_dict")) continue;
                if (property.Value.ValueKind == JsonValueKind.Object) otherObjects++;
                if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0) otherArrays++;
            }
            report(new(data.ValueKind, plantData.ValueKind, dict.ValueKind, rawCount, timestamps, knownUnits, intervals,
                otherArrays, otherObjects));
        }
        if (plantData.ValueKind == JsonValueKind.Undefined ||
            (plantData.ValueKind == JsonValueKind.Array && rawCount == 0))
        {
            Report();
            return [];
        }
        if (plantData.ValueKind != JsonValueKind.Array || dict.ValueKind != JsonValueKind.Array)
        {
            Report();
            throw new ProviderFailure("provider-payload", false);
        }

        var samples = new SortedDictionary<DateTimeOffset, JsonElement>();
        foreach (var sample in plantData.EnumerateArray())
        {
            if (sample.ValueKind != JsonValueKind.Object || !sample.TryGetProperty("time_stamp", out var time) ||
                !DateTime.TryParseExact(time.ToString(), "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime local) || zone.IsAmbiguousTime(local) || zone.IsInvalidTime(local))
                continue;
            var at = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
            if (samples.TryGetValue(at, out var previous) && previous.GetRawText() != sample.GetRawText())
                throw new ProviderFailure("ambiguous-sample", false);
            samples[at] = sample;
        }
        var result = new List<Interval>();
        var step = TimeSpan.FromMinutes(mapping.StepMinutes);
        decimal hours = mapping.StepMinutes / 60m;
        foreach (var (at, sample) in samples)
        {
            var from = !mapping.Instantaneous && mapping.EndStamped ? at - step : at;
            if (from < start || from + step > end) continue;
            if ((from.UtcTicks - start.UtcTicks) % step.Ticks != 0) throw new ProviderFailure("time-grid", false);
            if (mapping.Instantaneous && !samples.ContainsKey(at + step)) continue;
            decimal? Value(JsonElement pointRow, int id)
            {
                if (pointRow.ValueKind != JsonValueKind.Object || !pointRow.TryGetProperty("p" + id, out var value) ||
                    !decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    return null;
                return parsed;
            }
            decimal? Power(int? point)
            {
                if (point is not { } id) return null;
                var raw = Value(sample, id);
                if (raw is null) return null;
                if (!units.TryGetValue(id, out decimal scale)) throw new ProviderFailure("unconfirmed-unit", false);
                if (!mapping.Instantaneous) return raw * scale * hours;
                var next = Value(samples[at + step], id);
                return (raw + next) / 2 * scale * hours;
            }
            decimal? Soc(JsonElement row)
            {
                decimal? value = mapping.Soc is { } point ? Value(row, point) / (mapping.SocPercent ? 100 : 1) : null;
                return value is >= 0 and <= 1 ? value : null;
            }

            decimal? pv = Power(mapping.Pv), load = Power(mapping.Load), grid = Power(mapping.Grid);
            decimal? battery = mapping.Battery is { } b ? Power(b) : null;
            if (!mapping.PositiveGridIsImport) grid = -grid;
            if (!mapping.PositiveBatteryIsCharge) battery = -battery;
            decimal? Positive(decimal? energy) => energy is { } v ? Math.Max(v, 0) : null;
            pv = pv is >= 0 ? pv : null;
            load = load is >= 0 ? load : null;
            var soc = Soc(samples.GetValueOrDefault(from + step));
            var openingSoc = Soc(samples.GetValueOrDefault(from));
            if (pv is null && load is null && grid is null && battery is null && soc is null && openingSoc is null)
                continue;
            result.Add(new(from, from + step, pv, load,
                Positive(grid), Positive(-grid), Positive(battery), Positive(-battery),
                soc, openingSoc,
                mapping.Instantaneous ? "trapezoid" : "mean-power", "sungrow-minutes-v1"));
        }
        Report(samples.Count, result.Count);
        return result.ToArray();
    }

    static Dictionary<int, decimal> PowerUnits(JsonElement dict)
    {
        var units = new Dictionary<int, decimal>();
        if (dict.ValueKind != JsonValueKind.Array) return units;
        foreach (var item in dict.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("point_id", out var id) ||
                !int.TryParse(id.ToString().TrimStart('p'), out int point) ||
                !item.TryGetProperty("point_unit", out var unit)) continue;
            if (unit.ToString() == "W") units[point] = .001m;
            if (unit.ToString() == "kW") units[point] = 1;
        }
        return units;
    }
}

