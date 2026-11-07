namespace SolarInsight.Domain;

public enum TariffKind { Import, Export, Monthly }
public record Tariff(Guid Id, TariffKind Kind, DateOnly Start, DateOnly? End, decimal Price);
public record Cost(decimal Import, decimal Export, decimal Fixed, decimal ImportCoverage, decimal ExportCoverage,
    bool Estimated, bool Complete)
{
    public decimal CoveredNet => Import + Fixed - Export;
    public decimal? Net => Complete ? CoveredNet : null;
}

public sealed class Tariffs
{
    readonly ILookup<TariffKind, Tariff> periods;
    public Tariffs(IEnumerable<Tariff> source)
    {
        periods = source.OrderBy(t => t.Start).ToLookup(t => t.Kind);
        foreach (var group in periods)
        {
            Tariff? previous = null;
            foreach (var tariff in group)
            {
                if (!Enum.IsDefined(tariff.Kind) || tariff.End <= tariff.Start || tariff.Price is < 0 or > 10000 ||
                    (previous is not null && (previous.End is null || previous.End > tariff.Start)))
                    throw new ArgumentException("Ungültiger oder überlappender Tarifzeitraum.");
                previous = tariff;
            }
        }
    }

    public decimal? Price(TariffKind kind, DateOnly day) =>
        periods[kind].FirstOrDefault(t => t.Start <= day && (t.End is null || day < t.End))?.Price;

    public (decimal Value, decimal Hours, bool Estimated) Charge(TariffKind kind, Interval row,
        decimal energy, TimeZoneInfo zone)
    {
        var day = Calendar.Day(row.Start, zone);
        decimal amount = 0, hours = 0;
        bool estimated = false;
        for (var cursor = row.Start; cursor < row.End; day = day.AddDays(1))
        {
            var boundary = Calendar.Utc(day.AddDays(1), zone);
            var end = row.End < boundary ? row.End : boundary;
            decimal duration = (decimal)(end - cursor).Ticks / TimeSpan.TicksPerHour;
            if (Price(kind, day) is { } price)
            {
                hours += duration;
                amount += price * energy * duration / row.Hours;
            }
            if (end < row.End && Price(kind, day) != Price(kind, day.AddDays(1))) estimated = true;
            cursor = end;
        }
        return (amount, hours, estimated);
    }

    public Cost Evaluate(IEnumerable<Interval> rows, Period period, TimeZoneInfo zone)
    {
        decimal import = 0, export = 0, importHours = 0, exportHours = 0, fixedCost = 0;
        bool estimated = false;
        foreach (var row in rows)
        {
            if (row.Import is { } i)
            {
                var charge = Charge(TariffKind.Import, row, i, zone);
                import += charge.Value;
                importHours += charge.Hours;
                estimated |= charge.Estimated;
            }
            if (row.Export is { } e)
            {
                var charge = Charge(TariffKind.Export, row, e, zone);
                export += charge.Value;
                exportHours += charge.Hours;
                estimated |= charge.Estimated;
            }
        }
        for (var day = period.Start; day < period.End; day = day.AddDays(1))
            fixedCost += (Price(TariffKind.Monthly, day) ?? 0) / DateTime.DaysInMonth(day.Year, day.Month);
        decimal hours = (decimal)(Calendar.Utc(period.End, zone) - Calendar.Utc(period.Start, zone)).Ticks /
            TimeSpan.TicksPerHour;
        return new(import, export, fixedCost, hours > 0 ? importHours / hours : 0,
            hours > 0 ? exportHours / hours : 0, estimated, hours > 0 &&
            Math.Abs(importHours - hours) < 0.000001m && Math.Abs(exportHours - hours) < 0.000001m);
    }
}

