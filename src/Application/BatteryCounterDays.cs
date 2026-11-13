namespace SolarInsight.Application;

/// <summary>Cumulative device counters in kWh at the local midnight that starts <see cref="Day"/>.</summary>
public record CounterSnapshot(DateOnly Day, IReadOnlyDictionary<string, decimal> Kwh);

/// <summary>
/// Result for one calendar day. <see cref="Values"/> are the counter differences; <see cref="Missing"/> lists
/// the channels that have no stored daily value yet and would be written.
/// </summary>
public record CounterDay(DateOnly Day, string Status, string? Reason, IReadOnlyDictionary<string, decimal> Values,
    IReadOnlyDictionary<string, decimal> Deviations, string[] Missing);

/// <summary>
/// Daily energies from two consecutive midnight readings of the inverter's lifetime counters. The device history
/// keeps these counters for years, also for days where the plant daily route has no battery values. A day is
/// only accepted when the counters rise, the six channels close the energy balance and every channel that is
/// already stored as a daily value agrees within <see cref="Tolerance"/>.
/// </summary>
public static class BatteryCounterDays
{
    public const string Revision = "sungrow-device-counter-day-v1";
    public static readonly string[] Channels = ["pv", "load", "import", "export", "charge", "discharge"];

    /// <summary>Daily counters are rounded to 0.1 kWh; DATA-04 used the same bound against CSV values.</summary>
    public const decimal Tolerance = .2m;

    /// <summary>A plausible upper bound for one channel and day; larger jumps indicate a counter reset.</summary>
    public const decimal MaxDailyKwh = 500;

    public static CounterDay Evaluate(CounterSnapshot? start, CounterSnapshot? end, DateOnly day,
        IReadOnlyDictionary<string, decimal> stored)
    {
        var empty = new Dictionary<string, decimal>();
        if (start is null || end is null || Channels.Any(c => !start.Kwh.ContainsKey(c) || !end.Kwh.ContainsKey(c)))
            return new(day, "missing", "Zählerstand um Mitternacht fehlt", empty, empty, []);
        var values = Channels.ToDictionary(c => c, c => end.Kwh[c] - start.Kwh[c]);
        if (values.Values.Any(v => v < 0))
            return new(day, "rejected", "Zähler rückläufig", values, empty, []);
        if (values.Values.Any(v => v > MaxDailyKwh))
            return new(day, "rejected", "Unplausibler Zählersprung", values, empty, []);

        decimal residual = values["load"] + values["charge"] + values["export"] - values["pv"] - values["import"] - values["discharge"];
        if (Math.Abs(residual) > EnergyBalance.DailyTolerance(values["load"], values["pv"]))
            return new(day, "rejected", $"Bilanz schließt nicht ({residual:0.0} kWh)", values, empty, []);

        var deviations = stored.Where(p => values.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => values[p.Key] - p.Value);
        var off = deviations.Where(p => Math.Abs(p.Value) > Tolerance).Select(p => p.Key).ToArray();
        if (off.Length > 0)
            return new(day, "rejected", "Abweichung zu gespeicherten Tageswerten: " + string.Join(", ", off), values, deviations, []);

        var missing = Channels.Where(c => !stored.ContainsKey(c)).ToArray();
        return new(day, missing.Length == 0 ? "complete" : "accepted", null, values, deviations, missing);
    }
}
