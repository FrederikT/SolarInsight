using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public record AppKeys(string AppKey, string AccessKey, string Redirect);
public record LiveTokens(string Access, string Refresh, DateTimeOffset Expires);
public record AuthorizedPlant(string Id, string Name);
public record LiveActivation(string Plant, string Zone, SungrowMapping Mapping, string? Name = null);

public sealed class LiveProvider(IServiceScopeFactory scopes, IDataProtectionProvider protection,
    SungrowDiscovery sungrow, IConfiguration configuration, RuntimeOptions runtime) : IReadProvider, IAggregateProvider
{
    readonly IDataProtector protector = protection.CreateProtector("sungrow-tokens-v1");
    readonly SemaphoreSlim authLock = new(1, 1);
    readonly SemaphoreSlim apiLock = new(1, 1);
    DateTimeOffset nextCall;
    public TimeSpan Window => TimeSpan.FromMinutes(165);
    public bool Configured => !runtime.Demo && configuration["Sungrow:AppFile"] is { } file && File.Exists(file);

    public int CallSpacingSeconds => Math.Clamp(configuration.GetValue("Sungrow:CallSpacingSeconds", 45), 1, 86400);
    public int RefreshMinutes => Math.Clamp(configuration.GetValue("Sungrow:RefreshMinutes", 1440), 60, 10080);

    AppKeys Keys()
    {
        if (!Configured) throw new ArgumentException("Die serverseitige App-Datei der freigegebenen Sungrow-Anwendung fehlt.");
        try
        {
            var keys = JsonSerializer.Deserialize<AppKeys>(File.ReadAllText(configuration["Sungrow:AppFile"]!),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (keys is null || string.IsNullOrWhiteSpace(keys.AppKey) || string.IsNullOrWhiteSpace(keys.AccessKey) ||
                !Uri.TryCreate(keys.Redirect, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new ArgumentException("Ungültige serverseitige Sungrow-App-Konfiguration.");
            return keys;
        }
        catch (JsonException)
        {
            throw new ArgumentException("Ungültige serverseitige Sungrow-App-Konfiguration.");
        }
    }

    async Task<JsonElement> Send(Func<CancellationToken, Task<JsonElement>> call, CancellationToken ct)
    {
        await apiLock.WaitAsync(ct);
        try
        {
            var wait = nextCall - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            nextCall = DateTimeOffset.UtcNow.AddSeconds(CallSpacingSeconds);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            try { return await call(timeout.Token); }
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

    async Task<(AppKeys Keys, string Token)> Access(CancellationToken ct)
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
                var raw = await Send(c => sungrow.Refresh(keys.AppKey, keys.AccessKey, tokens.Refresh, c), ct);
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
        if (input.Plant is null || input.Plant.Length is < 1 or > 30 || !input.Plant.All(char.IsAsciiDigit) ||
            input.Zone is null || !input.Zone.Contains('/') || !TimeZoneInfo.TryFindSystemTimeZoneById(input.Zone, out _))
            throw new ArgumentException("Anlage oder Zeitzone ungültig.");
        if (input.Mapping is null) throw new ArgumentException("Messpunktzuordnung fehlt.");
        input.Mapping.Validate();
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
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(702928349)", ct);
        var plant = await db.Plants.SingleOrDefaultAsync(p => p.ProviderId == input.Plant, ct);
        var settings = JsonSerializer.Serialize(input.Mapping);
        if (plant is not null)
        {
            var previous = await db.Connections.FindAsync([plant.Id], ct);
            bool data = await db.Readings.AnyAsync(r => r.PlantId == plant.Id, ct) ||
                await db.Aggregates.AnyAsync(a => a.PlantId == plant.Id, ct);
            if (data && (previous?.Capabilities != settings || plant.Zone != input.Zone))
                throw new ArgumentException("Eine Änderung der Messpunktzuordnung oder Zeitzone erfordert eine separate Datenmigration.");
        }
        await db.Plants.Where(p => p.Active).ExecuteUpdateAsync(s => s.SetProperty(p => p.Active, false), ct);
        if (plant is null)
        {
            plant = new Plant { Id = Guid.NewGuid(), Name = input.Name is { Length: > 0 and <= 100 } ? input.Name : "Sungrow-Anlage",
                ProviderId = input.Plant, Zone = input.Zone,
                Active = true, Revision = 1 };
            db.Plants.Add(plant);
        }
        else
        {
            plant.Active = true;
            db.Entry(plant).Property(p => p.Active).IsModified = true;
            plant.Zone = input.Zone;
            plant.Revision++;
        }
        var connection = await db.Connections.FindAsync([plant.Id], ct);
        var metadata = new ProviderConnection(plant.Id, "mapped", "", settings);
        if (connection is null) db.Connections.Add(metadata);
        else db.Entry(connection).CurrentValues.SetValues(metadata);
        foreach (var (id, info) in devices)
        {
            var existing = await db.Devices.FindAsync([plant.Id, id], ct);
            var metadataDevice = new Device(plant.Id, id, info.Model, info.Type);
            if (existing is null) db.Devices.Add(metadataDevice);
            else db.Entry(existing).CurrentValues.SetValues(metadataDevice);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
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
        var raw = await Send(c => sungrow.Minutes(keys.AppKey, keys.AccessKey, token, plant.ProviderId!, mapping.Points,
            start, queryEnd, zone, c, step), ct);
        return SungrowNormalization.Map(raw, plant.ProviderId!, mapping, zone, start, end);
    }

    public async Task<Aggregate[]> ReadAggregates(Guid plantId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Store>();
        var plant = await db.Plants.AsNoTracking().SingleAsync(p => p.Id == plantId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        var first = Calendar.Day(start, zone);
        var last = Calendar.Day(end, zone).AddDays(-1);
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

