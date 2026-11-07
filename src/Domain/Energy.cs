namespace SolarInsight.Domain;

public record Battery(decimal Capacity, decimal ChargeKw, decimal DischargeKw, decimal ChargeEfficiency,
    decimal DischargeEfficiency, decimal MinSoc = 0, decimal MaxSoc = 1)
{
    public decimal Usable => Capacity * (MaxSoc - MinSoc);

    public void Validate()
    {
        if (Capacity is < 0 or > 500 || ChargeKw is < 0 or > 500 || DischargeKw is < 0 or > 500 ||
            ChargeEfficiency is <= 0 or > 1 || DischargeEfficiency is <= 0 or > 1 ||
            MinSoc < 0 || MaxSoc > 1 || MinSoc >= MaxSoc)
            throw new ArgumentException("Ungültige Speicherparameter.");
    }
}

public record Interval(DateTimeOffset Start, DateTimeOffset End, decimal? Pv, decimal? Load,
    decimal? Import = null, decimal? Export = null, decimal? Charge = null, decimal? Discharge = null,
    decimal? Soc = null, decimal? OpeningSoc = null, string Quality = "interval-energy", string SourceRevision = "1")
{
    public decimal Hours => (decimal)(End.UtcTicks - Start.UtcTicks) / TimeSpan.TicksPerHour;
}

public record Flow(decimal Direct, decimal Import, decimal Export, decimal Charge, decimal Discharge,
    decimal ChargeLoss, decimal DischargeLoss, decimal Opening, decimal Stored,
    decimal AddedPv, decimal AddedBattery, decimal AddedGrid, decimal AddedOpeningBattery,
    decimal OpeningInventory);

public static class Dispatch
{
    public static Flow Step(Battery battery, decimal stored, decimal hours, decimal pv, decimal household,
        decimal added = 0, decimal openingInventory = 0)
    {
        if (hours <= 0 || pv < 0 || household < 0 || added < 0 || stored < 0 || stored > battery.Usable ||
            openingInventory < 0 || openingInventory > stored)
            throw new ArgumentException("Ungültiges Energieintervall.");
        decimal directHouse = Math.Min(pv, household);
        decimal directAdded = Math.Min(pv - directHouse, added);
        decimal direct = directHouse + directAdded;
        decimal surplus = pv - direct;
        decimal deficit = household + added - direct;
        decimal charge = Math.Min(surplus, Math.Min(battery.ChargeKw * hours,
            (battery.Usable - stored) / battery.ChargeEfficiency));
        decimal gain = Math.Min(battery.Usable - stored, charge * battery.ChargeEfficiency);
        decimal available = stored + gain;
        decimal discharge = Math.Min(deficit, Math.Min(battery.DischargeKw * hours,
            available * battery.DischargeEfficiency));
        decimal withdrawn = Math.Min(available, discharge / battery.DischargeEfficiency);
        decimal fromOpening = Math.Min(openingInventory, withdrawn);
        decimal addedBattery = Math.Max(0, discharge - (household - directHouse));
        decimal addedOpening = Math.Clamp(fromOpening * battery.DischargeEfficiency -
            (household - directHouse), 0, addedBattery);
        return new(direct, deficit - discharge, surplus - charge, charge, discharge, charge - gain,
            withdrawn - discharge, stored, available - withdrawn, directAdded, addedBattery,
            added - directAdded - addedBattery, addedOpening, openingInventory - fromOpening);
    }
}

public enum Semantics { IntervalEnergy, MeanPower, InstantaneousPower, Counter }
public record Sample(DateTimeOffset At, decimal? Value);
public record NormalizedEnergy(decimal? Kwh, string Quality);
public static class Normalization
{
    public static NormalizedEnergy Energy(Semantics type, Sample left, Sample right, TimeSpan expected)
    {
        if (expected <= TimeSpan.Zero || right.At <= left.At) throw new ArgumentException("Ungültige Messzeit.");
        if (left.Value is not >= 0) return new(null, "missing");
        decimal hours = (decimal)(right.At - left.At).Ticks / TimeSpan.TicksPerHour;
        if (type == Semantics.IntervalEnergy) return new(left.Value, "measured");
        if (type == Semantics.MeanPower) return new(left.Value * hours, "integrated");
        if (right.Value is not >= 0) return new(null, "missing");
        if (type == Semantics.Counter && right.Value < left.Value) return new(null, "reset");
        if ((right.At - left.At).Ticks > expected.Ticks * 1.5m) return new(null, "gap");
        return type switch
        {
            Semantics.Counter => new(right.Value - left.Value, "difference"),
            Semantics.InstantaneousPower => new((left.Value + right.Value) / 2 * hours, "approximate"),
            _ => throw new ArgumentException("Unbekannte Messsemantik.")
        };
    }
}

