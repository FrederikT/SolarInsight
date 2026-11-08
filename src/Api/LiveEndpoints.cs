using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
                    new { key = DemoLive.Mapping.DeviceKey, Model = "Demo-Hybridwechselrichter", Type = "Synthetisch" } } };
            var devices = await db.Devices.AsNoTracking().Where(d => d.PlantId == plant.Id)
                .Select(d => new { key = d.ProviderId, d.Model, d.Type }).ToArrayAsync(ct);
            return new { mapping = await LiveMonitor.Mapping(db, plant.Id, ct), devices };
        });
        api.MapPost("/live/probe", async (LiveMapping mapping, Queries queries, Store db, LiveProvider provider,
            RuntimeOptions runtime, CancellationToken ct) =>
        {
            var plant = await queries.Plant(ct);
            if (!runtime.Demo) await ValidateDevice(db, plant.Id, mapping, ct);
            var batch = runtime.Demo ? DemoLive.Probe(DateTimeOffset.UtcNow, mapping) :
                await provider.ReadNow(plant, mapping, ct);
            return Results.Ok(new { batch.MeasuredAt, batch.Values, missing = mapping.Channels.Keys.Except(batch.Values.Keys) });
        }).RequireRateLimiting("login");
        api.MapPut("/live/mapping", async (LiveMapping mapping, Queries queries, Store db, RuntimeOptions runtime,
            CancellationToken ct) =>
        {
            if (runtime.Demo) return Results.StatusCode(403);
            var plant = await queries.Plant(ct);
            await ValidateDevice(db, plant.Id, mapping, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT pg_advisory_xact_lock(hashtextextended({plant.Id.ToString()}, 0))""", ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT pg_advisory_xact_lock(hashtextextended({plant.Id.ToString()}, 1))""", ct);
            var connection = await db.Connections.SingleAsync(c => c.PlantId == plant.Id, ct);
            var old = JsonSerializer.Deserialize<SungrowMapping>(connection.Capabilities)!;
            if (old.Live is { } previous &&
                !mapping.Extends(previous) &&
                await db.LiveMeasurements.AnyAsync(m => m.PlantId == plant.Id, ct))
                throw new ArgumentException("Die Zuordnung bereits erfasster Live-Werte erfordert eine Datenmigration.");
            db.Entry(connection).Property(c => c.Capabilities).CurrentValue = JsonSerializer.Serialize(old with { Live = mapping });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });
    }

    static async Task ValidateDevice(Store db, Guid plant, LiveMapping mapping, CancellationToken ct)
    {
        mapping.Validate();
        if (!await db.Devices.AnyAsync(d => d.PlantId == plant && d.ProviderId == mapping.DeviceKey, ct))
            throw new ArgumentException("Das Live-Gerät gehört nicht zur aktiven Anlage.");
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
