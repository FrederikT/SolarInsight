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
    public const string DeviceMinutesPath = "openapi/platform/getDevicePointMinuteDataList";

    public static bool IsAllowedRedirect(Uri uri) => uri.IsAbsoluteUri &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Scheme == Uri.UriSchemeHttps ||
         (uri.Scheme == Uri.UriSchemeHttp && uri.Host == "127.0.0.1"));

    public Task<JsonElement> Exchange(string appKey, string accessKey, string code, Uri registeredRedirect,
        CancellationToken ct)
    {
        if (!IsAllowedRedirect(registeredRedirect))
            throw new ArgumentException("Die Rücksprungadresse muss HTTPS oder HTTP auf 127.0.0.1 verwenden.");
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

    public Task<JsonElement> DeviceNow(string appKey, string accessKey, string token, LiveMapping mapping,
        CancellationToken ct) => DeviceNow(appKey, accessKey, token, mapping.DeviceKey, mapping.Points, ct);

    public Task<JsonElement> DeviceNow(string appKey, string accessKey, string token, string deviceKey,
        IEnumerable<int> points, CancellationToken ct)
    {
        var keyParts = deviceKey.Split('_');
        if (keyParts.Length < 2 || !int.TryParse(keyParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int deviceType) ||
            deviceType <= 0)
            throw new ArgumentException("Der Sungrow-Geräteschlüssel enthält keinen gültigen Gerätetyp.");
        return Post("openapi/platform/getDeviceRealTimeData", accessKey, token, new
        {
            appkey = appKey, ps_key_list = new[] { deviceKey },
            device_type = deviceType.ToString(CultureInfo.InvariantCulture),
            point_id_list = points.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToArray(),
            is_get_point_dict = "1"
        }, ct);
    }

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
        var (first, last, step) = MinuteWindow(start, end, zone, stepMinutes);
        return Post(MinutesPath, accessKey, token, new { appkey = appKey, ps_id_list = new[] { plant },
            points = Points(points), start_time_stamp = first, end_time_stamp = last, minute_interval = step,
            is_get_point_dict = "1" }, ct);
    }

    public Task<JsonElement> DeviceMinutes(string appKey, string accessKey, string token, string device,
        int[] points, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken ct, int stepMinutes)
    {
        var (first, last, step) = MinuteWindow(start, end, zone, stepMinutes);
        return Post(DeviceMinutesPath, accessKey, token, new { appkey = appKey, ps_key_list = new[] { device },
            points = Points(points), start_time_stamp = first, end_time_stamp = last, minute_interval = step,
            is_get_point_dict = "1" }, ct);
    }

    static (string First, string Last, string Step) MinuteWindow(DateTimeOffset start, DateTimeOffset end,
        TimeZoneInfo zone, int stepMinutes)
    {
        var first = TimeZoneInfo.ConvertTime(start, zone);
        var last = TimeZoneInfo.ConvertTime(end, zone);
        if (stepMinutes is not (5 or 15) || end <= start || end - start > TimeSpan.FromHours(3) || last.DateTime <= first.DateTime ||
            last.DateTime - first.DateTime > TimeSpan.FromHours(3))
            throw new ArgumentException("Ungültiges Abfragefenster für die Sungrow-Minutenhistorie.");
        const string format = "yyyyMMddHHmmss";
        return (first.ToString(format, CultureInfo.InvariantCulture), last.ToString(format, CultureInfo.InvariantCulture),
            stepMinutes.ToString(CultureInfo.InvariantCulture));
    }

    static string Points(int[] points)
    {
        if (points.Length is < 1 or > 50 || points.Any(p => p <= 0))
            throw new ArgumentException("Ungültige Messpunktauswahl.");
        return string.Join(',', points.Select(p => "p" + p.ToString(CultureInfo.InvariantCulture)));
    }

    static string? SafeResultCode(string? code)
    {
        if (code is null) return null;
        if (code == "-1" || (code.Length is >= 1 and <= 3 && code.All(char.IsAsciiDigit)))
            return code;
        return code.Length is >= 4 and <= 6 && code[0] == 'E' && code.Skip(1).All(char.IsAsciiDigit)
            ? code : null;
    }

    static bool HasString(JsonElement payload, string key) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString());

    static bool HasField(JsonElement payload, string key) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(key, out _);

    static bool HasTokenPayload(JsonElement payload) =>
        HasString(payload, "access_token") && HasString(payload, "refresh_token") &&
        payload.TryGetProperty("expires_in", out var expiry) &&
        expiry.ValueKind is JsonValueKind.String or JsonValueKind.Number &&
        int.TryParse(expiry.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) &&
        seconds is > 0 and <= 2_678_400;

    static string TokenShape(JsonElement root, JsonElement data, JsonElement status, string? code)
    {
        static string Fields(JsonElement payload) =>
            $"access_token={HasField(payload, "access_token")}, refresh_token={HasField(payload, "refresh_token")}, expires_in={HasField(payload, "expires_in")}";

        return $"result_code={status.ValueKind}, Länge={code?.Length ?? 0}; " +
            $"root({Fields(root)}); result_data={data.ValueKind}({Fields(data)}); " +
            $"error={HasField(root, "error")}, result_msg={HasField(root, "result_msg")}";
    }

    async Task<JsonElement> Post(string path, string accessKey, string? accessToken, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Gateway, path));
        request.Headers.Add("x-access-key", accessKey);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        int httpStatus = (int)response.StatusCode;
        bool denied = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
        bool tokenRequest = path is TokenPath or RefreshPath;
        ProviderFailure Failure(string category, bool retryable, string? resultCode = null, string? shape = null) =>
            new(category, retryable) { HttpStatus = httpStatus, ResultCode = resultCode, ResponseShape = shape };
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var header = response.Headers.RetryAfter;
            TimeSpan? wait = header?.Delta;
            if (wait is null && header?.Date is { } date) wait = date - DateTimeOffset.UtcNow;
            throw new ProviderFailure("rate-limit", true, wait < TimeSpan.Zero ? TimeSpan.Zero : wait) { HttpStatus = httpStatus };
        }
        if (!response.IsSuccessStatusCode && !denied && !tokenRequest)
            throw Failure("provider-http", httpStatus >= 500);
        try
        {
            using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = result.RootElement;
            string? code = root.TryGetProperty("result_code", out var status)
                && status.ValueKind is JsonValueKind.String or JsonValueKind.Number ? status.ToString() : null;
            root.TryGetProperty("result_data", out var data);
            string? shape = tokenRequest ? TokenShape(root, data, status, code) : null;
            if (!response.IsSuccessStatusCode && !denied)
                throw Failure("provider-http", httpStatus >= 500, SafeResultCode(code), shape);
            if (!denied && tokenRequest && (code == "1" || status.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                && !HasField(root, "error") && !HasField(data, "error"))
            {
                if (HasTokenPayload(data)) return data.Clone();
                if (HasTokenPayload(root)) return root.Clone();
                if (code == "1") throw Failure("provider-payload", false, SafeResultCode(code), shape);
            }
            if (denied || code != "1" || tokenRequest)
                throw Failure(denied || tokenRequest ? "auth" : "provider-result", false, SafeResultCode(code), shape);
            return root.GetProperty("result_data").Clone();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Failure(denied ? "auth" : response.IsSuccessStatusCode ? "provider-payload" : "provider-http", httpStatus >= 500);
        }
    }
}

