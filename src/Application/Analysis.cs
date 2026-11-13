using SolarInsight.Domain;

namespace SolarInsight.Application;

public record Channel(decimal? Kwh, decimal Coverage);
/// <summary>
/// Period metrics. <see cref="AutarkyCoverage"/> and <see cref="RetentionCoverage"/> give the share of the
/// period in which both quantities of the respective ratio were recorded, so a ratio from partial data stays
/// visible together with its basis instead of being suppressed.
/// </summary>
public record Metrics(Channel Pv, Channel Load, Channel Import, Channel Export, Channel Charge, Channel Discharge,
    decimal? Autarky, decimal? Retention, decimal Coverage, decimal? SocMean, decimal? SocMin, decimal? SocMax,
    decimal AutarkyCoverage = 0, decimal RetentionCoverage = 0);
public record EventRule(decimal Kw = .1m, decimal SocTolerance = .02m, int Minutes = 15, decimal? HighLoadKw = null);
public record EnergyEvent(string Type, DateTimeOffset Start, DateTimeOffset End, decimal Kwh, decimal PeakKw,
    decimal? MinSoc, decimal? MaxSoc, string Text, string Rule = "1", decimal Coverage = 1);

public static class Analysis
{
    /// <summary>
    /// Sums the channels and derives the two standard ratios:
    /// Autarkiegrad = (Hausverbrauch − Netzbezug) / Hausverbrauch and
    /// Eigenverbrauchsquote = (PV − Einspeisung) / PV.
    /// Both definitions only need the two metered quantities at the grid connection and the inverter, so they
    /// do not depend on confirmed internal AC paths. They are evaluated over the intervals (or days) where both
    /// quantities exist; the paired share of the period is returned alongside, so missing single values reduce
    /// the stated basis instead of hiding the ratio.
    /// </summary>
    public static Metrics Summarize(Interval[] rows, decimal hours)
    {
        Channel ChannelFor(Func<Interval, decimal?> select)
        {
            var valid = rows.Where(r => select(r) is not null).ToArray();
            return new(valid.Length == 0 ? null : valid.Sum(r => select(r)),
                hours > 0 ? Math.Min(1, valid.Sum(r => r.Hours) / hours) : 0);
        }
        (decimal? Value, decimal Coverage) Ratio(Func<Interval, decimal?> source, Func<Interval, decimal?> sub)
        {
            var valid = rows.Where(r => source(r).HasValue && sub(r).HasValue).ToArray();
            decimal total = valid.Sum(r => source(r)!.Value);
            decimal coverage = hours > 0 ? Math.Min(1, valid.Sum(r => r.Hours) / hours) : 0;
            return (total > 0 ? (total - valid.Sum(r => sub(r)!.Value)) / total : null, coverage);
        }
        var soc = rows.Where(r => r.Soc.HasValue).ToArray();
        var autarky = Ratio(r => r.Load, r => r.Import);
        var retention = Ratio(r => r.Pv, r => r.Export);
        return new(ChannelFor(r => r.Pv), ChannelFor(r => r.Load), ChannelFor(r => r.Import),
            ChannelFor(r => r.Export), ChannelFor(r => r.Charge), ChannelFor(r => r.Discharge),
            autarky.Value, retention.Value,
            hours > 0 ? Math.Min(1, rows.Where(r => r.Pv.HasValue && r.Load.HasValue).Sum(r => r.Hours) / hours) : 0,
            soc.Length == 0 ? null : soc.Sum(r => r.Soc!.Value * r.Hours) / soc.Sum(r => r.Hours),
            soc.Min(r => r.Soc), soc.Max(r => r.Soc), autarky.Coverage, retention.Coverage);
    }

    public static EnergyEvent[] Events(Interval[] rows, Battery? battery, EventRule rule)
    {
        var types = new List<(string Name, Func<Interval, decimal?> Energy)>
        {
            ("Überschussepisode", r => r.Export is { } e && e / r.Hours >= rule.Kw ? e : null)
        };
        if (battery is not null && battery.Usable > 0)
        {
            types.Add(("Einspeisung an oberer Ladegrenze", r =>
                r.Soc >= battery.MaxSoc - rule.SocTolerance && r.Export / r.Hours >= rule.Kw ? r.Export : null));
            types.Add(("Netzbezug an unterer Ladegrenze", r =>
                r.Soc <= battery.MinSoc + rule.SocTolerance && r.Import / r.Hours >= rule.Kw ? r.Import : null));
            types.Add(("Einspeisung bei freiem Speicher", r =>
                r.Soc < battery.MaxSoc - rule.SocTolerance && r.Export / r.Hours >= rule.Kw ? r.Export : null));
        }
        if (rule.HighLoadKw is { } high)
            types.Add(("Hohe Last bei geringer PV-Erzeugung", r =>
                r.Load / r.Hours >= high && r.Pv / r.Hours < rule.Kw ? r.Load : null));
        var result = new List<EnergyEvent>();
        foreach (var (name, select) in types)
        {
            Interval? first = null, last = null;
            decimal total = 0, peak = 0;
            decimal? min = null, max = null;
            void Close()
            {
                if (first is not null && last is not null && (last.End - first.Start).TotalMinutes >= rule.Minutes)
                {
                    var culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
                    string energy = total.ToString("N2", culture);
                    string duration = (last.End - first.Start).TotalMinutes.ToString("N0", culture);
                    result.Add(new(name, first.Start, last.End, total, peak, min, max,
                        $"{name} · {duration} Minuten · {energy} kWh."));
                }
                first = last = null;
                total = peak = 0;
                min = max = null;
            }
            foreach (var row in rows)
            {
                var energy = select(row);
                if (last?.End != row.Start || energy is null) Close();
                if (energy is null) continue;
                first ??= row;
                last = row;
                total += energy.Value;
                peak = Math.Max(peak, energy.Value / row.Hours);
                if (row.Soc is { } soc)
                {
                    min = Math.Min(min ?? soc, soc);
                    max = Math.Max(max ?? soc, soc);
                }
            }
            Close();
        }
        return result.OrderBy(e => e.Start).ThenBy(e => e.Type).ToArray();
    }
}

