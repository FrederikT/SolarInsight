using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public sealed record Visibility(Guid Tab, bool Visible);
public sealed record DaySample(string Channel, DateTimeOffset At, DateTimeOffset? End, decimal Value, string Source);

public static class LiveEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/live/activity", async (Visibility input, Queries queries, OverviewActivity activity, CancellationToken ct) =>
        {
            var plant = await queries.Plant(ct);
            activity.Set(plant.Id, input.Tab, input.Visible, DateTimeOffset.UtcNow);
            return Results.NoContent();
        });
        api.MapGet("/live/now", async (Queries queries, Store db, LiveMonitor monitor, CancellationToken ct) =>
        {
            var plant = await queries.Plant(ct);
            if (plant.Synthetic) return DemoLive.State(plant.Id, DateTimeOffset.UtcNow);
            return await monitor.State(db, plant.Id, await LiveMonitor.Mapping(db, plant.Id, ct), ct);
        });
        api.MapGet("/live/today", async (Queries queries, Store db, CancellationToken ct) =>
        {
            var plant = await queries.Plant(ct);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
            var now = DateTimeOffset.UtcNow;
            var day = Calendar.Day(now, zone);
            var start = Calendar.Utc(day, zone);
            var end = Calendar.Utc(day.AddDays(1), zone);
            if (plant.Synthetic)
                return new { day, start, end, gaps = Array.Empty<LiveGap>(), samples = DemoLive.Today(start, now) };
            var history = await HistoryReconciliation.Read(db, plant.Id, start, end, ct);
            var moments = await db.LiveMeasurements.AsNoTracking().Where(m => m.PlantId == plant.Id &&
                m.MeasuredAt >= start && m.MeasuredAt < end && m.Quality == "confirmed").ToArrayAsync(ct);
            var gaps = await db.LiveGaps.AsNoTracking().Where(g => g.PlantId == plant.Id && g.Start < end && g.End > start)
                .OrderBy(g => g.Start).ToArrayAsync(ct);
            return new { day, start, end, gaps, samples = Combine(history, moments) };
        });
        api.MapGet("/live/mapping", async (Queries queries, Store db, CancellationToken ct) =>
        {
            var plant = await queries.Plant(ct);
            if (plant.Synthetic)
                return new { mapping = (LiveMapping?)DemoLive.Mapping, devices = new[] {
                    new { key = DemoLive.Mapping.DeviceKey, Model = "Demo-Hybridwechselrichter", Type = "Synthetisch",
                        candidateProbeSupported = false } } };
            var devices = await db.Devices.AsNoTracking().Where(d => d.PlantId == plant.Id)
                .Select(d => new { key = d.ProviderId, d.Model, d.Type }).ToArrayAsync(ct);
            return new
            {
                mapping = await LiveMonitor.Mapping(db, plant.Id, ct),
                devices = devices.Select(d => new { d.key, d.Model, d.Type,
                    candidateProbeSupported = ProviderEndpoints.SupportsDeviceProbe(d.key) }).ToArray()
            };
        });
        api.MapPost("/live/probe", async (Queries queries, Store db, LiveProvider provider,
            RuntimeOptions runtime, CancellationToken ct) =>
        {
            var plant = await queries.Plant(ct);
            var mapping = runtime.Demo ? DemoLive.Mapping : await LiveMonitor.Mapping(db, plant.Id, ct);
            if (mapping is not { Confirmed: true })
                return Results.Problem(statusCode: 409, detail: "Für diese Anlage fehlt ein bestätigtes Live-Profil in der Betreiberkonfiguration.");
            var batch = runtime.Demo ? DemoLive.Probe(DateTimeOffset.UtcNow, mapping) :
                await provider.ReadNow(plant, mapping, ct);
            return Results.Ok(new { batch.MeasuredAt, batch.Values, missing = mapping.Channels.Keys.Except(batch.Values.Keys) });
        }).RequireRateLimiting("login");
    }

    public static DaySample[] Combine(Reading[] history, LiveMeasurement[] moments)
    {
        var result = new List<DaySample>();
        foreach (var row in history.Where(r => r.SourceRevision == "sungrow-minutes-v1" &&
            r.Quality is "mean-power" or "trapezoid" && r.End > r.Start))
        {
            decimal hours = (decimal)(row.End - row.Start).TotalHours;
            var channels = new (string Channel, decimal? Value)[] {
                ("pv", row.Pv / hours), ("load", row.Load / hours),
                ("grid", (row.Import - row.Export) / hours), ("battery", (row.Charge - row.Discharge) / hours)
            };
            result.AddRange(channels.Where(c => c.Value.HasValue).Select(c =>
                new DaySample(c.Channel, row.Start, row.End, c.Value!.Value, "history")));
            if (row.Soc is { } soc) result.Add(new("soc", row.End, null, soc, "history"));
        }
        var replacements = result.ToLookup(s => s.Channel);
        result.AddRange(moments.Where(m => !replacements[m.Channel].Any(s =>
            s.End is { } end ? m.MeasuredAt >= s.At && m.MeasuredAt < end : m.MeasuredAt == s.At))
            .Select(m => new DaySample(m.Channel, m.MeasuredAt, null, m.Value, "live")));
        return result.OrderBy(s => s.At).ToArray();
    }
}

public sealed class LanAccess(IConfiguration configuration)
{
    readonly bool enabled = configuration.GetValue("Lan:Enabled", false);
    readonly System.Net.IPNetwork? network = System.Net.IPNetwork.TryParse(configuration["Lan:Network"], out var parsed)
        ? parsed : null;
    readonly byte[]? key = configuration.GetValue("Lan:Enabled", false) ? ReadKey(configuration["Lan:ReadKeyFile"]) : null;

    static byte[]? ReadKey(string? path)
    {
        if (path is null) return null;
        var value = File.ReadAllText(path).TrimEnd('\r', '\n');
        if (value.Length < 32 || value.Length > 256 || value.Any(c => c <= ' ' || c > '~'))
            throw new InvalidOperationException("Der Heimnetz-Leseschlüssel benötigt 32 bis 256 druckbare ASCII-Zeichen ohne Leerraum.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }

    public bool Enabled => enabled && network is not null && key is not null;

    public bool Allows(IPAddress? ip, string? authorization)
    {
        if (!Enabled || ip is null || authorization is null || authorization.Length > 263 ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return network!.Value.Contains(ip) && CryptographicOperations.FixedTimeEquals(key!,
            SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..])));
    }
}
