using SolarInsight.Infrastructure;

namespace SolarInsight.Api;

public sealed record ImportStatusProgress(int Percent, int RemainingWindows, double? Seconds);

public static class ImportProgress
{
    public static int Windows(DateTimeOffset start, DateTimeOffset end, TimeSpan window) =>
        (int)Math.Ceiling(Math.Max(0, (end - start).TotalSeconds) / window.TotalSeconds);

    public static ImportStatusProgress Describe(ImportJob job, TimeSpan window, DateTimeOffset now)
    {
        var fraction = Math.Clamp((job.Checkpoint - job.Start).TotalSeconds / (job.End - job.Start).TotalSeconds, 0, 1);
        var remaining = Windows(job.Checkpoint, job.End, window);
        double? seconds = job.Status == "running" && job.Windows >= 2 && job.Started is { } started
            ? Math.Max(0, (now - started).TotalSeconds) / job.Windows * remaining : null;
        return new((int)Math.Floor(fraction * 100), remaining, seconds);
    }
}

// Persistent job status handles restart and cross-process races. This signal interrupts local I/O.
public sealed class ImportCancellation
{
    readonly object gate = new();
    readonly Dictionary<Guid, CancellationTokenSource> active = [];

    public async Task<bool> Run(Guid id, Func<CancellationToken, Task<bool>> advance, CancellationToken ct)
    {
        using var token = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (gate) active.Add(id, token);
        try { return await advance(token.Token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return false;
        }
        finally { lock (gate) active.Remove(id); }
    }

    public void Signal(Guid id)
    {
        lock (gate)
            if (active.TryGetValue(id, out var token)) token.Cancel();
    }
}
