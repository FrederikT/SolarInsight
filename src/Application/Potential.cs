using SolarInsight.Domain;

namespace SolarInsight.Application;

public record ScenarioParameters(Period Period, Battery? Battery = null, Shift? Shift = null, Added? Added = null,
    bool Exploratory = false, decimal? Investment = null, decimal? CurveMax = null, bool EmptyStart = false);
public record Comparison(decimal Pv, decimal Load, decimal Import, decimal Export, decimal Loss, decimal End,
    decimal? Autarky, Cost Cost);
public record Validation(decimal ImportError, decimal ExportError, decimal NormalizedError, decimal? SocMaePoints,
    bool Sufficient, bool SocValidated);
public record AddedResult(decimal Requested, decimal Served, decimal Pv, decimal Battery, decimal OpeningBattery,
    decimal Grid, decimal Unserved, decimal ExtraGrid, decimal RemainingExport, decimal? TotalCostChange,
    decimal? SavingsAgainstGrid);
public record CurvePoint(decimal Capacity, decimal AvoidedImport, decimal? Savings, decimal? MarginalImport);
public record DailyResult(DateOnly Day, decimal? MeasuredImport, decimal ReferenceImport, decimal ScenarioImport,
    decimal? MeasuredExport, decimal ReferenceExport, decimal ScenarioExport);
public record ModelPoint(DateTimeOffset Start, decimal ImportKw, decimal ExportKw, decimal Soc, decimal LoadKw);
public record PotentialResult(ScenarioParameters Parameters, string Revision, string Engine, string TimeZone,
    decimal Coverage, double ResolutionMinutes, Comparison Reference, Comparison Scenario, Metrics Measured,
    Validation Validation, decimal? Savings, decimal? PaybackYears, string PaybackStatus, AddedResult? Additional,
    CurvePoint[] Curve, DailyResult[] Daily, Move[] Moves, int Resets, decimal InitialReference,
    decimal InitialScenario, string[] Warnings, Dictionary<DateOnly, ModelPoint[]> Model);

public static class Potential
{
    public static PotentialResult Evaluate(ScenarioParameters request, Interval[] data, Battery baseline,
        TimeZoneInfo zone, Tariffs tariffs, string revision, CancellationToken ct = default,
        Action<int>? progress = null)
    {
        var rows = data.Where(r => r.Pv.HasValue && r.Load.HasValue).ToArray();
        if (rows.Length == 0) throw new ArgumentException("Keine gemeinsamen PV- und Verbrauchsdaten vorhanden.");
        decimal hours = (decimal)(Calendar.Utc(request.Period.End, zone) -
            Calendar.Utc(request.Period.Start, zone)).Ticks / TimeSpan.TicksPerHour;
        var measured = Analysis.Summarize(data, hours, true);
        decimal coverage = measured.Coverage;
        double resolution = rows.Max(r => (r.End - r.Start).TotalMinutes);
        bool exploratory = coverage < .95m || resolution > 15;
        if (exploratory && !request.Exploratory)
            throw new ArgumentException("Unter 95 % Abdeckung oder über 15 Minuten Auflösung ist nur ein explorativer Lauf möglich.");
        var battery = request.Battery ?? baseline;
        battery.Validate();
        if (request.Investment is < 0 or > 1000000 || request.CurveMax is < 0 or > 500)
            throw new ArgumentException("Investition oder Kurvengrenze außerhalb des zulässigen Bereichs.");
        decimal initial = !request.EmptyStart && rows[0].Start == Calendar.Utc(request.Period.Start, zone) &&
            rows[0].OpeningSoc is { } soc && soc >= baseline.MinSoc && soc <= baseline.MaxSoc
            ? baseline.Capacity * (soc - baseline.MinSoc) : 0;
        var reference = Domain.Scenario.Run(rows, baseline, zone, initial, ct: ct);
        var scenario = request.Battery is null && request.Shift is null && request.Added is null ? reference :
            Domain.Scenario.Run(rows, battery, zone, initial, request.Shift, request.Added, ct, tariffs);
        if (request.Added is { } added)
        {
            var measuredDays = rows.Select(r => Calendar.Day(r.Start, zone)).ToHashSet();
            decimal missing = 0;
            for (var day = request.Period.Start; day < request.Period.End; day = day.AddDays(1))
                if (!measuredDays.Contains(day) && added.Days.Contains((int)day.DayOfWeek))
                    missing += added.Daily * (added.Monthly?[day.Month - 1] ?? 1);
            if (missing > 0 && added.Full)
                throw new ArgumentException("Vollständige Zusatzbedarfsdeckung ist an Tagen ohne gemeinsame Daten nicht möglich.");
            scenario = scenario with { Requested = scenario.Requested + missing };
        }
        progress?.Invoke(45);

        Comparison Summarize(Simulated result)
        {
            decimal load = result.Input.Sum(r => r.Load!.Value) + result.Added.Sum();
            decimal import = result.Flows.Sum(r => r.Import);
            return new(result.Input.Sum(r => r.Pv!.Value), load, import, result.Flows.Sum(r => r.Export),
                result.Flows.Sum(r => r.ChargeLoss + r.DischargeLoss), result.Flows[^1].Stored,
                load == 0 ? null : (load - import) / load,
                tariffs.Evaluate(result.Series(), request.Period, zone));
        }
        var refResult = Summarize(reference);
        var scResult = ReferenceEquals(reference, scenario) ? refResult : Summarize(scenario);
        var gridPairs = rows.Select((row, i) => (row, flow: reference.Flows[i]))
            .Where(x => x.row.Import.HasValue && x.row.Export.HasValue).ToArray();
        decimal importError = gridPairs.Sum(x => x.flow.Import - x.row.Import!.Value);
        decimal exportError = gridPairs.Sum(x => x.flow.Export - x.row.Export!.Value);
        decimal error = (Math.Abs(importError) + Math.Abs(exportError)) /
            Math.Max(1, gridPairs.Sum(x => x.row.Pv!.Value + x.row.Load!.Value));
        var socErrors = rows.Select((row, i) => (row, flow: reference.Flows[i])).Where(x => x.row.Soc.HasValue)
            .Select(x => (x.row.Hours, Error: Math.Abs(x.row.Soc!.Value - baseline.MinSoc -
                (baseline.Capacity > 0 ? x.flow.Stored / baseline.Capacity : 0)) * 100)).ToArray();
        decimal? mae = socErrors.Length == 0 ? null : socErrors.Sum(x => x.Hours * x.Error) / socErrors.Sum(x => x.Hours);
        bool socValidated = mae is <= 10 && socErrors.Sum(x => x.Hours) / hours >= .95m;
        int completeDays = gridPairs.GroupBy(x => Calendar.Day(x.row.Start, zone)).Count(g =>
            Math.Abs(g.Sum(x => x.row.Hours) - (decimal)(Calendar.Utc(g.Key.AddDays(1), zone) -
                Calendar.Utc(g.Key, zone)).TotalHours) < .000001m);
        var validation = new Validation(importError, exportError, error, mae,
            !exploratory && gridPairs.Sum(x => x.row.Hours) / hours >= .95m && completeDays >= 7 &&
            error <= .05m && (mae is null or <= 10), socValidated);
        decimal? savings = refResult.Cost.Net - scResult.Cost.Net;
        decimal? payback = null;
        string paybackStatus = "Nur bei validierter Speichererweiterung und zwölf vollständigen Monaten.";
        if (request.Investment is { } investment && battery.Usable > baseline.Usable &&
            request.Shift is null && request.Added is null && validation.Sufficient && validation.SocValidated &&
            request.Period.Start.Day == 1 && request.Period.Start.AddMonths(12) == request.Period.End &&
            coverage >= .999999m && reference.Resets == 0 && savings.HasValue)
        {
            payback = savings > 0 ? investment / savings : null;
            paybackStatus = savings > 0 ? "Statisch, ohne Alterung und künftige Tarifänderungen." :
                "Unter diesen Annahmen keine statische Amortisation.";
        }
        AddedResult? additional = null;
        if (request.Added is not null)
        {
            var house = Summarize(Domain.Scenario.Run(rows, battery, zone, initial, request.Shift, ct: ct, tariffs: tariffs));
            decimal served = scenario.Added.Sum();
            var grid = rows.Select((row, i) => tariffs.Charge(TariffKind.Import, row, scenario.Added[i], zone)).Sum(x => x.Value);
            additional = new(scenario.Requested, served, scenario.Flows.Sum(f => f.AddedPv),
                scenario.Flows.Sum(f => f.AddedBattery), scenario.Flows.Sum(f => f.AddedOpeningBattery),
                scenario.Flows.Sum(f => f.AddedGrid), scenario.Requested - served,
                scResult.Import - house.Import, scResult.Export, scResult.Cost.Net - house.Cost.Net,
                house.Cost.Net + grid - scResult.Cost.Net);
        }

        var curve = new List<CurvePoint>();
        if (request.CurveMax is { } max)
        {
            max = Math.Max(max, Math.Max(battery.Capacity, baseline.Capacity));
            var capacities = Enumerable.Range(0, 19).Select(i => decimal.Round(max * i / 18, 3))
                .Append(baseline.Capacity).Append(battery.Capacity).Distinct().Order().ToArray();
            var curveBase = Summarize(Domain.Scenario.Run(rows, battery with { Capacity = baseline.Capacity },
                zone, initial, ct: ct));
            foreach (var capacity in capacities)
            {
                ct.ThrowIfCancellationRequested();
                var result = Summarize(Domain.Scenario.Run(rows, battery with { Capacity = capacity }, zone, initial, ct: ct));
                decimal avoided = curveBase.Import - result.Import;
                var last = curve.LastOrDefault();
                curve.Add(new(capacity, avoided, curveBase.Cost.Net - result.Cost.Net,
                    last is null ? null : (avoided - last.AvoidedImport) / (capacity - last.Capacity)));
                progress?.Invoke(45 + 50 * curve.Count / capacities.Length);
            }
        }
        var dates = Enumerable.Range(0, rows.Length).GroupBy(i => Calendar.Day(rows[i].Start, zone)).ToArray();
        var daily = dates.Select(g => new DailyResult(g.Key, NullableSum(g.Select(i => rows[i].Import)),
            g.Sum(i => reference.Flows[i].Import), g.Sum(i => scenario.Flows[i].Import),
            NullableSum(g.Select(i => rows[i].Export)), g.Sum(i => reference.Flows[i].Export),
            g.Sum(i => scenario.Flows[i].Export))).ToArray();
        var model = dates.ToDictionary(g => g.Key, g => g.Select(i => new ModelPoint(rows[i].Start,
            scenario.Flows[i].Import / rows[i].Hours, scenario.Flows[i].Export / rows[i].Hours,
            battery.MinSoc + (battery.Capacity == 0 ? 0 : scenario.Flows[i].Stored / battery.Capacity),
            (scenario.Input[i].Load!.Value + scenario.Added[i]) / rows[i].Hours)).ToArray());
        var warnings = new List<string> { "Historischer Rückblick. AC-Intervallmodell ohne Netzladen und Batterieexport.",
            "Der Endbestand ist in der Ersparnis nicht gutgeschrieben." };
        if (!validation.Sufficient) warnings.Add("Explorative Bewertung: Referenzvalidierung oder Datenqualität reicht nicht aus.");
        if (!validation.SocValidated) warnings.Add("Speicherzustand nicht ausreichend validiert.");
        if (scenario.Limited) warnings.Add("Suche begrenzt.");
        if (scenario.Resets > 0) warnings.Add($"{scenario.Resets} Speicherneustarts nach Datenlücken.");
        if (initial > scenario.Initial) warnings.Add("Der bekannte Anfangsbestand wurde auf die Szenariokapazität begrenzt.");
        if (initial == 0) warnings.Add("Start mit leerem nutzbarem Speicher.");
        return new(request, revision, "1.0", zone.Id, coverage, resolution, refResult, scResult, measured, validation,
            savings, payback, paybackStatus, additional, curve.ToArray(), daily, scenario.Moves, scenario.Resets,
            reference.Initial, scenario.Initial, warnings.ToArray(), model);
    }

    static decimal? NullableSum(IEnumerable<decimal?> values)
    {
        var valid = values.OfType<decimal>().ToArray();
        return valid.Length == 0 ? null : valid.Sum();
    }
}

