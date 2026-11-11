using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using SolarInsight.Api;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;
using Xunit;

namespace SolarInsight.Tests;

public class LiveImportTests
{
    static readonly DateTimeOffset Start = new(2026, 10, 24, 23, 55, 0, TimeSpan.Zero);
    static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    static readonly LiveMapping Mapping = new("42_14_1_1", 1, 2, 3, 4, 5, false, true, true, true, true);

    static Store Open() => new(new DbContextOptionsBuilder<Store>().UseNpgsql(
        Environment.GetEnvironmentVariable("SOLAR_TEST_DB") ??
        throw new InvalidOperationException("SOLAR_TEST_DB fehlt.")).Options);

    [Fact]
    public void DeviceFixtureChecksUnitsSignsTimestampAndBalance()
    {
        using var doc = JsonDocument.Parse("""
            {
              "device_point_list":[{"device_point":{"ps_id":42,"ps_key":"42_14_1_1","device_time":"20261025015500",
                "p1":"4000","p2":"2","p3":"1000","p4":"1000","p5":"42"}}],
              "point_dict":[{"point_id":1,"point_unit":"W"},{"point_id":2,"point_unit":"kW"},
                {"point_id":3,"point_unit":"W"},{"point_id":4,"point_unit":"W"}]
            }
            """);
        var batch = LiveNormalization.Map(doc.RootElement, "42", Mapping, Zone, Start.AddMinutes(1));
        Assert.Equal(Start, batch.MeasuredAt);
        Assert.Equal(-1m, batch.Values["grid"]);
        Assert.Equal(.42m, batch.Values["soc"]);
        var rows = batch.Values.Select(pair => new LiveMeasurement(Guid.Empty, pair.Key, LiveNormalization.Source,
            Start, Start.AddMinutes(1), pair.Value, "confirmed")).ToArray();
        var poll = new LivePoll(Guid.Empty, Start.AddMinutes(1), Start, "confirmed", "pv,load,grid,battery,soc");
        var state = LiveStateView.Create(poll, rows, Mapping, Start.AddMinutes(2), TimeSpan.FromMinutes(10));
        Assert.True(state.FlowConfirmed);
        Assert.Equal("export", state.Grid.Direction);
        Assert.Equal(1m, state.PvSurplusKw);
        Assert.Null(LiveStateView.Create(poll, rows, Mapping with { BatteryExportExcluded = false },
            Start.AddMinutes(2), TimeSpan.FromMinutes(10)).PvSurplusKw);
        var stale = LiveStateView.Create(poll, rows, Mapping, Start.AddMinutes(11), TimeSpan.FromMinutes(10));
        Assert.Null(stale.Grid.Value);
        Assert.Equal("stale", stale.Grid.Quality);
        var unequal = rows.Select(r => r.Channel == "pv" ? r with { MeasuredAt = Start.AddMinutes(-1) } : r).ToArray();
        Assert.False(LiveStateView.Create(poll, unequal, Mapping, Start.AddMinutes(2), TimeSpan.FromMinutes(10)).FlowConfirmed);
        using var ambiguous = JsonDocument.Parse(doc.RootElement.GetRawText().Replace("20261025015500", "20261025025500"));
        Assert.Null(LiveNormalization.Map(ambiguous.RootElement, "42", Mapping, Zone, Start.AddHours(3)).MeasuredAt);
    }

    [Fact]
    public async Task HybridProbeRequestsBothDirectionsAndNormalizesOnlyCompletePairs()
    {
        var mapping = Mapping with { Pv = null, Load = 13119, Grid = 13149, GridExport = 13121,
            Battery = 13126, BatteryDischarge = 13150, Soc = 13141 };
        const string payload = """
            {"device_point_list":[{"ps_id":42,"ps_key":"42_14_1_1","device_time":"20261025015500",
                "p13119":"2200","p13149":0,"p13121":"900","p13126":"1100","p13150":0,"p13141":"42"}],
             "point_dict":[{"point_id":13119,"point_unit":"W"},{"point_id":13149,"point_unit":"W"},
                {"point_id":13121,"point_unit":"W"},{"point_id":13126,"point_unit":"W"},{"point_id":13150,"point_unit":"W"}]}
            """;
        using var http = new HttpClient(new Stub(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("14", body.RootElement.GetProperty("device_type").GetString());
            var ids = body.RootElement.GetProperty("point_id_list").EnumerateArray().Select(id => id.GetString());
            Assert.Equal(new[] { "13119", "13149", "13126", "13141", "13121", "13150" }, ids);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"result_code\":\"1\",\"result_data\":" + payload + "}") };
        }));
        var response = await new SungrowDiscovery(http).DeviceNow("fixture-app", "fixture-key", "fixture-token", mapping, default);
        var available = LiveNormalization.Map(response, "42", mapping, Zone, Start.AddMinutes(1));
        Assert.Equal(2.2m, available.Values["load"]);
        Assert.Equal(-.9m, available.Values["grid"]);
        Assert.Equal(1.1m, available.Values["battery"]);
        Assert.Equal(.42m, available.Values["soc"]);
        using var missing = JsonDocument.Parse(payload.Replace("\"p13121\":\"900\"", "\"p13121\":null"));
        Assert.DoesNotContain("grid", LiveNormalization.Map(missing.RootElement, "42", mapping, Zone, Start.AddMinutes(1)).Values.Keys);
    }

    [Fact]
    public void MultipleTabsLeaseIndependentlyAndExpire()
    {
        var activity = new OverviewActivity();
        var plant = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.False(activity.Visible(plant, Start));
        activity.Set(plant, first, true, Start);
        activity.Set(plant, second, true, Start.AddSeconds(30));
        activity.Set(plant, first, false, Start.AddSeconds(31));
        Assert.True(activity.Visible(plant, Start.AddSeconds(71)));
        Assert.False(activity.Visible(plant, Start.AddSeconds(101)));
    }

    [Fact]
    public void LanRequiresOptInSubnetAndSeparateKey()
    {
        var path = Path.GetTempFileName();
        try
        {
            string key = new('a', 40);
            File.WriteAllText(path, key);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lan:ReadKeyFile"] = path, ["Lan:Network"] = "192.168.1.0/24"
            }).Build();
            var inside = IPAddress.Parse("192.168.1.8");
            Assert.False(new LanAccess(config).Allows(inside, "Bearer " + key));
            config["Lan:Enabled"] = "true";
            var access = new LanAccess(config);
            Assert.True(access.Allows(inside, "Bearer " + key));
            Assert.False(access.Allows(inside, null));
            Assert.False(access.Allows(inside, "Bearer " + new string('b', 40)));
            Assert.False(access.Allows(IPAddress.Parse("192.168.2.8"), "Bearer " + key));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ProgressUsesCheckpointAndObservedWallTimeIncludingWaits()
    {
        var job = new ImportJob { Start = Start, End = Start.AddHours(1), Checkpoint = Start.AddMinutes(10),
            Status = "running", Started = Start, Windows = 1 };
        var window = TimeSpan.FromMinutes(5);
        var now = Start.AddSeconds(40);
        Assert.Null(ImportProgress.Describe(job, window, now).Seconds);
        job.Windows = 2;
        var estimate = ImportProgress.Describe(job, window, now);
        Assert.Equal(16, estimate.Percent);
        Assert.Equal(10, estimate.RemainingWindows);
        Assert.Equal(200, estimate.Seconds);
    }

    [Fact]
    public async Task AbortedWindowCannotCommitAndNewJobCanBeQueued()
    {
        await using var db = Open();
        await Importer.Initialize(db, false, default);
        var plant = new Plant { Id = Guid.NewGuid(), Name = "Import cancellation fixture" };
        db.Plants.Add(plant);
        await db.SaveChangesAsync();
        var job = (await ProviderEndpoints.Enqueue(db, plant.Id, Start, Start.AddMinutes(15), default))!;
        var provider = new HistoryFixture();
        Assert.True(await Importer.Advance(db, job.Id, provider, default));
        provider.Wait = true;
        await using var worker = Open();
        var window = Importer.Advance(worker, job.Id, provider, default);
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var control = Open();
        Assert.False(await ProviderEndpoints.Cancel(control, Guid.NewGuid(), job.Id, default));
        Assert.True(await ProviderEndpoints.Cancel(control, plant.Id, job.Id, default));
        provider.Release.SetResult();
        Assert.False(await window);
        var saved = await control.Imports.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal("cancelled", saved.Status);
        Assert.NotNull(saved.Cancelled);
        Assert.Equal(1, saved.Windows);
        Assert.Equal(1, saved.ReadingRows);
        Assert.Equal(Start.AddMinutes(5), saved.Checkpoint);
        Assert.Equal(1, await control.Readings.CountAsync(r => r.PlantId == plant.Id));
        var next = await ProviderEndpoints.Enqueue(control, plant.Id, Start.AddDays(1), Start.AddDays(2), default);
        Assert.NotNull(next);
        Assert.True(await ProviderEndpoints.Cancel(control, plant.Id, next.Id, default));
        Assert.False(await Importer.Advance(control, next.Id, provider, default));
        Assert.Equal(2, provider.Calls.Count);
    }

    [Fact]
    public async Task LiveSamplesPersistAcrossRestartAndReplacementIsPerChannel()
    {
        await using var db = Open();
        await Importer.Initialize(db, false, default);
        var plant = new Plant { Id = Guid.NewGuid(), Name = "Live restart fixture" };
        db.Plants.Add(plant);
        await db.SaveChangesAsync();
        var at = new DateTimeOffset(2026, 10, 24, 21, 55, 0, TimeSpan.Zero); // 23:55 local
        var cadence = TimeSpan.FromMinutes(10);
        string[] channels = ["pv", "grid"];
        var original = new LiveBatch(at, new Dictionary<string, decimal> { ["pv"] = 2, ["grid"] = -1 });
        await LiveStorage.Save(db, plant.Id, channels, original, at.AddMinutes(1), cadence, null, null, default);
        await LiveStorage.Save(db, plant.Id, channels,
            new(at, new Dictionary<string, decimal> { ["pv"] = 3 }), at.AddMinutes(6), cadence, null, null, default);
        await using var restarted = Open();
        var previous = await restarted.LivePolls.Where(p => p.PlantId == plant.Id)
            .OrderByDescending(p => p.FetchedAt).FirstAsync();
        var samples = await LiveStorage.At(restarted, plant.Id, previous.MeasuredAt, default);
        Assert.Equal(2, samples.Length);
        Assert.Equal(2, samples.Single(r => r.Channel == "pv").Value);
        Assert.Null(LiveStateView.Create(previous, samples, Mapping, at.AddMinutes(6), cadence).Grid.Value);
        var resumed = at.AddMinutes(26);
        await LiveStorage.Save(restarted, plant.Id, channels,
            new(resumed.AddMinutes(-1), new Dictionary<string, decimal> { ["pv"] = 2 }), resumed, cadence, null, null, default);
        Assert.True(await restarted.LiveGaps.AnyAsync(g => g.PlantId == plant.Id && g.Channel == "pv" &&
            g.Reason == "poll-outage" && g.End == resumed));
        Assert.True(await restarted.LiveGaps.AnyAsync(g => g.PlantId == plant.Id && g.Channel == "grid" &&
            g.Reason == "missing-channel"));
        await Storage.Upsert(restarted, plant.Id,
            [new(at, at.AddMinutes(5), .1m, null, Quality: "mean-power", SourceRevision: "sungrow-minutes-v1")], default);
        var history = await HistoryReconciliation.Read(restarted, plant.Id, at, at.AddMinutes(5), default);
        var day = LiveEndpoints.Combine(history, samples);
        Assert.Single(day, p => p.Source == "history" && p.Channel == "pv");
        Assert.Single(day, p => p.Source == "live" && p.Channel == "grid");
        Assert.Equal(2, (await LiveStorage.At(restarted, plant.Id, at, default)).Length);
    }

    [Fact]
    public void HistoricalRepairPreservesVerifiedChannelsAndChecksSpringDayBoundaries()
    {
        var map = new SungrowMapping(1, 2, 3, null, null, true, true, false, false, true, false);
        var first = Calendar.Utc(new DateOnly(2026, 3, 29), Zone);
        var end = Calendar.Utc(new DateOnly(2026, 3, 30), Zone);
        var step = TimeSpan.FromMinutes(5);
        var interval = new Interval(first, first + step, 2, 1, 0, 1,
            Quality: "mean-power", SourceRevision: "sungrow-minutes-v1");
        var partial = new Reading(Guid.Empty, first, first + step, 2, null, 0, 1, null, null, null, null,
            interval.Quality, interval.SourceRevision);
        var merged = Assert.Single(HistoryReconciliation.Merge([partial],
            [interval with { Pv = 5, Import = 1, Export = 0 }], map));
        Assert.Equal(interval, merged);
        Assert.Empty(HistoryReconciliation.Merge([partial], [interval with { End = first.AddMinutes(15) }], map));
        Assert.Empty(HistoryReconciliation.Merge([partial with { Quality = "unverified" }],
            [interval with { Load = null }], map));
        Assert.Single(HistoryReconciliation.Merge([partial with { Quality = "unverified" }], [interval], map));
        var gaps = HistoryReconciliation.Missing([], first, end, map, TimeSpan.FromMinutes(165));
        Assert.Equal(TimeSpan.FromHours(23), TimeSpan.FromTicks(gaps.Sum(g => (g.End - g.Start).Ticks)));
        Assert.All(gaps, gap => Assert.True(gap.End - gap.Start <= TimeSpan.FromMinutes(165)));
    }

    [Fact]
    public async Task EmptyHistoryRetriesSurviveRestartAndVerifiedIntervalsAreSkipped()
    {
        await using var db = Open();
        await Importer.Initialize(db, false, default);
        var plant = new Plant { Id = Guid.NewGuid(), Name = "Repair fixture", Zone = "Europe/Berlin" };
        var today = Calendar.Day(DateTimeOffset.UtcNow, Zone);
        var yesterday = today.AddDays(-1);
        var start = Calendar.Utc(yesterday, Zone);
        var end = Calendar.Utc(today, Zone);
        var missing = end.AddMinutes(-5);
        db.Plants.Add(plant);
        db.Connections.Add(new(plant.Id, "mapped", "", JsonSerializer.Serialize(
            new SungrowMapping(1, null, null, null, null, true, true, false, false, true, false))));
        db.Imports.Add(new() { Id = Guid.NewGuid(), PlantId = plant.Id, Start = start, Checkpoint = end,
            End = end, Status = "completed" });
        await db.SaveChangesAsync();
        var known = new List<Interval>();
        for (var at = start; at < missing; at = at.AddMinutes(5))
            known.Add(new(at, at.AddMinutes(5), 1, null, Quality: "mean-power", SourceRevision: "sungrow-minutes-v1"));
        await Storage.Upsert(db, plant.Id, known.ToArray(), default);
        var provider = new HistoryFixture { Empty = true };
        var worker = new HistoryWorker(provider, new ConfigurationBuilder().Build());
        await worker.Tick(db, plant, default);
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await using var restarted = Open();
            await worker.Tick(restarted, plant, default);
            var gap = await restarted.HistoryGaps.SingleAsync(g => g.PlantId == plant.Id);
            Assert.Equal(missing, gap.Start);
            Assert.Equal(attempt, gap.Attempts);
            Assert.Equal(attempt == 3, gap.Open);
            Assert.True(gap.Due > DateTimeOffset.UtcNow.AddHours(23));
            gap.Due = DateTimeOffset.UtcNow.AddMinutes(-1);
            await restarted.SaveChangesAsync();
        }
        await using var final = Open();
        await worker.Tick(final, plant, default);
        Assert.Equal(3, provider.Calls.Count);
        Assert.All(provider.Calls, at => Assert.Equal(missing, at));
        await Storage.Upsert(final, plant.Id,
            [new(missing, end, 1, null, Quality: "mean-power", SourceRevision: "sungrow-minutes-v1")], default);
        await final.HistoryGaps.Where(g => g.PlantId == plant.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(g => g.Due, DateTimeOffset.UtcNow.AddMinutes(-1)));
        final.ChangeTracker.Clear();
        await worker.Tick(final, plant, default);
        Assert.False(await final.HistoryGaps.AnyAsync(g => g.PlantId == plant.Id));
        Assert.Equal(3, provider.Calls.Count);
    }

    [Fact]
    public async Task DeviceCounterScanPersistsAcrossRestartsAndStartsTheNextClosedDay()
    {
        await using var db = Open();
        await Importer.Initialize(db, false, default);
        var today = new DateOnly(2026, 11, 11);
        var start = Calendar.Utc(today.AddDays(-1), Zone);
        var end = Calendar.Utc(today, Zone);
        var plant = new Plant { Id = Guid.NewGuid(), Name = "Device counter fixture", Zone = Zone.Id };
        var mapping = new SungrowMapping(null, null, null, null, null, true, true, false, false, false, false,
            new(13134, null, null, null, null, null), 15,
            Mapping with { Soc = 13141, SocPercent = false }, DeviceCounters: true);
        db.Plants.Add(plant);
        db.Connections.Add(new(plant.Id, "mapped", "", JsonSerializer.Serialize(mapping)));
        db.Imports.Add(new() { Id = Guid.NewGuid(), PlantId = plant.Id, Start = start, Checkpoint = end,
            End = end, Status = "completed" });
        db.Aggregates.Add(new(plant.Id, "pv", start, end, .4m, 1, "provider", "fixture"));
        await db.SaveChangesAsync();
        var clock = new TestClock(end.AddHours(1));
        var source = new HistoryFixture { DevicePoints = at => at == start ?
        [
            new(13134, "PV", "Wh", true, 3, "available",
                [new(at, 1000), new(at.AddMinutes(15), 1100), new(at.AddMinutes(45), 1400)]),
            new(13003, "DC", "W", true, 3, "available",
                [new(at, 400), new(at.AddMinutes(15), 500), new(at.AddMinutes(45), 400)]),
            new(13141, "SOC", "", true, 0, "unknown-unit", [new(at, 0), new(at.AddMinutes(45), .45m)])
        ] : [] };
        var config = new ConfigurationBuilder().Build();
        await new HistoryWorker(source, config, clock).Tick(db, plant, default);
        Assert.Equal(Calendar.Utc(today.AddDays(-30), Zone), Assert.Single(source.DeviceCalls));
        var scan = await db.HistoryScans.SingleAsync(s => s.PlantId == plant.Id);
        scan.CounterDay = today.AddDays(-1);
        scan.CounterUntil = null;
        scan.CounterData = null;
        await db.SaveChangesAsync();

        for (int i = 0; i < 11; i++)
        {
            clock.Now = clock.Now.AddMinutes(1);
            await using var restarted = Open();
            await new HistoryWorker(source, config, clock).Tick(restarted, plant, default);
        }
        var reading = await db.Readings.AsNoTracking().SingleAsync(r => r.PlantId == plant.Id);
        Assert.Equal(.1m, reading.Pv);
        Assert.Equal(start.AddMinutes(15), reading.End);
        var gap = await db.Aggregates.AsNoTracking().SingleAsync(a => a.PlantId == plant.Id &&
            a.Origin == DeviceCounterHistory.GapOrigin);
        Assert.Equal((start.AddMinutes(15), start.AddMinutes(45), .3m), (gap.Start, gap.End, gap.Kwh));
        Assert.Equal(2, await db.LiveMeasurements.CountAsync(m => m.PlantId == plant.Id && m.Source == DeviceCounterHistory.SocSource));
        Assert.Equal(today, (await db.HistoryScans.AsNoTracking().SingleAsync(s => s.PlantId == plant.Id)).CounterDay);
        Assert.Equal(1, source.DeviceCalls.Count(at => at == start));

        clock.Now = Calendar.Utc(today.AddDays(1), Zone).AddMinutes(1);
        await using var following = Open();
        await new HistoryWorker(source, config, clock).Tick(following, plant, default);
        Assert.Equal(end, source.DeviceCalls.Last());
        Assert.Equal(1, await following.Readings.CountAsync(r => r.PlantId == plant.Id));
    }

    [Fact]
    public async Task ActivationUsesServerMappingAndKeepsOneInitialJobAcrossConfirmations()
    {
        await using var db = Open();
        await Importer.Initialize(db, false, default);
        var file = Path.GetTempFileName();
        var mappingFile = Path.GetTempFileName();
        var id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var previous = await db.Plants.AsNoTracking().Where(p => p.Active).Select(p => (Guid?)p.Id).SingleOrDefaultAsync();
        try
        {
            File.WriteAllText(file, """{"appKey":"fixture-app","accessKey":"fixture-key","redirect":"http://127.0.0.1:8081/"}""");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sungrow:AppFile"] = file, ["Sungrow:CallSpacingSeconds"] = "1",
                ["Sungrow:InitialHistoryDays"] = "7"
            }).Build();
            using var services = new ServiceCollection().AddScoped(_ => Open())
                .AddSingleton(new SungrowSetup(config)).BuildServiceProvider();
            using var http = new HttpClient(new Stub(request => Task.FromResult(Reply(
                request.RequestUri!.Segments[^1] switch
                {
                    "token" => """{"access_token":"fixture-token","refresh_token":"fixture-refresh","expires_in":3600}""",
                    "queryPowerStationList" => JsonSerializer.Serialize(new { pageList = new[] { new { ps_id = id, ps_name = "Setup fixture" } } }),
                    "getDeviceListByPsId" => """{"pageList":[]}""",
                    _ => throw new InvalidOperationException("Unerwarteter Provideraufruf")
                }))));
            var protection = new EphemeralDataProtectionProvider();
            LiveProvider Provider() => new(services.GetRequiredService<IServiceScopeFactory>(),
                protection, new SungrowDiscovery(http), config, new(false, "", false), NullLogger<LiveProvider>.Instance);
            var provider = Provider();
            await provider.Authorize("fixture-code", default);
            await Assert.ThrowsAsync<ArgumentException>(() => provider.Activate(new("123"), default));
            await provider.Activate(new(id), default);
            await Provider().Activate(new(id), default);
            var plant = await db.Plants.AsNoTracking().SingleAsync(p => p.ProviderId == id);
            Assert.True(plant.Active);
            Assert.Equal("Setup fixture", plant.Name);
            Assert.Equal("Europe/Berlin", plant.Zone);
            var stored = await db.Connections.AsNoTracking().SingleAsync(c => c.PlantId == plant.Id);
            var mapping = JsonSerializer.Deserialize<SungrowMapping>(stored.Capabilities)!;
            Assert.Empty(mapping.Points);
            Assert.Null(mapping.Live);
            Assert.Equal(new[] { 83022, 83118, 83102, 83072 }, mapping.Daily!.Points);
            var job = await db.Imports.AsNoTracking().SingleAsync(j => j.PlantId == plant.Id);
            Assert.Equal("pending", job.Status);
            Assert.True(job.AutomaticInitial);
            Assert.Equal(7, Calendar.Day(job.End, Zone).DayNumber - Calendar.Day(job.Start, Zone).DayNumber);
            Assert.True(await ProviderEndpoints.Cancel(db, plant.Id, job.Id, default));
            await Provider().Activate(new(id), default);
            Assert.Equal("cancelled", (await db.Imports.AsNoTracking().SingleAsync(j => j.PlantId == plant.Id)).Status);

            var confirmed = mapping with { Load = 83106, StepMinutes = 15 };
            File.WriteAllText(mappingFile, JsonSerializer.Serialize(new Dictionary<string, SungrowMapping> { [id] = confirmed }));
            config["Sungrow:MappingFile"] = mappingFile;
            await Provider().Activate(new(id), default);
            var updated = await db.Connections.AsNoTracking().SingleAsync(c => c.PlantId == plant.Id);
            Assert.Equal(confirmed, JsonSerializer.Deserialize<SungrowMapping>(updated.Capabilities));
            Assert.Single(await db.Imports.Where(j => j.PlantId == plant.Id).ToArrayAsync());
        }
        finally
        {
            await db.Plants.Where(p => p.ProviderId == id).ExecuteDeleteAsync();
            if (previous is { } active)
                await db.Plants.Where(p => p.Id == active).ExecuteUpdateAsync(s => s.SetProperty(p => p.Active, true));
            File.Delete(file);
            File.Delete(mappingFile);
        }
    }

    [Fact]
    public async Task AutomaticBackfillContinuesPastEmptyOpeningDays()
    {
        await using var db = Open();
        await Importer.Initialize(db, false, default);
        var plant = new Plant { Id = Guid.NewGuid(), Name = "New installation fixture" };
        var job = new ImportJob { Id = Guid.NewGuid(), PlantId = plant.Id, Start = Start,
            Checkpoint = Start, End = Start.AddDays(3), AutomaticInitial = true };
        db.Plants.Add(plant);
        db.Imports.Add(job);
        await db.SaveChangesAsync();
        var source = new HistoryFixture { Window = TimeSpan.FromDays(1), Empty = true };
        Assert.True(await Importer.Advance(db, job.Id, source, default));
        Assert.True(await Importer.Advance(db, job.Id, source, default));
        source.Empty = false;
        Assert.False(await Importer.Advance(db, job.Id, source, default));
        Assert.Equal("completed", job.Status);
        Assert.Equal(2, job.EmptyWindows);
        Assert.NotNull(job.LastSuccess);
    }

    [Fact]
    public async Task ConcurrentDeviceReadsShareOneDocumentedCloudRequest()
    {
        await using (var db = Open()) await Importer.Initialize(db, false, default);
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """{"appKey":"fixture-app","accessKey":"fixture-key","redirect":"http://127.0.0.1:8081/"}""");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sungrow:AppFile"] = file, ["Sungrow:CallSpacingSeconds"] = "1"
            }).Build();
            using var services = new ServiceCollection().AddScoped(_ => Open()).BuildServiceProvider();
            var calls = 0;
            using var http = new HttpClient(new Stub(async request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                    return Reply("""{"access_token":"fixture-token","refresh_token":"fixture-refresh","expires_in":3600}""");
                Interlocked.Increment(ref calls);
                Assert.Equal("https://gateway.isolarcloud.eu/openapi/platform/getDeviceRealTimeData",
                    request.RequestUri.AbsoluteUri);
                Assert.Equal("fixture-token", request.Headers.Authorization?.Parameter);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal(Mapping.DeviceKey, body.RootElement.GetProperty("ps_key_list")[0].GetString());
                Assert.Equal("3", body.RootElement.GetProperty("point_id_list")[2].GetString());
                return Reply("""
                    {"device_point_list":[{"device_point":{"ps_id":42,"ps_key":"42_14_1_1",
                    "device_time":"FIXTURE_TIME","p3":"1000"}}],"point_dict":[{"point_id":3,"point_unit":"W"}]}
                    """.Replace("FIXTURE_TIME", Calendar.Day(DateTimeOffset.UtcNow, Zone).AddDays(-1)
                        .ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "120000"));
            }));
            var provider = new LiveProvider(services.GetRequiredService<IServiceScopeFactory>(),
                new EphemeralDataProtectionProvider(), new SungrowDiscovery(http), config,
                new(false, "", false), NullLogger<LiveProvider>.Instance);
            await provider.Authorize("fixture-code", default);
            var plant = new Plant { Id = Guid.NewGuid(), ProviderId = "42", Zone = Zone.Id };
            var batches = await Task.WhenAll(provider.ReadNow(plant, Mapping, default),
                provider.ReadNow(plant, Mapping, default));
            Assert.Equal(1, calls);
            Assert.All(batches, batch => Assert.Equal(-1m, batch.Values["grid"]));
            var probe = await provider.ProbeLive(plant, Mapping.DeviceKey, [new(13141, "SOC", "soc")], default);
            Assert.Equal("rate-limit", probe.Failure);
            Assert.True(probe.RetryAfterSeconds > 0);
            Assert.Equal(1, calls);
            Assert.Same(batches[0], await provider.ReadNow(plant, Mapping, default));
        }
        finally { File.Delete(file); }

    }

    static HttpResponseMessage Reply(string data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"result_code\":\"1\",\"result_data\":" + data + "}")
    };

    sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            handler(request);
    }

    sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    sealed class HistoryFixture : IHistorySource, IDeviceCounterSource
    {
        public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(5);
        public int RefreshMinutes => 1440;
        public bool Empty { get; set; }
        public bool Wait { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<DateTimeOffset> Calls { get; } = [];
        public List<DateTimeOffset> DeviceCalls { get; } = [];
        public Func<DateTimeOffset, PointObservation[]>? DevicePoints { get; init; }

        public Task<PointProbeResult> ReadDeviceHistory(Plant plant, string device, ProbePoint[] points,
            DateTimeOffset start, DateTimeOffset end, int step, CancellationToken ct)
        {
            DeviceCalls.Add(start);
            return Task.FromResult(new PointProbeResult("Gerät", end, DevicePoints?.Invoke(start) ?? []));
        }

        public Task<Aggregate[]> ReadAggregates(Guid plant, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
            Task.FromResult<Aggregate[]>([]);

        public async Task<Interval[]> Read(Guid plant, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
        {
            Calls.Add(start);
            if (Wait)
            {
                Entered.SetResult();
                await Release.Task.WaitAsync(ct);
            }
            return Empty ? [] : [new(start, end, 1, null)];
        }
    }
}
