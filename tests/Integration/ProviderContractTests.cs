using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;
using Xunit;

namespace SolarInsight.Tests;

// Synthetic contract fixtures. These are independent expected examples, never recorded plant responses.
public sealed class ProviderContractTests
{
    [Fact]
    public void MinutesReportsUnusableSamplesWithoutInventingIntervals()
    {
        var mapping = new SungrowMapping(83067, null, null, null, null, true, true, true, false, true, false);
        using var json = JsonDocument.Parse("""
            {"fixture":[{"time_stamp":"20260101120000"},{"time_stamp":"20260101120500"}],
             "point_dict":[{"point_id":"p83067","point_unit":"W"}]}
            """);
        var start = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero);
        MinuteDiagnostics? report = null;
        Assert.Empty(SungrowNormalization.Map(json.RootElement, "fixture", mapping, Zone, start,
            start.AddHours(1), value => report = value));
        Assert.Equal(new MinuteDiagnostics(JsonValueKind.Object, JsonValueKind.Array, JsonValueKind.Array, 2, 2, 1, 0),
            report);
    }

    static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    static readonly DateTimeOffset Start = Calendar.Utc(new DateOnly(2026, 1, 1), Zone);

    [Fact]
    public void CounterDeltasRequireAdjacentPlausibleReadingsAndPreserveMeasuredZero()
    {
        var step = TimeSpan.FromMinutes(15);
        var pv = new PointObservation(13134, "PV", "Wh", true, 8, "available",
            [new(Start, 1000), new(Start + step, 1200), new(Start + step * 2, 1200),
             new(Start + step * 4, 1400), new(Start + step * 5, 50),
             new(Start + step * 6, 10500), new(Start + step * 7, 10600)]);
        var power = new PointObservation(13003, "DC", "W", true, 7, "available",
            Enumerable.Range(0, 8).Select(i => new ProbeValue(Start + step * i, 400)).ToArray());
        var report = new PointProbeResult("Gerät", Start.AddDays(1), [pv, power]);
        var rows = DeviceCounterHistory.Intervals(report, Start, Start + step * 7, step);
        Assert.Equal(new[] { Start, Start + step, Start + step * 6 }, rows.Select(row => row.Start));
        Assert.Equal(new decimal?[] { .2m, 0m, .1m }, rows.Select(row => row.Pv));
        Assert.All(rows, row => Assert.Null(row.Load));
        var withoutTelemetry = report with { Points = [pv] };
        Assert.Empty(DeviceCounterHistory.Intervals(withoutTelemetry, Start, Start + step * 7, step));
    }

    [Fact]
    public void InverterValuesStaySeparateFromTheAcBalance()
    {
        var mapping = new LiveMapping("42_14_1_1", null, null, null, null, null, true, true, false, true,
            PvDc: 13003, InverterActive: 13011);
        using var payload = JsonDocument.Parse("""
            {"device_point_list":[{"ps_id":42,"ps_key":"42_14_1_1","device_time":"20260101000000",
              "p13003":840,"p13011":-160}],"point_dict":[{"point_id":13003,"point_unit":"W"},
              {"point_id":13011,"point_unit":"W"}]}
            """);
        var batch = LiveNormalization.Map(payload.RootElement, "42", mapping, Zone, Start.AddMinutes(1));
        Assert.Equal(.84m, batch.Values["pvDc"]);
        Assert.Equal(-.16m, batch.Values["inverterActive"]);
        Assert.DoesNotContain("pv", batch.Values.Keys);
    }

    [Fact]
    public void CandidateProbeKeepsZeroAndSeparatesMissingUnknownUnitAndDeniedAccess()
    {
        ProbePoint[] points = [new(13141, "SOC", "soc"), new(13003, "DC", "power"), new(13011, "AC", "power")];
        using var payload = JsonDocument.Parse("""
            {"device_point_list":[{"device_point":{"ps_id":"42","ps_key":"42_14_1_1",
              "device_time":"20260101000000","p13141":"0","p13003":"1500"}}],
              "point_dict":[{"point_id":13141,"point_unit":"%"},{"point_id":13003,"point_unit":"?"}]}
            """);
        var result = PointProbe.DeviceNow(payload.RootElement, "42", "42_14_1_1", points, Zone, Start.AddMinutes(1));
        Assert.Equal(Start.AddMinutes(1), result.FetchedAt);
        Assert.Collection(result.Points, soc =>
        {
            Assert.Equal("available", soc.Status);
            Assert.Equal(1, soc.ValidCount);
            Assert.Equal(new ProbeValue(Start, 0), Assert.Single(soc.Values));
        }, dc =>
        {
            Assert.Equal("unknown-unit", dc.Status);
            Assert.Equal(0, dc.ValidCount);
            Assert.Equal("?", dc.Unit);
            Assert.Equal(1500, Assert.Single(dc.Values).Value);
        }, ac =>
        {
            Assert.Equal("missing", ac.Status);
            Assert.False(ac.Delivered);
        });
        var denied = PointProbe.Failed(result.Source, points, result.FetchedAt,
            new ProviderFailure("provider-result", false) { HttpStatus = 200, ResultCode = "E900" });
        Assert.Equal("authorization", denied.Failure);
        Assert.Equal("E900", denied.ResultCode);
        Assert.All(denied.Points, point => Assert.Equal("not-evaluated", point.Status));
    }

    [Fact]
    public async Task DeviceHistoryUsesTheDocumentedEuRouteAndOnlyTheRequestedDevice()
    {
        const string key = "42_14_1_1";
        using var http = new HttpClient(new Stub(async request =>
        {
            Assert.Equal("https://gateway.isolarcloud.eu/openapi/platform/getDevicePointMinuteDataList",
                request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var data = body.RootElement;
            Assert.Equal(key, data.GetProperty("ps_key_list")[0].GetString());
            Assert.Equal("p13119", data.GetProperty("points").GetString());
            Assert.Equal("15", data.GetProperty("minute_interval").GetString());
            Assert.Equal("20260101000000", data.GetProperty("start_time_stamp").GetString());
            Assert.Equal("20260101001500", data.GetProperty("end_time_stamp").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"result_code":"1","result_data":{
                  "42_14_1_1":[{"time_stamp":"20260101000000","p13119":"0"}],
                  "42_14_1_2":[{"time_stamp":"20260101000000","p13119":"1500"}],
                  "point_dict":[{"point_id":13119,"point_unit":"W"}]}}
                """) };
        }));
        var end = Start.AddMinutes(15);
        var payload = await new SungrowDiscovery(http).DeviceMinutes("app", "key", "token", key, [13119],
            Start, end, Zone, default, 15);
        var result = PointProbe.Minutes("Gerätehistorie", payload, key, [new(13119, "Last", "power")],
            Zone, Start, end, end.AddDays(1));
        Assert.Equal(new ProbeValue(Start, 0), Assert.Single(Assert.Single(result.Points).Values));
    }

    [Fact]
    public void MinutesDiagnoseMissingPlantSeriesWithoutUsingOtherRows()
    {
        using var json = JsonDocument.Parse("""
            {"another-plant":[{"time_stamp":"20260101000000","p83067":"4000"}],
             "other-wrapper":{},"point_dict":[{"point_id":"p83067","point_unit":"W"}]}
            """);
        var mapping = new SungrowMapping(83067, null, null, null, null, true, true, true, false, true, false);
        MinuteDiagnostics? report = null;
        Assert.Empty(SungrowNormalization.Map(json.RootElement, "fixture", mapping, Zone, Start,
            Start.AddHours(1), value => report = value));
        Assert.Equal(new MinuteDiagnostics(JsonValueKind.Object, JsonValueKind.Undefined, JsonValueKind.Array,
            0, 0, 1, 0, OtherNonemptyArrays: 1, OtherObjects: 1), report);
    }

    [Fact]
    public async Task OfficialTokenContractUsesEuPostAndReturnsNoFabricatedSuccess()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var transport = new Stub(async request =>
        {
            sent = request;
            body = await request.Content!.ReadAsStringAsync();
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"result_code":"1","result_data":{"access_token":"fixture-only","refresh_token":"fixture-refresh","expires_in":3600}}
                """) };
        });
        var client = new SungrowDiscovery(new HttpClient(transport));
        var result = await client.Exchange("fixture-app", "fixture-key", "fixture-code", new("https://example.org/callback"), default);
        Assert.Equal("fixture-only", result.GetProperty("access_token").GetString());
        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("https://gateway.isolarcloud.eu/openapi/apiManage/token", sent.RequestUri!.AbsoluteUri);
        Assert.Contains("authorization_bound", body);
        Assert.Contains("authorization_code", body);
        Assert.Equal("fixture-key", sent.Headers.GetValues("x-access-key").Single());
    }

    [Theory]
    [InlineData(401, "auth", false)]
    [InlineData(403, "auth", false)]
    [InlineData(429, "rate-limit", true)]
    [InlineData(500, "provider-http", true)]
    public async Task OfficialTransportClassifiesErrors(int code, string category, bool retry)
    {
        var transport = new Stub(_ =>
        {
            var result = new HttpResponseMessage((HttpStatusCode)code);
            result.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(8));
            return Task.FromResult(result);
        });
        var client = new SungrowDiscovery(new HttpClient(transport));
        var error = await Assert.ThrowsAsync<ProviderFailure>(() => client.Plants("app", "key", "token", 1, default));
        Assert.Equal(category, error.Category);
        Assert.Equal(retry, error.Retryable);
        if (code == 429) Assert.Equal(TimeSpan.FromSeconds(8), error.RetryAfter);
    }

    [Fact]
    public void MinutesNormalizeExplicitUnitsSignsAndGaps()
    {
        using var json = JsonDocument.Parse("""
            {"fixture":[
              {"time_stamp":"20260101000000","p83067":"4000","p83106":"2000","p83549":"-1000","p83129":"50"},
              {"time_stamp":"20260101000500","p83067":"2000","p83106":"2000","p83549":"-1000","p83129":"51"},
              {"time_stamp":"20260101002000","p83067":"2000","p83106":"2000","p83549":"-1000","p83129":"52"}
            ],"point_dict":[
              {"point_id":"83067","point_unit":"W"},{"point_id":"83106","point_unit":"W"},
              {"point_id":"83549","point_unit":"W"}]}
            """);
        var mapping = new SungrowMapping(83067, 83106, 83549, null, 83129, true, true, true, false, true, true);
        var rows = SungrowNormalization.Map(json.RootElement, "fixture", mapping, Zone, Start, Start.AddMinutes(30));
        Assert.Single(rows);
        Assert.Equal(.25m, rows[0].Pv!.Value, 20);
        Assert.Equal(1m / 6, rows[0].Load!.Value, 20);
        Assert.Equal(0, rows[0].Import);
        Assert.Equal(1m / 12, rows[0].Export!.Value, 20);
        Assert.Equal(.51m, rows[0].Soc);
        Assert.Equal(.5m, rows[0].OpeningSoc);
        Assert.Null(rows[0].Charge);
    }

    [Fact]
    public void FifteenMinuteAveragesStayOnTheirOriginalGrid()
    {
        using var json = JsonDocument.Parse("""
            {"fixture":[{"time_stamp":"20260101001500","p83067":"4000"}],
             "point_dict":[{"point_id":83067,"point_unit":"W"}]}
            """);
        var mapping = new SungrowMapping(83067, null, null, null, null, true, true, false, true,
            false, false, StepMinutes: 15);
        var row = Assert.Single(SungrowNormalization.Map(json.RootElement, "fixture", mapping, Zone, Start,
            Start.AddMinutes(15)));
        Assert.Equal(Start, row.Start);
        Assert.Equal(Start.AddMinutes(15), row.End);
        Assert.Equal(1, row.Pv);
        Assert.Equal("mean-power", row.Quality);
    }

    [Fact]
    public void UnknownUnitsNeverBecomeMeasuredEnergy()
    {
        using var json = JsonDocument.Parse("""
            {"fixture":[{"time_stamp":"20260101000000","p83067":"4"}],"point_dict":[{"point_id":"83067","point_unit":"V"}]}
            """);
        var mapping = new SungrowMapping(83067, null, null, null, null, true, true, false, false, false, false);
        Assert.Throws<ProviderFailure>(() => SungrowNormalization.Map(json.RootElement, "fixture", mapping, Zone, Start,
            Start.AddMinutes(5)));
    }

    [Fact]
    public void DailyEnergyIsConvertedAndKeptAsAggregate()
    {
        using var json = JsonDocument.Parse("""
            {"fixture":{"p83118":[{"2":"12000","time_stamp":"20260101"}]},
             "point_dict":[{"point_id":83118,"point_unit":"Wh"}]}
            """);
        var mapping = new AggregateMapping(null, 83118, null, null, null, null);
        var rows = mapping.Normalize(json.RootElement, Guid.NewGuid(), "fixture", Zone,
            new(2026, 1, 1), new(2026, 1, 1));
        Assert.Single(rows);
        Assert.Equal(12, rows[0].Kwh);
        Assert.Equal("provider", rows[0].Origin);
        Assert.Equal(24, (rows[0].End - rows[0].Start).TotalHours);
    }

    [Theory]
    [InlineData(200, "2", "2")]
    [InlineData(400, "5", "5")]
    [InlineData(401, "E914", "E914")]
    [InlineData(403, "123456", null)]
    public async Task TokenFailureExposesOnlySafeDiagnostics(int http, string code, string? expected)
    {
        using var client = new HttpClient(new Stub(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)http)
        {
            Content = new StringContent($"{{\"result_code\":\"{code}\",\"result_msg\":\"private-response\"}}")
        })));
        var sungrow = new SungrowDiscovery(client);
        var error = await Assert.ThrowsAsync<ProviderFailure>(() =>
            sungrow.Exchange("app", "key", "secret-code", new Uri("http://127.0.0.1:8081/"), default));
        Assert.Equal(http, error.HttpStatus);
        Assert.Equal(expected, error.ResultCode);
        Assert.DoesNotContain("private-response", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteTokenPayloadWithoutResultCodeCanBeUsed(bool wrapped)
    {
        const string tokens = """
            {"access_token":"fixture-access","refresh_token":"fixture-refresh","expires_in":3600}
            """;
        using var client = new HttpClient(new Stub(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(wrapped ? "{\"result_data\":" + tokens + "}" : tokens)
        })));
        var result = await new SungrowDiscovery(client).Exchange("app", "key", "code", new Uri("http://127.0.0.1:8081/"), default);
        Assert.Equal("fixture-access", result.GetProperty("access_token").GetString());
    }

    [Fact]
    public async Task ExplicitFailureIsPreservedEvenWithTokenFields()
    {
        using var client = new HttpClient(new Stub(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"result_code":"2","result_data":{"access_token":"fixture-access","refresh_token":"fixture-refresh","expires_in":3600}}
                """)
        })));
        var error = await Assert.ThrowsAsync<ProviderFailure>(() =>
            new SungrowDiscovery(client).Exchange("app", "key", "code", new Uri("http://127.0.0.1:8081/"), default));
        Assert.Equal("2", error.ResultCode);
        Assert.Contains("access_token=True", error.ResponseShape);
        Assert.DoesNotContain("fixture-access", error.ResponseShape);
    }

    sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            handler(request);
    }
}

