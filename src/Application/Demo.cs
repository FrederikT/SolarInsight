using SolarInsight.Domain;

namespace SolarInsight.Application;

public static class Demo
{
    public static readonly DateOnly Start = new(2024, 11, 1);
    public static readonly DateOnly End = new(2026, 11, 1);
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    public static readonly Battery Battery = new(9.6m, 4, 4, 0.94m, 0.94m, 0.1m);
    public static readonly Tariff[] Prices =
    [
        new(Guid.Parse("42132079-aa6e-4bee-8bfd-385b6108ea16"), TariffKind.Import, Start, new DateOnly(2026, 1, 1), .30m),
        new(Guid.Parse("47132079-aa6e-4bee-8bfd-385b6108ea16"), TariffKind.Import, new DateOnly(2026, 1, 1), null, .34m),
        new(Guid.Parse("43132079-aa6e-4bee-8bfd-385b6108ea16"), TariffKind.Export, Start, null, .08m),
        new(Guid.Parse("44132079-aa6e-4bee-8bfd-385b6108ea16"), TariffKind.Monthly, Start, null, 12)
    ];

    public static Interval[] Generate()
    {
        uint seed = 17293;
        double Next()
        {
            seed = unchecked(seed * 1664525 + 1013904223);
            return seed / 4294967296d;
        }

        var rows = new List<Interval>(211000);
        decimal stored = 0;
        DateOnly previous = default;
        double weather = 0;
        for (var at = Calendar.Utc(Start, Zone); at < Calendar.Utc(End, Zone); at = at.AddMinutes(5))
        {
            var local = TimeZoneInfo.ConvertTime(at, Zone);
            var day = DateOnly.FromDateTime(local.DateTime);
            if (day != previous)
            {
                weather = .22 + .78 * Next();
                previous = day;
            }
            double season = .5 + .5 * Math.Cos((local.DayOfYear - 172) * Math.Tau / 365.25);
            double hour = local.TimeOfDay.TotalHours + 1d / 24;
            double daylight = 8 + 8 * season;
            double sun = Math.Max(0, 1 - Math.Abs(hour - 13) / (daylight / 2));
            decimal pv = (decimal)(Math.Pow(sun, 1.6) * (4 + 7 * season) * weather * (.8 + .2 * Next())) / 12;
            decimal load = (decimal)(.3 + .2 * (1 - season) + .2 * Next() +
                .65 * Math.Exp(-Math.Pow((hour - 7) / 1.1, 2)) +
                1.4 * Math.Exp(-Math.Pow((hour - 19) / 2.1, 2))) / 12;
            var flow = Dispatch.Step(Battery, stored, 1m / 12, pv, load);
            rows.Add(new(at, at.AddMinutes(5), pv, load, flow.Import, flow.Export, flow.Charge, flow.Discharge,
                Battery.MinSoc + flow.Stored / Battery.Capacity, Battery.MinSoc + stored / Battery.Capacity, "synthetic", "demo-17293-v1"));
            stored = flow.Stored;
        }
        return rows.ToArray();
    }
}

