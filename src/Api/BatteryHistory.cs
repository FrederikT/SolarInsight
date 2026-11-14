using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Application;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public record BatteryHistoryRequest(DateOnly Start, DateOnly End, bool Apply = false);

/// <summary>
/// DATA-06: fills historical daily values from the inverter's lifetime counters. One device-history call per
/// local midnight; day values are counter differences and are written only when they pass
/// <see cref="BatteryCounterDays.Evaluate"/>. Progress lives in memory: the stored daily values are the
/// checkpoint, so a restarted run simply skips the days that are already complete.
/// </summary>
public sealed class BatteryHistory(IServiceScopeFactory scopes, IDeviceCounterSource source, IConfiguration configuration,
    ILogger<BatteryHistory> logger)
{
    public const int PreviewDays = 31;
    static readonly int[] CounterPoints = DeviceCounterHistory.Points.Select(p => p.Id).ToArray();
    static readonly Dictionary<int, string> ChannelOf = new()
    {
        [13134] = "pv", [13130] = "load", [13148] = "import", [13125] = "export", [13034] = "charge", [13035] = "discharge"
    };

    readonly Lock gate = new();
    CancellationTokenSource? running;
    Status state = new("idle", false, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, [], null, new Dictionary<string, int>());

    public record Status(string State, bool Apply, DateOnly? Start, DateOnly? End, int Days, int Done, int Accepted,
        int Complete, int Rejected, int Missing, int Calls, int RemainingCalls, int WrittenValues, DateOnly? Current,
        string? Error, CounterDay[] Recent, BatteryTotals? Totals, IReadOnlyDictionary<string, int> Problems);
    public record BatteryTotals(decimal Charge, decimal Discharge);

    public Status Snapshot() { lock (gate) return state; }

    public bool Running { get { lock (gate) return running is not null; } }

    /// <summary>Starts a run unless one is active. Preview runs are limited to <see cref="PreviewDays"/> days.</summary>
    public string? Start(Plant plant, string device, BatteryHistoryRequest input)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        var today = Calendar.Day(DateTimeOffset.UtcNow, zone);
        if (input.End < input.Start || input.End >= today || input.Start.Year < 2000)
            return "Zeitraum mit abgeschlossenen Tagen angeben (Ende vor heute).";
        if (!input.Apply && input.End.DayNumber - input.Start.DayNumber >= PreviewDays)
            return $"Die Vorschau umfasst höchstens {PreviewDays} Tage.";
        if (input.End.DayNumber - input.Start.DayNumber >= Queries.MaxDays) return "Zeitraum zu lang.";
        lock (gate)
        {
            if (running is not null) return "Ein Batterie-Nachimport läuft bereits.";
            running = new CancellationTokenSource();
            state = new("running", input.Apply, input.Start, input.End, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, [], null, new Dictionary<string, int>());
            var token = running.Token;
            _ = Task.Run(() => Run(plant, device, zone, input, token), CancellationToken.None);
        }
        return null;
    }

    public bool Cancel()
    {
        lock (gate)
        {
            if (running is null) return false;
            running.Cancel();
            return true;
        }
    }

    async Task Run(Plant plant, string device, TimeZoneInfo zone, BatteryHistoryRequest input, CancellationToken ct)
    {
        var recent = new List<CounterDay>();
        decimal charge = 0, discharge = 0;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Store>();
            var first = Calendar.Utc(input.Start, zone);
            var last = Calendar.Utc(input.End.AddDays(1), zone);
            var stored = (await db.Aggregates.AsNoTracking().Where(a => a.PlantId == plant.Id && a.Origin == "provider" &&
                    a.Coverage == 1 && a.Start >= first && a.Start < last).ToArrayAsync(ct))
                .GroupBy(a => Calendar.Day(a.Start, zone))
                .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, decimal>)g.ToDictionary(a => a.Point, a => a.Kwh));
            IReadOnlyDictionary<string, decimal> Stored(DateOnly d) => stored.GetValueOrDefault(d) ?? new Dictionary<string, decimal>();

            var days = new List<DateOnly>();
            for (var d = input.Start; d <= input.End; d = d.AddDays(1))
                if (!BatteryCounterDays.Channels.All(Stored(d).ContainsKey)) days.Add(d);
            var boundaries = days.SelectMany(d => new[] { d, d.AddDays(1) }).Distinct().Count();
            Update(s => s with { Days = days.Count, RemainingCalls = boundaries });

            var snapshots = new Dictionary<DateOnly, CounterSnapshot?>();
            int failures = 0;
            async Task<CounterSnapshot?> Snapshot(DateOnly day)
            {
                if (snapshots.TryGetValue(day, out var known)) return known;
                var midnight = Calendar.Utc(day, zone);
                // A one-hour window: very short windows sometimes come back empty for older days.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        var report = await source.ReadDeviceHistory(plant, device, DeviceCounterHistory.Points,
                            midnight, midnight.AddHours(1), 15, ct);
                        failures = 0;
                        Update(s => s with { Calls = s.Calls + 1, RemainingCalls = Math.Max(0, s.RemainingCalls - 1) });
                        var snapshot = Read(report, day, midnight);
                        var absent = BatteryCounterDays.Channels.Where(c => snapshot?.Kwh.ContainsKey(c) != true).ToArray();
                        // Empty answers are often transient; ask once more after a pause before giving up the day.
                        if (absent.Length == BatteryCounterDays.Channels.Length && attempt == 0)
                        {
                            Note("leere Antwort, wiederholt");
                            await Task.Delay(TimeSpan.FromSeconds(30), ct);
                            continue;
                        }
                        if (absent.Length > 0) Note(absent.Length == BatteryCounterDays.Channels.Length
                            ? "kein Zählerstand um 00:00" : "unvollständiger Zählerstand: " + string.Join(", ", absent));
                        return snapshots[day] = snapshot;
                    }
                    catch (ProviderFailure failure) when (failure.Category == "rate-limit" && attempt < 6)
                    {
                        // Wait for the provider spacing or quota window instead of skipping days.
                        Note("Sungrow: rate-limit (gewartet)");
                        await Task.Delay(failure.RetryAfter is { } wait && wait > TimeSpan.Zero ? wait : TimeSpan.FromMinutes(10), ct);
                    }
                    catch (Exception ex) when (ex is ProviderFailure { Retryable: true } or HttpRequestException or TimeoutException)
                    {
                        string reason = ex is ProviderFailure f ? "Sungrow: " + f.Category : ex is TimeoutException ? "Zeitüberschreitung" : "Netzwerk";
                        Update(s => s with { Calls = s.Calls + 1 });
                        if (attempt < 2)
                        {
                            Note(reason + " (wiederholt)");
                            await Task.Delay(TimeSpan.FromSeconds(30 * (attempt + 1)), ct);
                            continue;
                        }
                        Note(reason);
                        if (++failures >= 5) throw;
                        Update(s => s with { RemainingCalls = Math.Max(0, s.RemainingCalls - 1) });
                        return snapshots[day] = null;
                    }
                }
            }

            foreach (var day in days)
            {
                ct.ThrowIfCancellationRequested();
                Update(s => s with { Current = day });
                var result = BatteryCounterDays.Evaluate(await Snapshot(day), await Snapshot(day.AddDays(1)), day, Stored(day));
                int written = 0;
                if (result.Status == "accepted" && input.Apply)
                {
                    var start = Calendar.Utc(day, zone);
                    var end = Calendar.Utc(day.AddDays(1), zone);
                    var rows = result.Missing.Select(channel => new Aggregate(plant.Id, channel, start, end,
                        result.Values[channel], 1, "provider", BatteryCounterDays.Revision)).ToArray();
                    written = await Storage.UpsertAggregates(db, plant.Id, rows, ct, fillOnly: true);
                }
                if (result.Status is "accepted" or "complete")
                {
                    charge += result.Values["charge"];
                    discharge += result.Values["discharge"];
                }
                // Keep a bounded sample for the preview: the first accepted days and every problem day up to a limit.
                bool problem = result.Status is "rejected" or "missing";
                if (problem ? recent.Count(r => r.Status is "rejected" or "missing") < 100 : recent.Count(r => !(r.Status is "rejected" or "missing")) < 40)
                    recent.Add(result);
                Update(s => s with
                {
                    Done = s.Done + 1, WrittenValues = s.WrittenValues + written,
                    Accepted = s.Accepted + (result.Status == "accepted" ? 1 : 0),
                    Complete = s.Complete + (result.Status == "complete" ? 1 : 0),
                    Rejected = s.Rejected + (result.Status == "rejected" ? 1 : 0),
                    Missing = s.Missing + (result.Status == "missing" ? 1 : 0),
                    Recent = recent.ToArray(), Totals = new(charge, discharge)
                });
                // Midnight readings older than the previous day are no longer needed.
                snapshots.Remove(day);
            }
            Update(s => s with { State = "completed", Current = null });
        }
        catch (OperationCanceledException)
        {
            Update(s => s with { State = "cancelled", Current = null });
        }
        catch (Exception ex)
        {
            logger.LogWarning("Batterie-Nachimport abgebrochen. Kategorie {Category}", ex is ProviderFailure f ? f.Category : ex.GetType().Name);
            Update(s => s with { State = "failed", Current = null,
                Error = ex is ProviderFailure failure ? "Sungrow: " + failure.Category : "Interner Fehler" });
        }
        finally
        {
            lock (gate) { running?.Dispose(); running = null; }
        }
    }

    void Update(Func<Status, Status> change) { lock (gate) state = change(state); }

    void Note(string problem) => Update(s => s with { Problems = new Dictionary<string, int>(s.Problems)
        { [problem] = s.Problems.GetValueOrDefault(problem) + 1 } });

    /// <summary>Counter values exactly at the midnight timestamp, converted to kWh.</summary>
    internal static CounterSnapshot? Read(PointProbeResult report, DateOnly day, DateTimeOffset midnight)
    {
        var values = new Dictionary<string, decimal>();
        foreach (var point in report.Points.Where(p => ChannelOf.ContainsKey(p.Point)))
        {
            decimal scale = point.Unit switch { "Wh" => .001m, "kWh" => 1, _ => 0 };
            var at = point.Values.Where(v => v.MeasuredAt == midnight).Select(v => v.Value).Distinct().ToArray();
            if (scale > 0 && at.Length == 1 && at[0] >= 0) values[ChannelOf[point.Point]] = at[0] * scale;
        }
        return values.Count == 0 ? null : new(day, values);
    }

    /// <summary>The confirmed device of the active plant, or an explanation why the run cannot start.</summary>
    public static async Task<(string? Device, string? Problem)> Device(Store db, Plant plant, CancellationToken ct)
    {
        if (plant.ProviderId is null) return (null, "Keine Sungrow-Anlage aktiv.");
        var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(c => c.PlantId == plant.Id, ct);
        var mapping = connection is null ? null : JsonSerializer.Deserialize<SungrowMapping>(connection.Capabilities);
        return mapping is { DeviceCounters: true, Live: { Confirmed: true } device } && ProviderEndpoints.SupportsDeviceProbe(device.DeviceKey)
            ? (device.DeviceKey, null) : (null, "Ein bestätigtes Gerätezählerprofil ist erforderlich.");
    }

    public int CallSpacingSeconds => Math.Clamp(configuration.GetValue("Sungrow:CallSpacingSeconds", 15), 1, 86400);
}
