using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public record AppKeys(string AppKey, string AccessKey, string Redirect);
public record LiveTokens(string Access, string Refresh, DateTimeOffset Expires);
public record AuthorizedPlant(string Id, string Name);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public record LiveActivation(string Plant, string? Zone = null, int Page = 1);

public sealed class LiveProvider(IServiceScopeFactory scopes, IDataProtectionProvider protection,
    SungrowDiscovery sungrow, IConfiguration configuration, RuntimeOptions runtime, ILogger<LiveProvider> logger)
    : IHistorySource
{
    readonly IDataProtector protector = protection.CreateProtector("sungrow-tokens-v1");
    readonly SemaphoreSlim authLock = new(1, 1);
    readonly SemaphoreSlim apiLock = new(1, 1);
    DateTimeOffset nextCall;
    int liveWaiting;
    readonly SemaphoreSlim liveLock = new(1, 1);
    DateTimeOffset nextLiveCall;
    (Guid Plant, LiveMapping Mapping, LiveBatch Batch)? lastLive;
    public int LiveMinCloudSeconds => Math.Clamp(configuration.GetValue("Sungrow:LiveMinCloudSeconds", 300), 300, 86400);
    public TimeSpan Window => TimeSpan.FromMinutes(165);
    public bool Configured => !runtime.Demo && configuration["Sungrow:AppFile"] is { } file && File.Exists(file);

    public int CallSpacingSeconds => Math.Clamp(configuration.GetValue("Sungrow:CallSpacingSeconds", 15), 1, 86400);
    public int RefreshMinutes => Math.Clamp(configuration.GetValue("Sungrow:RefreshMinutes", 1440), 60, 10080);

    AppKeys Keys()
    {
        if (!Configured) throw new ArgumentException("Die serverseitige App-Datei der freigegebenen Sungrow-Anwendung fehlt.");
        try
        {
            var keys = JsonSerializer.Deserialize<AppKeys>(File.ReadAllText(configuration["Sungrow:AppFile"]!),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (keys is null || string.IsNullOrWhiteSpace(keys.AppKey) || string.IsNullOrWhiteSpace(keys.AccessKey) ||
                !Uri.TryCreate(keys.Redirect, UriKind.Absolute, out var uri) || !SungrowDiscovery.IsAllowedRedirect(uri))
                throw new ArgumentException("Ungültige serverseitige Sungrow-App-Konfiguration.");
            return keys;
        }
        catch (JsonException)
        {
            throw new ArgumentException("Ungültige serverseitige Sungrow-App-Konfiguration.");
        }
    }

    async Task<JsonElement> Send(Func<CancellationToken, Task<JsonElement>> call, CancellationToken ct, bool priority = false)
    {
        while (true)
        {
            await apiLock.WaitAsync(ct);
            var wait = nextCall - DateTimeOffset.UtcNow;
            if (wait <= TimeSpan.Zero && (priority || Volatile.Read(ref liveWaiting) == 0)) break;
            apiLock.Release();
            if (wait > TimeSpan.FromMinutes(2)) throw new ProviderFailure("rate-limit", true, wait);
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(100), ct);
        }
        try
        {
            nextCall = DateTimeOffset.UtcNow.AddSeconds(CallSpacingSeconds);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            try { return await call(timeout.Token); }
            catch (ProviderFailure failure) when (failure.Category == "rate-limit" && failure.RetryAfter is { } retry)
            {
                var resume = DateTimeOffset.UtcNow + retry;
                if (resume > nextCall) nextCall = resume;
                throw;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException(); }
        }
        finally { apiLock.Release(); }
    }

    public async Task Authorize(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 256) throw new ArgumentException("Ungültiger Autorisierungscode.");
        var keys = Keys();
        await authLock.WaitAsync(ct);
        try
        {
            var result = await Send(c => sungrow.Exchange(keys.AppKey, keys.AccessKey, code, new(keys.Redirect), c), ct);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Store>();
            await SaveTokens(db, ParseTokens(result), ct);
        }
        finally { authLock.Release(); }
    }

    static LiveTokens ParseTokens(JsonElement raw)
    {
        if (!raw.TryGetProperty("access_token", out var access) || access.ValueKind != JsonValueKind.String ||
            !raw.TryGetProperty("refresh_token", out var refresh) || refresh.ValueKind != JsonValueKind.String ||
            !raw.TryGetProperty("expires_in", out var duration) || !int.TryParse(duration.ToString(), out int seconds) ||
            seconds <= 0 || seconds > 2_678_400)
            throw new ProviderFailure("provider-payload", false);
        return new(access.GetString()!, refresh.GetString()!, DateTimeOffset.UtcNow.AddSeconds(seconds));
    }

    async Task SaveTokens(Store db, LiveTokens tokens, CancellationToken ct)
    {
        var existing = await db.Connections.FindAsync([Storage.LocalId], ct);
        var encrypted = protector.Protect(JsonSerializer.Serialize(tokens));
        var current = new ProviderConnection(Storage.LocalId, "authorized", encrypted, "{}");
        if (existing is null) db.Connections.Add(current);
        else db.Entry(existing).CurrentValues.SetValues(current);
        await db.SaveChangesAsync(ct);
    }

    async Task<(AppKeys Keys, string Token)> Access(CancellationToken ct, bool priority = false)
    {
        var keys = Keys();
        await authLock.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Store>();
            var existing = await db.Connections.FindAsync([Storage.LocalId], ct) ??
                throw new ProviderFailure("auth", false);
            LiveTokens tokens;
            try
            {
                tokens = JsonSerializer.Deserialize<LiveTokens>(protector.Unprotect(existing.ProtectedSettings)) ??
                    throw new ProviderFailure("auth", false);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                throw new ProviderFailure("key-restore", false);
            }
            if (tokens.Expires <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                var raw = await Send(c => sungrow.Refresh(keys.AppKey, keys.AccessKey, tokens.Refresh, c), ct, priority);
                tokens = ParseTokens(raw);
                await SaveTokens(db, tokens, ct);
            }
            return (keys, tokens.Access);
        }
        finally { authLock.Release(); }
    }

    public async Task<AuthorizedPlant[]> Plants(int page, CancellationToken ct)
    {
        if (page is < 1 or > 100) throw new ArgumentException("Ungültige Seite.");
        var (keys, token) = await Access(ct);
        var raw = await Send(c => sungrow.Plants(keys.AppKey, keys.AccessKey, token, page, c), ct);
        return Page(raw).Where(item => item.TryGetProperty("ps_id", out _) && item.TryGetProperty("ps_name", out _))
            .Select(item => new AuthorizedPlant(item.GetProperty("ps_id").ToString(),
                item.GetProperty("ps_name").ToString())).Where(p => p.Id.Length is >= 1 and <= 30 && p.Id.All(char.IsAsciiDigit))
            .ToArray();
    }

    static JsonElement.ArrayEnumerator Page(JsonElement raw) =>
        raw.TryGetProperty("pageList", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray() : throw new ProviderFailure("provider-payload", false);

    public async Task Activate(LiveActivation input, CancellationToken ct)
    {
        if (input.Plant is null || input.Plant.Length is < 1 or > 30 || !input.Plant.All(char.IsAsciiDigit))
            throw new ArgumentException("Eine autorisierte Anlage auswählen.");
        var authorized = (await Plants(input.Page, ct)).FirstOrDefault(p => p.Id == input.Plant)
            ?? throw new ArgumentException("Die Anlage ist nicht autorisiert. Anlagenliste neu laden.");
        var (keys, token) = await Access(ct);
        var devices = new Dictionary<string, (string Model, string Type)>();
        for (int page = 1; page <= 10; page++)
        {
            int index = page;
            var raw = await Send(c => sungrow.Devices(keys.AppKey, keys.AccessKey, token, input.Plant, index, c), ct);
            var rows = Page(raw).ToArray();
            foreach (var item in rows)
            {
                if (!item.TryGetProperty("ps_key", out var key)) continue;
                string id = key.ToString();
                if (id.Length is < 1 or > 100) continue;
                string Label(string field, string fallback) => item.TryGetProperty(field, out var value) &&
                    value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 100 } label
                    ? label : fallback;
                devices[id] = (Label("device_model_code", "Unbestätigt"), Label("type_name", "Sungrow-Gerät"));
            }
            if (rows.Length < 10) break;
        }
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Store>();
        var setup = scope.ServiceProvider.GetRequiredService<SungrowSetup>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(702928349)", ct);
        var plantId = await db.Plants.Where(p => p.ProviderId == input.Plant).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);
        Plant? plant = null;
        if (plantId is { } existingId)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT pg_advisory_xact_lock(hashtextextended({existingId.ToString()}, 0))""", ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT pg_advisory_xact_lock(hashtextextended({existingId.ToString()}, 1))""", ct);
            plant = await Endpoints.LockPlant(db, existingId, ct);
        }
        var zone = input.Zone ?? plant?.Zone ?? setup.DefaultZone;
        if (!zone.Contains('/') || !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
            throw new ArgumentException("Ungültige Anlagenzeitzone. Eine IANA-Zeitzone wie Europe/Berlin angeben.");
        var connection = plant is null ? null : await db.Connections.FindAsync([plant.Id], ct);
        var old = connection is null ? null : JsonSerializer.Deserialize<SungrowMapping>(connection.Capabilities);
        var mapping = setup.Resolve(input.Plant, old);
        if (mapping.Live != old?.Live && mapping.Live is { } live && !devices.ContainsKey(live.DeviceKey))
            throw new ArgumentException("Das Live-Gerät im Betreiberprofil gehört nicht zur ausgewählten Anlage.");
        if (plant is not null)
        {
            bool changed = old != mapping || plant.Zone != zone;
            if (changed && await db.Imports.AnyAsync(j => j.PlantId == plant.Id &&
                (j.Status == "pending" || j.Status == "running"), ct))
                throw new ArgumentException("Die Konfiguration kann nach Ende des laufenden Imports angewendet werden.");
            bool data = await db.Readings.AnyAsync(r => r.PlantId == plant.Id, ct) ||
                await db.Aggregates.AnyAsync(a => a.PlantId == plant.Id, ct);
            var added = mapping.Daily;
            bool additive = old is not null && (old with { Daily = added, Live = mapping.Live }) == mapping &&
                (old.Daily?.Fields.All(field => added?.Fields.Contains(field) == true) ?? true);
            if (data && (!additive || plant.Zone != zone))
                throw new ArgumentException("Eine Änderung der Messpunktzuordnung oder Zeitzone erfordert eine separate Datenmigration.");
            if ((plant.Zone != zone || old?.Live is { } previous && mapping.Live?.Extends(previous) != true) &&
                await db.LiveMeasurements.AnyAsync(m => m.PlantId == plant.Id, ct))
                throw new ArgumentException("Eine Änderung bereits erfasster Live-Werte erfordert eine separate Datenmigration.");
            if (changed) plant.Revision++;
        }
        await db.Plants.Where(p => p.Active).ExecuteUpdateAsync(s => s.SetProperty(p => p.Active, false), ct);
        if (plant is null)
        {
            plant = new Plant { Id = Guid.NewGuid(), Name = authorized.Name is { Length: > 0 and <= 100 } ? authorized.Name : "Sungrow-Anlage",
                ProviderId = input.Plant, Zone = zone,
                Active = true, Revision = 1 };
            db.Plants.Add(plant);
        }
        else
        {
            plant.Active = true;
            db.Entry(plant).Property(p => p.Active).IsModified = true;
            plant.Zone = zone;
        }
        var metadata = new ProviderConnection(plant.Id, "mapped", "", JsonSerializer.Serialize(mapping));
        if (connection is null) db.Connections.Add(metadata);
        else db.Entry(connection).CurrentValues.SetValues(metadata);
        foreach (var (deviceId, info) in devices)
        {
            var existing = await db.Devices.FindAsync([plant.Id, deviceId], ct);
            var metadataDevice = new Device(plant.Id, deviceId, info.Model, info.Type);
            if (existing is null) db.Devices.Add(metadataDevice);
            else db.Entry(existing).CurrentValues.SetValues(metadataDevice);
        }
        await setup.PlanInitial(db, plant, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<LiveBatch> ReadNow(Plant plant, LiveMapping mapping, CancellationToken ct)
    {
        await liveLock.WaitAsync(ct);
        try
        {
            var identity = mapping with { Confirmed = false, BatteryExportExcluded = false };
            var now = DateTimeOffset.UtcNow;
            if (now < nextLiveCall)
            {
                if (lastLive is { } previous && previous.Plant == plant.Id && previous.Mapping == identity)
                    return previous.Batch;
                throw new ProviderFailure("rate-limit", true, nextLiveCall - now);
            }
            lastLive = null;
            // Take the token lock before claiming priority so a concurrent token refresh can finish.
            var (keys, token) = await Access(ct, priority: true);
            Interlocked.Increment(ref liveWaiting);
            try
            {
                var raw = await Send(c =>
                {
                    nextLiveCall = DateTimeOffset.UtcNow.AddSeconds(LiveMinCloudSeconds);
                    return sungrow.DeviceNow(keys.AppKey, keys.AccessKey, token, mapping, c);
                }, ct, priority: true);
                var batch = LiveNormalization.Map(raw, plant.ProviderId!, mapping,
                    TimeZoneInfo.FindSystemTimeZoneById(plant.Zone), DateTimeOffset.UtcNow);
                lastLive = (plant.Id, identity, batch);
                return batch;
            }
            finally { Interlocked.Decrement(ref liveWaiting); }
        }
        finally { liveLock.Release(); }
    }

    public async Task<Interval[]> Read(Guid plantId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Store>();
        var plant = await db.Plants.AsNoTracking().SingleAsync(p => p.Id == plantId, ct);
        var metadata = await db.Connections.AsNoTracking().SingleAsync(c => c.PlantId == plantId, ct);
        var mapping = JsonSerializer.Deserialize<SungrowMapping>(metadata.Capabilities)!;
        if (mapping.Points.Length == 0) return [];
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        var (keys, token) = await Access(ct);
        var today = Calendar.Utc(Calendar.Day(DateTimeOffset.UtcNow, zone), zone);
        int step = mapping.StepMinutes;
        var queryEnd = end.AddMinutes(step) < today ? end.AddMinutes(step) : end;
        var localStart = TimeZoneInfo.ConvertTime(start, zone);
        var localEnd = TimeZoneInfo.ConvertTime(queryEnd, zone);
        // Local-only API timestamps cannot identify the repeated hour. Its coverage remains unknown.
        if (localStart.Offset != localEnd.Offset)
        {
            var collected = new List<Interval>();
            DateTimeOffset? segment = null;
            for (var at = start; at <= end; at = at.AddMinutes(step))
            {
                var localAt = TimeZoneInfo.ConvertTime(at, zone);
                var localNext = TimeZoneInfo.ConvertTime(at.AddMinutes(2 * step), zone);
                bool safe = at < end && localAt.Offset == localNext.Offset &&
                    !zone.IsAmbiguousTime(localAt.DateTime) && !zone.IsAmbiguousTime(localNext.DateTime);
                if (safe) segment ??= at;
                else if (segment is { } from)
                {
                    collected.AddRange(await Read(plantId, from, at, ct));
                    segment = null;
                }
            }
            return collected.ToArray();
        }
        try
        {
            var raw = await Send(c => sungrow.Minutes(keys.AppKey, keys.AccessKey, token, plant.ProviderId!, mapping.Points,
                start, queryEnd, zone, c, step), ct);
            return SungrowNormalization.Map(raw, plant.ProviderId!, mapping, zone, start, end, summary =>
                logger.Log(summary.Intervals == 0 ? LogLevel.Warning : LogLevel.Information,
                    "Sungrow-Minutenimport {Start:O} bis {End:O}, Punkte {Points}, Schritt {Step}: " +
                    "Antwort {Payload}, Anlagenreihe {Series}, Wörterbuch {Dictionary}, Rohzeilen {RawRows}, " +
                    "gültige Zeitstempel {Timestamps}, Leistungspunkte mit W/kW {Units}, verwertbare Intervalle {Intervals}, " +
                    "weitere nichtleere Arrays {OtherArrays}, weitere Objekte {OtherObjects}",
                    localStart, localEnd, string.Join(",", mapping.Points), step, summary.Payload, summary.PlantSeries,
                    summary.PointDictionary, summary.RawSamples, summary.ValidTimestamps, summary.PowerPointsWithKnownUnit,
                    summary.Intervals, summary.OtherNonemptyArrays, summary.OtherObjects));
        }
        catch (ProviderFailure failure)
        {
            logger.LogWarning("Sungrow-Minutenimport {Start:O} bis {End:O}: {Category}, HTTP {HttpStatus}, Sungrow-Code {ResultCode}",
                localStart, localEnd, failure.Category, failure.HttpStatus, failure.ResultCode);
            throw;
        }
    }

    public async Task<object> Probe(Plant plant, DateOnly day, CancellationToken ct)
    {
        if (plant.ProviderId is not { } id) throw new ArgumentException("Keine Sungrow-Anlage ausgewählt.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        if (day >= Calendar.Day(DateTimeOffset.UtcNow, zone))
            throw new ArgumentException("Einen abgeschlossenen Tag auswählen.");
        var start = Calendar.Utc(day, zone).AddHours(11);
        var (keys, token) = await Access(ct);
        int[] minutePoints = [83067, 83033, 83052, 83106, 83549, 83238, 83129];
        int[] dayPoints = [83022, 83118, 83072, 83102, 83322, 83323];
        var minute = await Send(c => sungrow.Minutes(keys.AppKey, keys.AccessKey, token, id,
            minutePoints, start, start.AddHours(2), zone, c, 15), ct);
        var daily = await Send(c => sungrow.Days(keys.AppKey, keys.AccessKey, token, id, dayPoints, day, day, c), ct);
        JsonElement Series(JsonElement data) =>
            data.ValueKind == JsonValueKind.Object && data.TryGetProperty(id, out var series) ? series : default;
        static bool HasNumber(JsonElement value) => decimal.TryParse(value.ToString(),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
        var minuteSeries = Series(minute);
        var daySeries = Series(daily);
        return new
        {
            minutes = minutePoints.Select(point => new { point, samples = minuteSeries.ValueKind == JsonValueKind.Array
                ? minuteSeries.EnumerateArray().Count(row => row.ValueKind == JsonValueKind.Object &&
                    row.TryGetProperty($"p{point}", out var value) && HasNumber(value)) : 0 }).ToArray(),
            normalizedDays = new AggregateMapping(83022, 83118, 83102, 83072, 83322, 83323)
                .Normalize(daily, plant.Id, id, zone, day, day).Select(row => new { row.Point, row.Kwh }).ToArray(),
            dayTimestamps = daySeries.ValueKind == JsonValueKind.Object
                ? daySeries.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array)
                    .SelectMany(p => p.Value.EnumerateArray()).Where(row => row.ValueKind == JsonValueKind.Object)
                    .Select(row => row.TryGetProperty("time_stamp", out var at) ? at.ToString() : "")
                    .Distinct().Take(5).ToArray() : [],
            days = dayPoints.Select(point => new { point, samples = daySeries.ValueKind == JsonValueKind.Object &&
                daySeries.TryGetProperty($"p{point}", out var values) && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray().Count(row => row.ValueKind == JsonValueKind.Object &&
                    row.TryGetProperty("2", out var value) && HasNumber(value)) : 0 }).ToArray()
        };
    }

    public async Task<Aggregate[]> ReadAggregates(Guid plantId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
        => await ReadAggregates(plantId, start, end, start, end, ct);

    public async Task<Aggregate[]> ReadAggregates(Guid plantId, DateTimeOffset jobStart, DateTimeOffset jobEnd,
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Store>();
        var plant = await db.Plants.AsNoTracking().SingleAsync(p => p.Id == plantId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        const int batchDays = 100;
        var first = Calendar.Day(jobStart, zone);
        if (Calendar.Utc(first, zone) < jobStart) first = first.AddDays(1);
        int elapsed = Math.Max(0, Calendar.Day(start, zone).DayNumber - first.DayNumber);
        var block = first.AddDays(elapsed / batchDays * batchDays);
        if (Calendar.Utc(block, zone) < start) block = block.AddDays(batchDays);
        if (Calendar.Utc(block, zone) >= end) return [];
        first = block;
        var last = Calendar.Day(jobEnd, zone).AddDays(-1);
        if (last.DayNumber - first.DayNumber >= batchDays) last = first.AddDays(batchDays - 1);
        if (last < first) return [];
        var metadata = await db.Connections.AsNoTracking().SingleAsync(c => c.PlantId == plantId, ct);
        var mapping = JsonSerializer.Deserialize<SungrowMapping>(metadata.Capabilities)?.Daily;
        if (mapping is null || mapping.Points.Length == 0) return [];
        var (keys, token) = await Access(ct);
        var raw = await Send(c => sungrow.Days(keys.AppKey, keys.AccessKey, token, plant.ProviderId!, mapping.Points,
            first, last, c), ct);
        return mapping.Normalize(raw, plant.Id, plant.ProviderId!, zone, first, last);
    }
}

