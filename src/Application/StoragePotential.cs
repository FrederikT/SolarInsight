using SolarInsight.Domain;

namespace SolarInsight.Application;

/// <summary>Inputs of the daily storage estimate. Prices in EUR per kWh, capacities as usable kWh.</summary>
public record StorageAssumptions(decimal[] ExtraCapacities, decimal RoundTripEfficiency = .9m,
    decimal ImportPrice = .30m, decimal ExportPrice = .08m, decimal? InvestmentPerKwh = null)
{
    public void Validate()
    {
        if (ExtraCapacities is not { Length: > 0 and <= 12 } || ExtraCapacities.Any(c => c is < 0 or > 200) ||
            RoundTripEfficiency is < .5m or > 1 || ImportPrice is < 0 or > 5 || ExportPrice is < 0 or > 5 ||
            InvestmentPerKwh is < 0 or > 10000)
            throw new ArgumentException("Ungültige Annahmen für die Speicherabschätzung.");
    }
}

public record PotentialTotals(decimal Import, decimal Export, decimal? Autarky, decimal? SelfConsumption);
public record MonthlyBenefit(string Month, int Days, decimal AvoidedImport, decimal LostExport);
public record CapacityScenario(decimal ExtraCapacity, PotentialTotals Totals, decimal AvoidedImport, decimal LostExport,
    decimal Savings, decimal? AnnualSavings, decimal? PaybackYears, decimal EquivalentFullCycles,
    int DaysCapacityLimited, int DaysSurplusLimited, int DaysDemandLimited, MonthlyBenefit[] Months);
public record ExistingBattery(int Days, decimal Charge, decimal Discharge, decimal ImportWithout, decimal ExportWithout,
    decimal? AutarkyWithout, decimal? AutarkyWith, decimal Savings);
public record StoragePotentialResult(StorageAssumptions Assumptions, int Days, int UsedDays, DateOnly? First,
    DateOnly? Last, decimal Load, decimal Pv, PotentialTotals Current, ExistingBattery? Existing,
    CapacityScenario[] Scenarios, string[] Notes);

/// <summary>
/// Transparent day-balance estimate for additional storage. It uses the long-term Sungrow daily values
/// (PV, load, import, export) instead of interval data, so it works for every period with daily values.
///
/// Model per day, with carry-over of the extra storage between days:
///   charged   = min(export_d, free capacity / η)   (surplus that would otherwise be exported)
///   delivered = min(stored, import_d)              (offsets later grid import)
/// The existing battery keeps operating as measured; the additional capacity only absorbs today's export and
/// only replaces today's import. Because a day total cannot tell whether the import happened before or after
/// the surplus, the result is an upper bound. Losses are applied on charging with the round-trip efficiency η.
/// </summary>
public static class StoragePotential
{
    public static StoragePotentialResult Evaluate(DailyValues[] input, StorageAssumptions assumptions, int periodDays)
    {
        assumptions.Validate();
        var days = input.Where(d => d.Import is >= 0 && d.Export is >= 0).OrderBy(d => d.Day).ToArray();
        var complete = days.Where(d => d.Load is >= 0 && d.Pv is >= 0).ToArray();
        decimal load = complete.Sum(d => d.Load!.Value), pv = complete.Sum(d => d.Pv!.Value);
        var current = Totals(complete, complete.Sum(d => d.Import!.Value), complete.Sum(d => d.Export!.Value));
        var notes = new List<string>
        {
            "Tagesbilanzmodell mit Übertrag zwischen Tagen. Die Reihenfolge innerhalb eines Tages ist unbekannt, daher obere Abschätzung.",
            "Der vorhandene Speicher arbeitet wie gemessen weiter. Zusätzliche Kapazität nimmt nur bisherige Einspeisung auf und ersetzt nur bisherigen Netzbezug.",
            "Ersparnis = vermiedener Netzbezug × Bezugspreis − entgangene Einspeisung × Einspeisevergütung. Ohne Alterung, Netzentgeltänderungen und Finanzierung."
        };
        if (days.Length < periodDays) notes.Add($"{periodDays - days.Length} Tage ohne Netzbezug oder Einspeisung sind nicht enthalten.");
        if (days.Length < 300) notes.Add("Weniger als 300 Tage: Jahreswerte sind hochgerechnet und saisonal verzerrt.");

        var scenarios = assumptions.ExtraCapacities.Distinct().Order().Select(capacity =>
            Simulate(complete, capacity, assumptions, current)).ToArray();
        return new(assumptions, periodDays, complete.Length, complete.FirstOrDefault()?.Day, complete.LastOrDefault()?.Day,
            load, pv, current, Existing(complete, assumptions), scenarios, notes.ToArray());
    }

    static PotentialTotals Totals(DailyValues[] days, decimal import, decimal export)
    {
        decimal load = days.Sum(d => d.Load!.Value), pv = days.Sum(d => d.Pv!.Value);
        return new(import, export, load > 0 ? (load - import) / load : null, pv > 0 ? (pv - export) / pv : null);
    }

    static CapacityScenario Simulate(DailyValues[] days, decimal capacity, StorageAssumptions a, PotentialTotals current)
    {
        decimal stored = 0, avoided = 0, lost = 0;
        int capacityLimited = 0, surplusLimited = 0, demandLimited = 0;
        var months = new Dictionary<string, (int Days, decimal Avoided, decimal Lost)>();
        foreach (var day in days)
        {
            decimal export = day.Export!.Value, import = day.Import!.Value;
            decimal room = capacity - stored;
            decimal charged = Math.Min(export, room / a.RoundTripEfficiency);
            stored += charged * a.RoundTripEfficiency;
            decimal delivered = Math.Min(stored, import);
            stored -= delivered;
            avoided += delivered;
            lost += charged;
            if (capacity > 0)
            {
                // What limited the benefit on this day: storage size, missing surplus, or no further demand.
                if (export * a.RoundTripEfficiency >= room - .05m && import > delivered + .05m) capacityLimited++;
                else if (import > delivered + .05m) surplusLimited++;
                else demandLimited++;
            }
            string key = day.Day.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            var m = months.GetValueOrDefault(key);
            months[key] = (m.Days + 1, m.Avoided + delivered, m.Lost + charged);
        }
        decimal savings = avoided * a.ImportPrice - lost * a.ExportPrice;
        decimal? annual = days.Length >= 28 ? savings * 365 / days.Length : null;
        decimal? payback = a.InvestmentPerKwh is { } price && capacity > 0 && annual > 0 ? price * capacity / annual : null;
        return new(capacity, Totals(days, current.Import - avoided, current.Export - lost), avoided, lost,
            decimal.Round(savings, 2), annual is null ? null : decimal.Round(annual.Value, 0),
            payback is null ? null : decimal.Round(payback.Value, 1), capacity > 0 ? decimal.Round(avoided / capacity, 1) : 0,
            capacityLimited, surplusLimited, demandLimited,
            months.OrderBy(p => p.Key).Select(p => new MonthlyBenefit(p.Key, p.Value.Days, p.Value.Avoided, p.Value.Lost)).ToArray());
    }

    /// <summary>
    /// Value of the battery that already exists, on days with measured charge and discharge: without it,
    /// discharged energy would have been imported and charged energy exported. Grid charging is ignored, which
    /// matches the measured data where charging closes against PV surplus.
    /// </summary>
    static ExistingBattery? Existing(DailyValues[] days, StorageAssumptions a)
    {
        var battery = days.Where(d => d.Charge is >= 0 && d.Discharge is >= 0).ToArray();
        if (battery.Length == 0) return null;
        decimal charge = battery.Sum(d => d.Charge!.Value), discharge = battery.Sum(d => d.Discharge!.Value);
        decimal import = battery.Sum(d => d.Import!.Value), export = battery.Sum(d => d.Export!.Value);
        decimal load = battery.Sum(d => d.Load!.Value);
        return new(battery.Length, charge, discharge, import + discharge, export + charge,
            load > 0 ? (load - import - discharge) / load : null, load > 0 ? (load - import) / load : null,
            decimal.Round(discharge * a.ImportPrice - charge * a.ExportPrice, 2));
    }
}
