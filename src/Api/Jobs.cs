using System.Collections.Concurrent;
using System.Threading.Channels;
using SolarInsight.Application;
using SolarInsight.Domain;

namespace SolarInsight.Api;

public sealed class SimulationJob(Guid owner, Guid plant, ScenarioParameters request, string revision)
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid Owner { get; } = owner;
    public Guid Plant { get; } = plant;
    public ScenarioParameters Request { get; } = request;
    public string Revision { get; } = revision;
    public CancellationTokenSource Cancellation { get; } = new();
    public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
    public volatile int Progress;
    public volatile string State = "queued";
    public volatile string? Error;
    public volatile PotentialResult? Result;
}

public sealed class Jobs(IServiceScopeFactory scopes, ILogger<Jobs> log) : BackgroundService
{
    readonly ConcurrentDictionary<Guid, SimulationJob> jobs = new();
    readonly Channel<SimulationJob> queue = System.Threading.Channels.Channel.CreateBounded<SimulationJob>(8);
    readonly object gate = new();

    public SimulationJob? Submit(Guid owner, Guid plant, ScenarioParameters request, string revision)
    {
        lock (gate)
        {
            foreach (var existing in jobs.Values.Where(j => j.State is not ("queued" or "running") &&
                (j.Owner == owner || j.Created < DateTimeOffset.UtcNow.AddMinutes(-30))))
                if (jobs.TryRemove(existing.Id, out var removed)) removed.Cancellation.Dispose();
            if (jobs.Count >= 32 || jobs.Values.Any(j => j.Owner == owner && j.State is "queued" or "running"))
                return null;
            var job = new SimulationJob(owner, plant, request, revision);
            jobs[job.Id] = job;
            if (queue.Writer.TryWrite(job)) return job;
            jobs.TryRemove(job.Id, out _);
            job.Cancellation.Dispose();
            return null;
        }
    }

    public SimulationJob? Find(Guid owner, Guid id) =>
        jobs.TryGetValue(id, out var job) && job.Owner == owner ? job : null;

    public bool Remove(Guid owner, Guid id)
    {
        lock (gate)
        {
            var job = Find(owner, id);
            if (job is null) return false;
            if (job.State is "queued" or "running") job.Cancellation.Cancel();
            else if (jobs.TryRemove(id, out _)) job.Cancellation.Dispose();
            return true;
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Consume(stoppingToken), Consume(stoppingToken));

    async Task Consume(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            job.State = "running";
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token);
            limit.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                using var scope = scopes.CreateScope();
                var queries = scope.ServiceProvider.GetRequiredService<Queries>();
                var plant = await queries.Plant(limit.Token);
                if (plant.Id != job.Plant || plant.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) != job.Revision)
                    throw new ArgumentException("Die Daten wurden geändert. Bitte erneut rechnen.");
                var view = await queries.Read(plant, job.Request.Period, limit.Token);
                if (view.ConfigurationBoundary) throw new ArgumentException("Zeitraum an der Anlagenänderung aufteilen.");
                if (view.Battery is null || !view.KnownPaths)
                    throw new ArgumentException("Bestätigte Speicherparameter und Energiepfade werden benötigt.");
                if (view.Revision != job.Revision) throw new ArgumentException("Die Daten wurden geändert. Bitte erneut rechnen.");
                job.Progress = 15;
                job.Result = Potential.Evaluate(job.Request, view.Rows, view.Battery, view.Zone, view.Tariffs,
                    view.Revision, limit.Token, n => job.Progress = n);
                limit.Token.ThrowIfCancellationRequested();
                job.Progress = 100;
                job.State = "completed";
            }
            catch (OperationCanceledException)
            {
                job.Result = null;
                job.State = job.Cancellation.IsCancellationRequested ? "cancelled" : "failed";
                job.Error = job.State == "failed" ? "Rechenzeit überschritten." : null;
            }
            catch (ArgumentException ex)
            {
                job.Error = ex.Message;
                job.State = "failed";
            }
            catch (Exception ex)
            {
                log.LogError("Simulation fehlgeschlagen. Kategorie {Category}", ex.GetType().Name);
                job.Error = "Simulation fehlgeschlagen.";
                job.State = "failed";
            }
        }
    }
}

