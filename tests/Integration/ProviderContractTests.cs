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
    static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    static readonly DateTimeOffset Start = Calendar.Utc(new DateOnly(2026, 1, 1), Zone);

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

    sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            handler(request);
    }
}

