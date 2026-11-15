using System.Globalization;
using SolarInsight.Domain;

namespace SolarInsight.Application;

/// <summary>
/// Days whose stored values describe a provider outage: an outage day reports zero household load (a house never
/// consumes nothing for a whole day), and the following day usually contains the energy of both days.
/// </summary>
public record DayFlag(DateOnly Day, string Kind);

public static class DayFlags
{
    public const string Outage = "outage";
    public const string CatchUp = "catch-up";

    public static DayFlag[] Detect(DailyValues[] daily)
    {
        var byDay = daily.ToDictionary(d => d.Day);
        var result = new List<DayFlag>();
        foreach (var day in daily.OrderBy(d => d.Day))
        {
            if (day.Load is not 0 || day.Pv is not (0 or null)) continue;
            result.Add(new(day.Day, Outage));
            var next = day.Day.AddDays(1);
            if (byDay.TryGetValue(next, out var following) && following.Load is > 0)
                result.Add(new(next, CatchUp));
        }
        return result.DistinctBy(f => f.Day).ToArray();
    }
}

public record MonthlyCost(string Month, int Days, int PricedDays, decimal Load, decimal Pv, decimal Import, decimal Export,
    decimal ImportCost, decimal ExportRevenue, decimal Fixed, decimal Net, decimal WithoutPv, decimal WithoutBattery,
    decimal PvSavings, decimal BatterySavings);
public record CostResult(MonthlyCost[] Months, MonthlyCost Total, bool HasImportPrice, bool HasExportPrice,
    int Days, int PricedDays, string[] Notes);

/// <summary>
/// Costs per month from daily energies and the stored tariffs. Two counterfactuals use the same prices:
/// without PV the whole household load is imported; without the battery its discharge would have been imported
/// and its charge exported. Monthly fixed fees are part of every variant and do not change the savings.
/// </summary>
public static class Economics
{
    public static CostResult Evaluate(DailyValues[] daily, Tariffs tariffs, DateOnly today)
    {
        var flags = DayFlags.Detect(daily).ToDictionary(f => f.Day, f => f.Kind);
        var days = daily.Where(d => d.Day < today && d.Load is >= 0 && d.Import is >= 0 && d.Export is >= 0).ToArray();
        var months = days.GroupBy(d => d.Day.ToString("yyyy-MM", CultureInfo.InvariantCulture)).OrderBy(g => g.Key)
            .Select(g => Month(g.Key, g.ToArray(), tariffs)).ToArray();
        var total = months.Aggregate(new MonthlyCost("total", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), (a, m) => new(
            "total", a.Days + m.Days, a.PricedDays + m.PricedDays, a.Load + m.Load, a.Pv + m.Pv, a.Import + m.Import,
            a.Export + m.Export, a.ImportCost + m.ImportCost, a.ExportRevenue + m.ExportRevenue, a.Fixed + m.Fixed,
            a.Net + m.Net, a.WithoutPv + m.WithoutPv, a.WithoutBattery + m.WithoutBattery, a.PvSavings + m.PvSavings,
            a.BatterySavings + m.BatterySavings));
        bool importPrice = days.Any(d => tariffs.Price(TariffKind.Import, d.Day) is not null);
        bool exportPrice = days.Any(d => tariffs.Price(TariffKind.Export, d.Day) is not null);
        var notes = new List<string>
        {
            "Kosten = Netzbezug × Bezugspreis + Grundpreis − Einspeisung × Vergütung, je Tag mit dem gültigen Tarif.",
            "Ohne PV: gesamter Hausverbrauch aus dem Netz, keine Einspeisung. Ohne Speicher: Entladung wäre Netzbezug, Ladung wäre Einspeisung.",
            "Tage ohne gültigen Bezugspreis fließen nicht in die Beträge ein."
        };
        if (flags.Count > 0) notes.Add($"{flags.Count} Tage gehören zu Ausfällen mit nachgelieferten Werten. Ihre Summe ist korrekt, die Verteilung auf die Tage nicht.");
        return new(months, total, importPrice, exportPrice, days.Length, total.PricedDays, notes.ToArray());
    }

    static MonthlyCost Month(string key, DailyValues[] days, Tariffs tariffs)
    {
        decimal load = 0, pv = 0, import = 0, export = 0, importCost = 0, revenue = 0, fixedCost = 0, withoutPv = 0,
            withoutBattery = 0;
        int priced = 0;
        foreach (var d in days)
        {
            if (tariffs.Price(TariffKind.Import, d.Day) is not { } buy) continue;
            decimal sell = tariffs.Price(TariffKind.Export, d.Day) ?? 0;
            decimal fee = (tariffs.Price(TariffKind.Monthly, d.Day) ?? 0) / DateTime.DaysInMonth(d.Day.Year, d.Day.Month);
            priced++;
            load += d.Load!.Value; pv += d.Pv ?? 0; import += d.Import!.Value; export += d.Export!.Value;
            importCost += d.Import.Value * buy;
            revenue += d.Export.Value * sell;
            fixedCost += fee;
            withoutPv += d.Load.Value * buy + fee;
            decimal charge = d.Charge ?? 0, discharge = d.Discharge ?? 0;
            withoutBattery += (d.Import.Value + discharge) * buy - (d.Export.Value + charge) * sell + fee;
        }
        decimal net = importCost + fixedCost - revenue;
        return new(key, days.Length, priced, load, pv, import, export, Round(importCost), Round(revenue), Round(fixedCost),
            Round(net), Round(withoutPv), Round(withoutBattery), Round(withoutPv - net), Round(withoutBattery - net));
    }

    static decimal Round(decimal value) => decimal.Round(value, 2);
}

public record BatteryYear(int Year, int Days, decimal Charge, decimal Discharge, decimal? Efficiency, decimal? Cycles,
    decimal? CyclesPerYear, decimal? LoadShare, int FullDays, decimal? MaxCharge);
public record BatteryMonth(int Month, int Days, decimal AverageDischarge, decimal? Efficiency, decimal FullShare,
    Dictionary<int, decimal> DischargeByYear, Dictionary<int, decimal?> EfficiencyByYear);
public record BatteryHealthResult(BatteryYear[] Years, BatteryMonth[] Months, decimal? Capacity, bool CapacityEstimated,
    decimal TotalCycles, int Days, DateOnly? First, string[] Findings, string[] Notes);

/// <summary>
/// Long-term battery use from daily charge and discharge. Efficiency = discharge ÷ charge over the period (the
/// stored-energy change at the boundaries is small against yearly throughput). Equivalent full cycles =
/// discharge ÷ usable capacity. Outage and catch-up days are excluded because they split energy across days.
/// </summary>
public static class BatteryHealth
{
    public static BatteryHealthResult Evaluate(DailyValues[] daily, decimal? capacity, bool estimated, DateOnly today)
    {
        var excluded = DayFlags.Detect(daily).Select(f => f.Day).ToHashSet();
        var days = daily.Where(d => d.Day < today && d.Charge is >= 0 && d.Discharge is >= 0 && !excluded.Contains(d.Day))
            .OrderBy(d => d.Day).ToArray();
        decimal full = capacity is > 0 ? capacity.Value * .85m : decimal.MaxValue;
        var years = days.GroupBy(d => d.Day.Year).Select(g =>
        {
            decimal charge = g.Sum(d => d.Charge!.Value), discharge = g.Sum(d => d.Discharge!.Value);
            decimal load = g.Where(d => d.Load is > 0).Sum(d => d.Load!.Value);
            decimal? cycles = capacity is > 0 ? decimal.Round(discharge / capacity.Value, 0) : null;
            return new BatteryYear(g.Key, g.Count(), decimal.Round(charge, 1), decimal.Round(discharge, 1),
                charge > 0 ? decimal.Round(discharge / charge, 3) : null, cycles,
                cycles is null ? null : decimal.Round(cycles.Value * 365 / g.Count(), 0),
                load > 0 ? decimal.Round(discharge / load, 3) : null, g.Count(d => d.Charge >= full),
                g.Max(d => d.Charge));
        }).ToArray();
        var months = days.GroupBy(d => d.Day.Month).OrderBy(g => g.Key).Select(g =>
        {
            decimal charge = g.Sum(d => d.Charge!.Value), discharge = g.Sum(d => d.Discharge!.Value);
            return new BatteryMonth(g.Key, g.Count(), decimal.Round(discharge / g.Count(), 2),
                charge > 0 ? decimal.Round(discharge / charge, 3) : null,
                decimal.Round((decimal)g.Count(d => d.Charge >= full) / g.Count(), 3),
                g.GroupBy(d => d.Day.Year).ToDictionary(y => y.Key, y => decimal.Round(y.Average(d => d.Discharge!.Value), 2)),
                g.GroupBy(d => d.Day.Year).ToDictionary(y => y.Key, y => y.Sum(d => d.Charge!.Value) is > 0 and var c
                    ? decimal.Round(y.Sum(d => d.Discharge!.Value) / c, 3) : (decimal?)null));
        }).ToArray();

        var findings = new List<string>();
        var complete = years.Where(y => y.Days >= 300 && y.Efficiency is not null).ToArray();
        if (complete.Length >= 2)
        {
            var first = complete[0];
            var last = complete[^1];
            decimal change = (last.Efficiency!.Value - first.Efficiency!.Value) * 100;
            findings.Add(Math.Abs(change) < 2
                ? $"Der Wirkungsgrad ist stabil: {Pct(first.Efficiency)} ({first.Year}) und {Pct(last.Efficiency)} ({last.Year})."
                : $"Der Wirkungsgrad hat sich von {Pct(first.Efficiency)} ({first.Year}) auf {Pct(last.Efficiency)} ({last.Year}) verändert.");
        }
        var winter = months.Where(m => m.Month is 11 or 12 or 1).ToArray();
        var summer = months.Where(m => m.Month is >= 4 and <= 9).ToArray();
        if (winter.Length > 0 && summer.Length > 0)
            findings.Add($"Im Winter (Nov–Jan) gibt der Speicher im Mittel {Kwh(winter.Average(m => m.AverageDischarge))} am Tag ab, " +
                $"im Sommerhalbjahr {Kwh(summer.Average(m => m.AverageDischarge))}. " +
                (capacity is > 0 ? $"Voll geladen wird er im Winter an {Pct(winter.Average(m => m.FullShare))} der Tage, im Sommer an {Pct(summer.Average(m => m.FullShare))}." : ""));
        var winterEfficiency = winter.Where(m => m.Efficiency is not null).Select(m => m.Efficiency!.Value).ToArray();
        var summerEfficiency = summer.Where(m => m.Efficiency is not null).Select(m => m.Efficiency!.Value).ToArray();
        if (winterEfficiency.Length > 0 && summerEfficiency.Length > 0 && summerEfficiency.Average() - winterEfficiency.Average() > .03m)
            findings.Add($"Bei wenig Durchsatz im Winter sinkt der Wirkungsgrad auf {Pct(winterEfficiency.Average())} " +
                $"(Sommer {Pct(summerEfficiency.Average())}). Eigenverbrauch und Wandlungsverluste fallen dann stärker ins Gewicht.");
        decimal totalCycles = capacity is > 0 ? decimal.Round(days.Sum(d => d.Discharge!.Value) / capacity.Value, 0) : 0;
        var notes = new List<string>
        {
            "Wirkungsgrad = Entladung ÷ Ladung im Zeitraum. Er enthält Lade- und Entladeverluste sowie den Eigenverbrauch des Speichers.",
            "Vollzyklen = Entladung ÷ nutzbare Kapazität. „Voll geladen“ zählt Tage mit einer Ladung von mindestens 85 % der Kapazität.",
            "Ein Kalendertag kann Teile zweier Zyklen enthalten (Nachtentladung vom Vortag und Abendentladung). Tageswerte über der Kapazität sind deshalb normal.",
            "Eine Kapazitätsabnahme lässt sich erst mit Ladezustandsdaten mehrerer Jahre belegen."
        };
        if (excluded.Count > 0) notes.Add($"{excluded.Count} Ausfall- und Nachholtage sind nicht enthalten.");
        return new(years, months, capacity, estimated, totalCycles, days.Length, days.FirstOrDefault()?.Day,
            findings.ToArray(), notes.ToArray());
    }

    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static string Pct(decimal? value) => value is null ? "—" : (value.Value * 100).ToString("N1", German) + " %";
    static string Kwh(decimal value) => value.ToString("N1", German) + " kWh";
}
