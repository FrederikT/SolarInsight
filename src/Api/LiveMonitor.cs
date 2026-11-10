using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public sealed class OverviewActivity
{
    // Each tab owns a short lease. A hidden or closed tab cannot end another tab's lease.
    readonly ConcurrentDictionary<(Guid Plant, Guid Tab), DateTimeOffset> leases = new();
    public void Set(Guid plant, Guid tab, bool visible, DateTimeOffset now)
    {
        if (visible)
        {
            if (leases.Count < 1024 || leases.ContainsKey((plant, tab)))
                leases[(plant, tab)] = now.AddSeconds(70);
        }
        else leases.TryRemove((plant, tab), out _);
    }

    public bool Visible(Guid plant, DateTimeOffset now)
    {
        bool visible = false;
        foreach (var lease in leases)
        {
            if (lease.Value <= now) leases.TryRemove(lease);
            else if (lease.Key.Plant == plant) visible = true;
        }
        return visible;
    }
}

public sealed record LiveValue(decimal? Value, string Direction, DateTimeOffset? MeasuredAt, long? AgeSeconds, string Quality);
public sealed record LiveState(LiveValue Pv, LiveValue Load, LiveValue Grid, LiveValue Battery, LiveValue Soc,
    decimal? PvSurplusKw, bool FlowConfirmed, DateTimeOffset? FetchedAt, string Quality)
{
    public LiveTiming? Timing { get; init; }
}
public sealed record LiveTiming(bool OverviewVisible, int CloudIntervalSeconds, DateTimeOffset? SourceMeasuredAt,
    DateTimeOffset? NextEligibleAt, DateTimeOffset? RetryAt);

public static class LiveStateView
{
    public static LiveState Create(LivePoll? poll, LiveMeasurement[] rows, LiveMapping? mapping, DateTimeOffset now,
        TimeSpan maxAge)
    {
        var configured = mapping is { Confirmed: true } ? mapping.Channels : null;
        LiveValue Channel(string channel, string positive = "neutral", string negative = "neutral")
        {
            var row = poll?.Channels.Split(',').Contains(channel) == true
                ? rows.FirstOrDefault(r => r.Channel == channel) : null;
            var at = row?.MeasuredAt;
            long? age = at is { } measured ? Math.Max(0, (long)(now - measured).TotalSeconds) : null;
            string quality = configured?.ContainsKey(channel) != true ? "unconfigured" : poll?.Quality switch
            {
                "confirmed" or "repeated" => row is null ? "missing" : now - row.MeasuredAt > maxAge ? "stale" : "confirmed",
                null => "missing",
                _ => "unavailable"
            };
            decimal? value = quality == "confirmed" ? row?.Value : null;
            return new(value, value switch { null => "unknown", > 0 => positive, < 0 => negative, _ => "idle" },
                at, age, quality);
        }

        var pv = Channel("pv");
        var load = Channel("load");
        var grid = Channel("grid", "import", "export");
        var battery = Channel("battery", "charge", "discharge");
        bool flow = pv.Value is { } p && load.Value is { } l && grid.Value is { } g && battery.Value is { } b &&
            pv.MeasuredAt == load.MeasuredAt && pv.MeasuredAt == grid.MeasuredAt && pv.MeasuredAt == battery.MeasuredAt &&
            Math.Abs(p + g - l - b) <= Math.Max(.1m, Math.Max(p, l) * .05m);
        return new(pv, load, grid, battery, Channel("soc"),
            flow && grid.Value is < 0 && mapping?.BatteryExportExcluded == true ? -grid.Value : null,
            flow, poll?.FetchedAt, poll?.Quality ?? (mapping?.Confirmed == true ? "missing" : "unconfigured"));
    }
}

public sealed class LiveMonitor(IServiceScopeFactory scopes, LiveProvider provider, RuntimeOptions runtime,
    IConfiguration configuration, OverviewActivity activity, ILogger<LiveMonitor> logger) : BackgroundService
{
    sealed record Snapshot(Guid Plant, LivePoll? Poll, LiveMeasurement[] Rows);
    volatile Snapshot? cache;
    DateTimeOffset retryAt;
    int failures;
    readonly TimeSpan background = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Sungrow:LiveBackgroundSeconds", 600), 300, 600));
    // The documented device feed refreshes about every five minutes.
    readonly TimeSpan cloudFloor = TimeSpan.FromSeconds(provider.LiveMinCloudSeconds);
    readonly TimeSpan maxAge = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Sungrow:LiveMaxAgeSeconds", 900), 60, 3600));

    public async Task<LiveState> State(Store db, Guid plant, LiveMapping? mapping, CancellationToken ct)
    {
        if (mapping?.Confirmed != true)
            return LiveStateView.Create(null, [], mapping, DateTimeOffset.UtcNow, maxAge);
        var snapshot = cache;
        if (snapshot?.Plant != plant) snapshot = await Restore(db, plant, ct);
        var now = DateTimeOffset.UtcNow;
        var visible = activity.Visible(plant, now);
        var cadence = Cadence(visible);
        var poll = snapshot.Poll;
        var due = poll?.FetchedAt + cadence;
        if (poll?.RetryAt is { } wait && (due is null || wait > due)) due = wait;
        return LiveStateView.Create(poll, snapshot.Rows, mapping, now, maxAge) with
        {
            Timing = new(visible, (int)cadence.TotalSeconds, poll?.MeasuredAt, due, poll?.RetryAt)
        };
    }

    TimeSpan Cadence(bool visible) => visible || background < cloudFloor ? cloudFloor : background;

    static async Task<Snapshot> Restore(Store db, Guid plant, CancellationToken ct)
    {
        var poll = await db.LivePolls.AsNoTracking().Where(p => p.PlantId == plant)
            .OrderByDescending(p => p.FetchedAt).FirstOrDefaultAsync(ct);
        return new(plant, poll, await LiveStorage.At(db, plant, poll?.MeasuredAt, ct));
    }

    public static async Task<LiveMapping?> Mapping(Store db, Guid plant, CancellationToken ct)
    {
        var metadata = await db.Connections.AsNoTracking().Where(c => c.PlantId == plant && c.Status == "mapped")
            .Select(c => c.Capabilities).SingleOrDefaultAsync(ct);
        return metadata is null ? null : JsonSerializer.Deserialize<SungrowMapping>(metadata)?.Live;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (runtime.Demo || !provider.Configured) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try { await Tick(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Live-Cache konnte nicht aktualisiert werden. Kategorie {Category}", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    async Task Tick(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Store>();
        var plant = await db.Plants.AsNoTracking().SingleOrDefaultAsync(p => p.Active && p.ProviderId != null, ct);
        if (plant is null || await Mapping(db, plant.Id, ct) is not { Confirmed: true } mapping) return;
        if (cache?.Plant != plant.Id)
        {
            cache = await Restore(db, plant.Id, ct);
            failures = 0;
            retryAt = cache.Poll?.RetryAt ?? default;
        }
        var now = DateTimeOffset.UtcNow;
        var cadence = Cadence(activity.Visible(plant.Id, now));
        if (now < retryAt || (cache.Poll is { } previous && now - previous.FetchedAt < cadence)) return;
        string? failure = null;
        LiveBatch batch = LiveBatch.Unknown;
        try
        {
            batch = await provider.ReadNow(plant, mapping, ct);
            failures = 0;
            retryAt = default;
        }
        catch (Exception ex) when (ex is ProviderFailure or HttpRequestException or TimeoutException)
        {
            failure = ex is ProviderFailure providerFailure ? providerFailure.Category : ex is TimeoutException ? "timeout" : "network";
            var delay = TimeSpan.FromMinutes(5 * (1 << Math.Min(failures++, 4)));
            if (ex is ProviderFailure { RetryAfter: { } retry } && retry > delay) delay = retry;
            retryAt = DateTimeOffset.UtcNow + delay;
        }
        var poll = await LiveStorage.Save(db, plant.Id, mapping.Channels.Keys, batch, DateTimeOffset.UtcNow,
            background > cloudFloor ? background : cloudFloor, failure, retryAt == default ? null : retryAt, ct, mapping);
        if (poll is not null) cache = new(plant.Id, poll, await LiveStorage.At(db, plant.Id, poll.MeasuredAt, ct));
    }
}
