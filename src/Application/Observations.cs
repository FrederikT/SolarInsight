using System.Globalization;

namespace SolarInsight.Application;

/// <summary>A statement derived from daily energies. <see cref="Day"/> points to the evidence when it is one day.</summary>
public record Observation(string Kind, string Text, DateOnly? Day, decimal? Kwh);

/// <summary>
/// Period observations from completed provider daily values. They complement the interval-based events, which
/// need 15-minute data, so long periods without intervals still get evidence-backed statements.
/// </summary>
public static class Observations
{
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static string Kwh(decimal value) => value.ToString("N1", German) + " kWh";
    static string Date(DateOnly day) => day.ToString("dd.MM.yyyy", German);

    public static Observation[] FromDaily(DailyValues[] daily, DateOnly today)
    {
        var days = daily.Where(d => d.Day < today).ToArray();
        var result = new List<Observation>();
        if (days.Where(d => d.Pv.HasValue).MaxBy(d => d.Pv) is { Pv: > 0 } best)
            result.Add(new("pv-max", $"Höchste PV-Erzeugung am {Date(best.Day)} · {Kwh(best.Pv!.Value)}.", best.Day, best.Pv));
        if (days.Where(d => d.Import.HasValue).MaxBy(d => d.Import) is { Import: > 0 } worst)
            result.Add(new("import-max", $"Höchster Netzbezug am {Date(worst.Day)} · {Kwh(worst.Import!.Value)}" +
                (worst.Load is > 0 ? $" bei {Kwh(worst.Load.Value)} Hausverbrauch." : "."), worst.Day, worst.Import));

        var paired = days.Where(d => d.Load is > 0 && d.Import.HasValue).ToArray();
        if (paired.Length > 0)
        {
            // 0.1 kWh is the resolution of the Sungrow daily counters.
            var independent = paired.Where(d => d.Import <= .1m).ToArray();
            int streak = 0, longest = 0;
            DateOnly? streakEnd = null, previous = null;
            foreach (var day in paired)
            {
                streak = day.Import <= .1m ? (previous == day.Day.AddDays(-1) ? streak + 1 : 1) : 0;
                if (streak > longest) { longest = streak; streakEnd = day.Day; }
                previous = day.Day;
            }
            result.Add(new("autarkic-days", $"{independent.Length} von {paired.Length} Tagen ohne nennenswerten Netzbezug (≤ 0,1 kWh)" +
                (longest > 1 ? $" · längste Serie {longest} Tage bis {Date(streakEnd!.Value)}." : "."), longest > 1 ? streakEnd : null, null));
            var low = paired.Count(d => d.Import > d.Load / 2);
            if (low > 0) result.Add(new("low-autarky", $"{low} Tage mit mehr als der Hälfte des Hausverbrauchs aus dem Netz.", null, null));
        }
        var surplus = days.Where(d => d.Export is > 0 && d.Load.HasValue && d.Export > d.Load).ToArray();
        if (surplus.Length > 0)
            result.Add(new("surplus-days", $"{surplus.Length} Tage mit mehr Einspeisung als Hausverbrauch · zusammen {Kwh(surplus.Sum(d => d.Export!.Value))} eingespeist.", null, null));
        var battery = days.Where(d => d.Discharge.HasValue && d.Load is > 0).ToArray();
        if (battery.Length > 0)
        {
            decimal share = battery.Sum(d => d.Discharge!.Value) / battery.Sum(d => d.Load!.Value);
            result.Add(new("battery-share", $"Der Speicher deckte an {battery.Length} Tagen mit Batteriewerten {(share * 100).ToString("N0", German)} % des Hausverbrauchs.", null, null));
        }
        return result.ToArray();
    }
}
