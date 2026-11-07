using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SolarInsight.Domain;

namespace SolarInsight.Infrastructure;

// Read-only transport for the published EU OAuth contract. Historical payloads stay raw
// until a real plant establishes the signs, units, timestamp semantics and channel mapping.
public sealed class SungrowDiscovery(HttpClient http)
{
    static readonly Uri Gateway = new("https://gateway.isolarcloud.eu/");
    public const string TokenPath = "openapi/apiManage/token";
    public const string RefreshPath = "openapi/apiManage/refreshToken";
    public const string MinutesPath = "openapi/platform/getPowerStationPointMinuteDataList";

    public Task<JsonElement> Exchange(string appKey, string accessKey, string code, Uri registeredRedirect,
        CancellationToken ct)
    {
        if (!registeredRedirect.IsAbsoluteUri || registeredRedirect.Scheme != "https" ||
            registeredRedirect.UserInfo.Length > 0 || registeredRedirect.Fragment.Length > 0)
            throw new ArgumentException("Die registrierte Rücksprungadresse muss eine HTTPS-Adresse sein.");
        return Post(TokenPath, accessKey, null, new { appkey = appKey, code,
            grant_type = "authorization_code", redirect_uri = registeredRedirect.AbsoluteUri,
            token_scope_mode = "authorization_bound" }, ct);
    }

    public Task<JsonElement> Refresh(string appKey, string accessKey, string refreshToken, CancellationToken ct) =>
        Post(RefreshPath, accessKey, null, new { appkey = appKey, refresh_token = refreshToken }, ct);

    public Task<JsonElement> Plants(string appKey, string accessKey, string token, int page, CancellationToken ct) =>
        Post("openapi/platform/queryPowerStationList", accessKey, token,
            new { appkey = appKey, page, size = 10 }, ct);

    public Task<JsonElement> Devices(string appKey, string accessKey, string token, string plant, int page,
        CancellationToken ct) => Post("openapi/platform/getDeviceListByPsId", accessKey, token,
            new { appkey = appKey, ps_id = plant, page, size = 10 }, ct);

    public Task<JsonElement> Days(string appKey, string accessKey, string token, string plant, int[] points,
        DateOnly first, DateOnly lastInclusive, CancellationToken ct)
    {
        if (lastInclusive < first || lastInclusive.DayNumber - first.DayNumber >= 100)
            throw new ArgumentException("Die Tagesabfrage erlaubt höchstens 100 Kalendertage.");
        return Post("openapi/platform/getPowerStationPointDayMonthYearDataList", accessKey, token,
            new { appkey = appKey, ps_id_list = new[] { plant }, query_type = "day", data_type = "2",
                data_point = Points(points), start_time = first.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                end_time = lastInclusive.ToString("yyyyMMdd", CultureInfo.InvariantCulture), order = 0,
                is_get_point_dict = "1" }, ct);
    }

    public Task<JsonElement> Minutes(string appKey, string accessKey, string token, string plant,
        int[] points, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken ct, int stepMinutes = 5)
    {
        var first = TimeZoneInfo.ConvertTime(start, zone);
        var last = TimeZoneInfo.ConvertTime(end, zone);
        if (stepMinutes is not (5 or 15) || end <= start || end - start > TimeSpan.FromHours(3) || last.DateTime <= first.DateTime ||
            last.DateTime - first.DateTime > TimeSpan.FromHours(3))
            throw new ArgumentException("Ungültiges Abfragefenster für die Sungrow-Minutenhistorie.");
        const string format = "yyyyMMddHHmmss";
        return Post(MinutesPath, accessKey, token, new { appkey = appKey, ps_id_list = new[] { plant },
            points = Points(points), start_time_stamp = first.ToString(format, CultureInfo.InvariantCulture),
            end_time_stamp = last.ToString(format, CultureInfo.InvariantCulture), minute_interval = stepMinutes.ToString(CultureInfo.InvariantCulture),
            is_get_point_dict = "1" }, ct);
    }

    static string Points(int[] points)
    {
        if (points.Length is < 1 or > 50 || points.Any(p => p <= 0))
            throw new ArgumentException("Ungültige Messpunktauswahl.");
        return string.Join(',', points.Select(p => "p" + p.ToString(CultureInfo.InvariantCulture)));
    }

    async Task<JsonElement> Post(string path, string accessKey, string? accessToken, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Gateway, path));
        request.Headers.Add("x-access-key", accessKey);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ProviderFailure("auth", false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var header = response.Headers.RetryAfter;
            TimeSpan? wait = header?.Delta;
            if (wait is null && header?.Date is { } date) wait = date - DateTimeOffset.UtcNow;
            throw new ProviderFailure("rate-limit", true, wait < TimeSpan.Zero ? TimeSpan.Zero : wait);
        }
        if (!response.IsSuccessStatusCode)
            throw new ProviderFailure("provider-http", (int)response.StatusCode >= 500);
        try
        {
            using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!result.RootElement.TryGetProperty("result_code", out var status) || status.ToString() != "1")
                throw new ProviderFailure(path is TokenPath or RefreshPath ? "auth" : "provider-result", false);
            return result.RootElement.GetProperty("result_data").Clone();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ProviderFailure("provider-payload", false);
        }
    }
}

