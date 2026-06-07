using System.Collections.Concurrent;

namespace Armadillo.Core.Orchestration;

/// <summary>Lineage of a spawn: which root request it descends from, and how deep.</summary>
public sealed record SpawnLineage(string RootId, int Depth)
{
    /// <summary>A brand-new top-level request (e.g. the operator's `run`, or an external agent).</summary>
    public static SpawnLineage NewRoot() => new(Armadillo.Core.Ids.New("root"), 0);

    /// <summary>The lineage to hand a child this spawn creates.</summary>
    public SpawnLineage Child() => this with { Depth = Depth + 1 };
}

public sealed record GovernorOptions
{
    public int MaxDepth { get; init; } = 2;
    public int MaxConcurrent { get; init; } = 4;
    public int MaxPerRoot { get; init; } = 20;
}

public sealed record GuardResult(bool Allowed, string? Reason)
{
    public static readonly GuardResult Ok = new(true, null);
    public static GuardResult Deny(string reason) => new(false, reason);
}

/// <summary>
/// The safety valve. A spawned agent can itself call <c>request_agent</c>, so without bounds a
/// single misbehaving agent fork-bombs the machine and the token budget. Enforces max depth,
/// max concurrent sessions, and a per-root total-spawn cap.
/// </summary>
/// <summary>The result of reserving a session slot. Dispose <see cref="Release"/> to free concurrency.</summary>
public sealed record SessionLease(bool Acquired, IDisposable Release, string? Reason);

public interface IGovernor
{
    GuardResult Authorize(SpawnLineage lineage);

    /// <summary>Reserve a slot for a session; dispose the lease to release concurrency.</summary>
    SessionLease BeginSession(SpawnLineage lineage);
}

public sealed class Governor : IGovernor
{
    private readonly GovernorOptions _opts;
    private int _concurrent;
    private readonly ConcurrentDictionary<string, int> _perRoot = new();

    public Governor(GovernorOptions? opts = null) => _opts = opts ?? new GovernorOptions();

    public GuardResult Authorize(SpawnLineage lineage)
    {
        if (lineage.Depth > _opts.MaxDepth)
            return GuardResult.Deny($"max spawn depth {_opts.MaxDepth} exceeded (depth={lineage.Depth})");
        if (Volatile.Read(ref _concurrent) >= _opts.MaxConcurrent)
            return GuardResult.Deny($"max concurrent sessions {_opts.MaxConcurrent} reached");
        if (_perRoot.GetValueOrDefault(lineage.RootId) >= _opts.MaxPerRoot)
            return GuardResult.Deny($"per-root spawn budget {_opts.MaxPerRoot} exhausted for {lineage.RootId}");
        return GuardResult.Ok;
    }

    public SessionLease BeginSession(SpawnLineage lineage)
    {
        var guard = Authorize(lineage);
        if (!guard.Allowed) return new SessionLease(false, NoopDisposable.Instance, guard.Reason);

        Interlocked.Increment(ref _concurrent);
        _perRoot.AddOrUpdate(lineage.RootId, 1, (_, n) => n + 1);
        return new SessionLease(true, new Releaser(this), null);
    }

    private void Release() => Interlocked.Decrement(ref _concurrent);

    private sealed class Releaser(Governor g) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) g.Release(); }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
