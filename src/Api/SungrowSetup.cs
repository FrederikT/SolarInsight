using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SolarInsight.Domain;
using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public sealed class SungrowSetup(IConfiguration configuration)
{
    static readonly JsonSerializerOptions ProfileJson = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true
    };

    public string DefaultZone => configuration["Sungrow:DefaultZone"] ?? "Europe/Berlin";

    // Published plant-level daily energy points. Live, SOC and interval semantics
    // need a confirmed profile for the actual installation.
    static readonly AggregateMapping StandardDaily = new(83022, 83118, 83102, 83072, null, null);
    static readonly SungrowMapping Standard = new(null, null, null, null, null,
        true, true, true, false, true, false, StandardDaily);

    public SungrowMapping Resolve(string providerId, SungrowMapping? saved)
    {
        var path = configuration["Sungrow:MappingFile"];
        if (string.IsNullOrWhiteSpace(path)) return saved ?? Standard;
        SungrowMapping mapping;
        try
        {
            using var profile = JsonDocument.Parse(File.ReadAllText(path));
            if (profile.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            if (!profile.RootElement.TryGetProperty(providerId, out var entry)) return saved ?? Standard;
            mapping = entry.Deserialize<SungrowMapping>(ProfileJson)
                ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ArgumentException("Die Betreiberdatei für die Messpunkte ist nicht lesbar oder ungültig.", ex);
        }
        mapping.Validate();
        if (mapping.Live is { Confirmed: false })
            throw new ArgumentException("Das Betreiberprofil enthält eine unbestätigte Live-Zuordnung.");
        return mapping;
    }

    // Callers hold the per-plant import lock (or are creating a new plant in this transaction).
    public async Task PlanInitial(Store db, Plant plant, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Imports.AnyAsync(job => job.PlantId == plant.Id &&
            (job.AutomaticInitial || job.End - job.Start > ProviderEndpoints.MaxTestWindow), ct))
            return;
        if (await db.Imports.AnyAsync(job => job.PlantId == plant.Id && (job.Status == "pending" || job.Status == "running"), ct))
            throw new ArgumentException("Ein Testimport läuft. Bestätige die Anlage erneut, wenn der Test beendet ist.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plant.Zone);
        var today = Calendar.Day(now, zone);
        var days = Math.Clamp(configuration.GetValue("Sungrow:InitialHistoryDays", 30), 1, 366);
        var start = Calendar.Utc(today.AddDays(-days), zone);
        db.Imports.Add(new() { Id = Guid.NewGuid(), PlantId = plant.Id, Start = start, AutomaticInitial = true,
            Checkpoint = start, End = Calendar.Utc(today, zone) });
    }
}
