using System.Globalization;
using Calendar = SolarInsight.Domain.Calendar;
using System.Text.Json;
using SolarInsight.Domain;

namespace SolarInsight.Infrastructure;

public record AggregateMapping(int? Pv, int? Load, int? Import, int? Export, int? Charge, int? Discharge)
{
    public (string Name, int Id)[] Fields => new (string, int?)[] { ("pv", Pv), ("load", Load),
        ("import", Import), ("export", Export), ("charge", Charge), ("discharge", Discharge) }
        .Where(x => x.Item2.HasValue).Select(x => (x.Item1, x.Item2!.Value)).ToArray();
    public int[] Points => Fields.Select(x => x.Id).ToArray();

    public void Validate()
    {
        if (Points.Any(p => p <= 0) || Points.Distinct().Count() != Points.Length)
            throw new ArgumentException("Tagesmesspunkte müssen eindeutig sein.");
    }

    public Aggregate[] Normalize(JsonElement raw, Guid plant, string providerId, TimeZoneInfo zone,
        DateOnly first, DateOnly last)
    {
        if (!raw.TryGetProperty(providerId, out var data)) return [];
        if (!raw.TryGetProperty("point_dict", out var dict) || dict.ValueKind != JsonValueKind.Array)
            throw new ProviderFailure("provider-payload", false);
        var units = new Dictionary<string, decimal>();
        foreach (var point in dict.EnumerateArray())
        {
            if (!point.TryGetProperty("point_id", out var id) || !point.TryGetProperty("point_unit", out var unit)) continue;
            if (unit.ToString() == "Wh") units[id.ToString().TrimStart('p')] = .001m;
            if (unit.ToString() == "kWh") units[id.ToString().TrimStart('p')] = 1;
        }
        var rows = new List<Aggregate>();
        foreach (var (name, id) in Fields)
        {
            if (!data.TryGetProperty("p" + id, out var series)) continue;
            if (!units.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var scale))
                throw new ProviderFailure("unconfirmed-unit", false);
            foreach (var item in series.EnumerateArray())
            {
                if (!item.TryGetProperty("time_stamp", out var at) ||
                    !DateOnly.TryParseExact(at.ToString(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
                    day < first || day > last || !item.TryGetProperty("2", out var field) ||
                    !decimal.TryParse(field.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) || value < 0)
                    continue;
                rows.Add(new(plant, name, Calendar.Utc(day, zone), Calendar.Utc(day.AddDays(1), zone), value * scale,
                    1, "provider", "sungrow-day-2"));
            }
        }
        return rows.GroupBy(r => new { r.Start, r.Point }).Select(g =>
            g.Distinct().Count() == 1 ? g.First() : throw new ProviderFailure("ambiguous-aggregate", false)).ToArray();
    }
}

