namespace SolarInsight.Domain;

public record Window(int From, int To)
{
    public void Validate()
    {
        if (From < 0 || To > 1440 || From >= To) throw new ArgumentException("Ungültiges Tageszeitfenster.");
    }

    public decimal Hours(Interval row, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(row.Start, zone);
        decimal minute = (decimal)local.TimeOfDay.Ticks / TimeSpan.TicksPerMinute;
        return Math.Max(0, Math.Min(To, minute + row.Hours * 60) - Math.Max(From, minute)) / 60;
    }
}

public record Shift(decimal Daily, decimal Share, decimal TargetKw, Window Source, Window Target);
public record Added(decimal Daily, decimal Kw, Window Window, int[] Days, bool Full, decimal[]? Monthly = null);
public record Move(DateTimeOffset Source, DateTimeOffset Target, decimal Kwh);
public record Simulated(Interval[] Input, Flow[] Flows, decimal[] Added, Move[] Moves,
    decimal Requested, int Resets, decimal Initial, bool Limited)
{
    public Interval[] Series() => Input.Select((row, i) => row with
    {
        Load = row.Load + Added[i], Import = Flows[i].Import, Export = Flows[i].Export,
        Charge = Flows[i].Charge, Discharge = Flows[i].Discharge
    }).ToArray();
}

public static class Scenario
{
    const decimal Epsilon = 0.000000001m;

    public static Simulated Run(Interval[] rows, Battery battery, TimeZoneInfo zone, decimal initial = 0,
        Shift? shift = null, Added? added = null, CancellationToken ct = default, Tariffs? tariffs = null)
    {
        battery.Validate();
        if (rows.Length == 0 || rows.Any(r => r.Pv is not >= 0 || r.Load is not >= 0 || r.End <= r.Start))
            throw new ArgumentException("PV und Hausverbrauch werden auf gemeinsamer Zeitachse benötigt.");
        for (int i = 1; i < rows.Length; i++)
            if (rows[i].Start < rows[i - 1].End) throw new ArgumentException("Überlappende oder unsortierte Intervalle.");
        if (initial < 0) throw new ArgumentException("Ungültiger Anfangsbestand.");
        initial = Math.Min(battery.Usable, initial);

        rows = (Interval[])rows.Clone();
        var loads = new decimal[rows.Length];
        var extra = new decimal[rows.Length];
        var flows = new Flow[rows.Length];
        var moves = new List<Move>();
        var days = Enumerable.Range(0, rows.Length).GroupBy(i => Calendar.Day(rows[i].Start, zone)).ToArray();
        bool limited = false;
        decimal requested = 0;
        int resets = 0;

        Flow Calculate(int i, Flow previous, decimal change = 0)
        {
            bool continued = i > 0 && rows[i - 1].End == rows[i].Start;
            decimal stored = i == 0 ? initial : continued ? previous.Stored : 0;
            decimal opening = i == 0 ? initial : continued ? previous.OpeningInventory : 0;
            return Dispatch.Step(battery, stored, rows[i].Hours, rows[i].Pv!.Value, loads[i] + change,
                extra[i], opening);
        }

        for (int i = 0; i < rows.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            loads[i] = rows[i].Load!.Value;
            if (i > 0 && rows[i - 1].End != rows[i].Start) resets++;
            flows[i] = Calculate(i, i == 0 ? default! : flows[i - 1]);
        }

        if (shift is not null)
        {
            if (shift.Source is null || shift.Target is null) throw new ArgumentException("Verschiebungsfenster fehlen.");
            shift.Source.Validate();
            shift.Target.Validate();
            if (shift.Daily is < 0 or > 100 || shift.Share is < 0 or > 1 || shift.TargetKw is < 0 or > 100)
                throw new ArgumentException("Ungültige Verschiebungsparameter.");
            int operations = 0;
            var trial = new List<Flow>();
            decimal? UnitCost(Interval row, TariffKind kind)
            {
                var charge = tariffs?.Charge(kind, row, 1, zone);
                return charge is { } value && Math.Abs(value.Hours - row.Hours) < Epsilon ? value.Value : null;
            }
            var rates = tariffs is null ? null : rows.Select(r =>
                (Import: UnitCost(r, TariffKind.Import), Export: UnitCost(r, TariffKind.Export))).ToArray();
            foreach (var day in days)
            {
                decimal budget = shift.Daily;
                var source = day.ToDictionary(i => i, i => rows[i].Load!.Value * shift.Share *
                    shift.Source.Hours(rows[i], zone) / rows[i].Hours);
                var target = day.ToDictionary(i => i, i => shift.TargetKw * shift.Target.Hours(rows[i], zone));
                while (budget > Epsilon && !limited)
                {
                    ct.ThrowIfCancellationRequested();
                    // With both moves bounded by residual grid flow, dispatch at source and target
                    // keeps the original storage state. Verify the affected sequence before committing.
                    var sources = day.Where(i => source[i] > Epsilon && flows[i].Import > Epsilon)
                        .Select(i => (Index: i, Available: Math.Min(source[i], Math.Min(loads[i], flows[i].Import))))
                        .ToArray();
                    var targets = day.Where(i => target[i] > Epsilon && flows[i].Export > Epsilon)
                        .Select(i => (Index: i, Available: Math.Min(target[i], flows[i].Export))).ToArray();
                    if (sources.Length == 0 || targets.Length == 0) break;
                    decimal amount = Math.Min(Math.Min(.05m, budget),
                        Math.Min(sources.Max(x => x.Available), targets.Max(x => x.Available)));
                    if (amount <= Epsilon) break;

                    int s = sources.Where(x => x.Available >= amount)
                        .OrderByDescending(x => rates?[x.Index].Import)
                        .ThenByDescending(x => flows[x.Index].Import).ThenBy(x => x.Index).First().Index;
                    int t = targets.Where(x => x.Available >= amount)
                        .OrderBy(x => rates?[x.Index].Export).ThenBy(x => x.Index).First().Index;
                    trial.Clear();
                    int start = Math.Min(s, t), last = Math.Max(s, t);
                    Flow previous = start == 0 ? default! : flows[start - 1];
                    decimal delta = 0;
                    for (int i = start; i < rows.Length; i++)
                    {
                        if (++operations > 2_000_000) { limited = true; break; }
                        previous = Calculate(i, previous, i == s ? -amount : i == t ? amount : 0);
                        trial.Add(previous);
                        delta += previous.Import - flows[i].Import;
                        if (i >= last && previous.Stored == flows[i].Stored &&
                            previous.OpeningInventory == flows[i].OpeningInventory) break;
                    }
                    if (limited) break;
                    if (delta >= -Epsilon) { limited = true; break; }
                    for (int i = 0; i < trial.Count; i++) flows[start + i] = trial[i];
                    loads[s] -= amount;
                    loads[t] += amount;
                    source[s] -= amount;
                    target[t] -= amount;
                    budget -= amount;
                    moves.Add(new(rows[s].Start, rows[t].Start, amount));
                }
                if (limited) break;
            }
        }

        if (added is not null)
        {
            if (added.Window is null) throw new ArgumentException("Zeitfenster für Zusatzbedarf fehlt.");
            added.Window.Validate();
            if (added.Daily is < 0 or > 500 || added.Kw is < 0 or > 100 ||
                added.Days is null || added.Days.Any(d => d is < 0 or > 6) ||
                added.Monthly is { Length: not 12 } || added.Monthly?.Any(f => f is < 0 or > 5) == true)
                throw new ArgumentException("Ungültige Zusatzbedarfsparameter.");

            foreach (var day in days.Where(d => added.Days.Contains((int)d.Key.DayOfWeek)))
            {
                ct.ThrowIfCancellationRequested();
                decimal left = added.Daily * (added.Monthly?[day.Key.Month - 1] ?? 1);
                requested += left;
                var limits = day.ToDictionary(i => i, i => added.Kw * added.Window.Hours(rows[i], zone));
                if (added.Full && left > limits.Values.Sum() + Epsilon)
                    throw new ArgumentException($"Das Tagesziel am {day.Key:dd.MM.yyyy} überschreitet die verfügbare Leistung oder Datenabdeckung.");
                foreach (int i in day)
                {
                    extra[i] = Math.Min(left, Math.Min(limits[i], flows[i].Export *
                        added.Window.Hours(rows[i], zone) / rows[i].Hours));
                    left -= extra[i];
                }
                if (added.Full)
                    foreach (int i in day)
                    {
                        decimal energy = Math.Min(left, limits[i] - extra[i]);
                        extra[i] += energy;
                        left -= energy;
                    }
            }
            for (int i = 0; i < rows.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                flows[i] = added.Full
                    ? Calculate(i, i == 0 ? default! : flows[i - 1])
                    : flows[i] with { Direct = flows[i].Direct + extra[i],
                        Export = flows[i].Export - extra[i], AddedPv = extra[i] };
            }
        }

        for (int i = 0; i < rows.Length; i++) rows[i] = rows[i] with { Load = loads[i] };
        return new(rows, flows, extra, moves.ToArray(), requested, resets, initial, limited);
    }
}

