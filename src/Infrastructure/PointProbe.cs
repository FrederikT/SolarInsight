using System.Globalization;
using System.Text.Json;

namespace SolarInsight.Infrastructure;

public sealed record ProbePoint(int Id, string Channel, string Quantity);
public sealed record ProbeValue(DateTimeOffset? MeasuredAt, decimal Value);
public sealed record PointObservation(int Point, string Channel, string? Unit, bool Delivered, int ValidCount,
    string Status, ProbeValue[] Values);
public sealed record PointProbeResult(string Source, DateTimeOffset FetchedAt, PointObservation[] Points,
    string? Failure = null, int? HttpStatus = null, string? ResultCode = null, int? RetryAfterSeconds = null);

public static class PointProbe
{
    public static PointProbeResult DeviceNow(JsonElement payload, string plant, string device, ProbePoint[] points,
        TimeZoneInfo zone, DateTimeOffset fetchedAt)
    {
        var row = LiveNormalization.Device(payload, plant, device);
        var at = LiveNormalization.Timestamp(row, "device_time", zone);
        return Inspect("Geräte-Livewerte", payload, points, [(row, at <= fetchedAt ? at : null)], fetchedAt);
    }

    public static PointProbeResult Minutes(string source, JsonElement payload, string key, ProbePoint[] points,
        TimeZoneInfo zone, DateTimeOffset start, DateTimeOffset end, DateTimeOffset fetchedAt)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ProviderFailure("provider-payload", false);
        payload.TryGetProperty(key, out var series);
        if (series.ValueKind is not (JsonValueKind.Array or JsonValueKind.Undefined))
            throw new ProviderFailure("provider-payload", false);
        var rows = series.ValueKind == JsonValueKind.Array
            ? series.EnumerateArray().Select(row =>
            {
                var at = LiveNormalization.Timestamp(row, "time_stamp", zone);
                return (row, at >= start && at <= end ? at : null);
            }).ToArray()
            : [];
        return Inspect(source, payload, points, rows, fetchedAt);
    }

    public static PointProbeResult Inspect(string source, JsonElement payload, ProbePoint[] points,
        (JsonElement Row, DateTimeOffset? At)[] rows, DateTimeOffset fetchedAt)
    {
        var units = LiveNormalization.Units(payload);
        return new(source, fetchedAt, points.Select(point =>
        {
            var unit = units.GetValueOrDefault(point.Id);
            var key = "p" + point.Id.ToString(CultureInfo.InvariantCulture);
            var delivered = rows.Where(row => row.Row.ValueKind == JsonValueKind.Object &&
                row.Row.TryGetProperty(key, out _)).ToArray();
            var values = new List<ProbeValue>();
            foreach (var (row, at) in delivered)
                if (decimal.TryParse(LiveNormalization.Field(row, key), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out decimal value))
                    values.Add(new(at, value));
            bool knownUnit = point.Quantity switch
            {
                "power" => unit is "W" or "kW",
                "energy" => unit is "Wh" or "kWh",
                _ => unit == "%"
            };
            int valid = knownUnit ? values.Count(value => value.MeasuredAt.HasValue) : 0;
            string status = delivered.Length == 0 ? "missing" : values.Count == 0 ? "invalid-value" :
                !knownUnit ? "unknown-unit" : valid < values.Count ? "invalid-time" : "available";
            return new PointObservation(point.Id, point.Channel, unit, delivered.Length > 0, valid, status,
                values.ToArray());
        }).ToArray());
    }

    public static PointProbeResult Failed(string source, ProbePoint[] points, DateTimeOffset attemptedAt,
        ProviderFailure failure) => new(source, attemptedAt, points.Select(point =>
            new PointObservation(point.Id, point.Channel, null, false, 0, "not-evaluated", [])).ToArray(),
        failure.HttpStatus is 401 or 403 || failure.Category == "auth" ||
            failure.ResultCode is "E900" or "E918" or "E919" ? "authorization" :
            failure.ResultCode is "E901" or "E998" or "E999" ? "rate-limit" : failure.Category,
        failure.HttpStatus, failure.ResultCode,
        failure.RetryAfter is { } retry ? (int)Math.Ceiling(retry.TotalSeconds) : null);
}
