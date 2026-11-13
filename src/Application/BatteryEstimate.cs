using SolarInsight.Domain;

namespace SolarInsight.Application;

/// <summary>
/// Battery parameters derived from measurements when the operator has not configured any. All values are
/// estimates and are reported as such; they only drive limit detection and default simulation inputs.
/// </summary>
public record BatteryEstimate(Battery Battery, decimal? CapacityKwh, decimal MinSoc, decimal MaxSoc,
    int SocSamples, int CapacityPairs, decimal? MaxDailyChargeKwh);

public static class BatteryEstimation
{
    /// <summary>
    /// SOC limits are the 1st and 99th percentile of the observed SOC, so single outliers do not move them.
    /// Capacity is the least-squares slope between battery net energy (charge − discharge) and the SOC change of
    /// consecutive intervals; it therefore includes charging losses and is a usable-capacity estimate.
    /// </summary>
    public static BatteryEstimate? Estimate(Interval[] rows, DailyValues[] daily)
    {
        var soc = rows.Where(r => r.Soc is >= 0 and <= 1).OrderBy(r => r.Start).ToArray();
        decimal? maxDaily = daily.Where(d => d.Charge.HasValue).Select(d => d.Charge).Max();
        if (soc.Length < 48) return null;
        var sorted = soc.Select(r => r.Soc!.Value).Order().ToArray();
        decimal min = decimal.Round(sorted[(int)((sorted.Length - 1) * .01m)], 2);
        decimal max = decimal.Round(sorted[(int)((sorted.Length - 1) * .99m)], 2);
        if (max - min < .2m) return null;

        decimal xy = 0, xx = 0;
        int pairs = 0;
        for (int i = 1; i < soc.Length; i++)
        {
            var row = soc[i];
            if (soc[i - 1].End != row.Start || row.Charge is null || row.Discharge is null) continue;
            decimal change = row.Soc!.Value - soc[i - 1].Soc!.Value;
            // Ignore clipped intervals at the limits; there SOC no longer follows the energy.
            if (Math.Abs(change) < .01m || row.Soc <= min || row.Soc >= max) continue;
            decimal net = row.Charge.Value - row.Discharge.Value;
            xy += net * change;
            xx += change * change;
            pairs++;
        }
        // SOC is a share of the total capacity, so the slope kWh per SOC unit is the capacity itself.
        decimal? capacity = pairs >= 20 && xx > 0 ? decimal.Round(xy / xx, 1) : null;
        if (capacity is < 1 or > 200) capacity = null;
        decimal chargeKw = Peak(soc.Where(r => r.Charge.HasValue).Select(r => r.Charge!.Value / r.Hours));
        decimal dischargeKw = Peak(soc.Where(r => r.Discharge.HasValue).Select(r => r.Discharge!.Value / r.Hours));
        // Capacity before reserve; without an estimate fall back to the largest observed daily charge.
        decimal total = capacity ?? (maxDaily is > 0 ? decimal.Round(maxDaily.Value / (max - min), 1) : 10);
        var battery = new Battery(total, Math.Max(.5m, chargeKw), Math.Max(.5m, dischargeKw), .95m, .95m, min, max);
        return new(battery, capacity, min, max, soc.Length, pairs, maxDaily);
    }

    static decimal Peak(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : decimal.Round(sorted[(int)((sorted.Length - 1) * .99m)], 1);
    }
}
