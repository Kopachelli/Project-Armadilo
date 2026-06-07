using Armadillo.Core.Orchestration;
using Xunit;

namespace Armadillo.Tests;

public class GovernorTests
{
    [Fact]
    public void Denies_when_depth_exceeds_max()
    {
        var g = new Governor(new GovernorOptions { MaxDepth = 2 });
        Assert.True(g.Authorize(new SpawnLineage("r", 2)).Allowed);
        var denied = g.Authorize(new SpawnLineage("r", 3));
        Assert.False(denied.Allowed);
        Assert.Contains("depth", denied.Reason);
    }

    [Fact]
    public void Concurrency_cap_blocks_then_releases()
    {
        var g = new Governor(new GovernorOptions { MaxConcurrent = 2, MaxPerRoot = 100, MaxDepth = 9 });
        var a = g.BeginSession(new SpawnLineage("r", 0));
        var b = g.BeginSession(new SpawnLineage("r", 0));
        Assert.True(a.Acquired);
        Assert.True(b.Acquired);

        var c = g.BeginSession(new SpawnLineage("r", 0));
        Assert.False(c.Acquired);                 // at cap
        Assert.Contains("concurrent", c.Reason);

        a.Release.Dispose();                       // free one slot
        var d = g.BeginSession(new SpawnLineage("r", 0));
        Assert.True(d.Acquired);

        b.Release.Dispose();
        d.Release.Dispose();
    }

    [Fact]
    public void Per_root_total_spawn_budget_is_enforced()
    {
        var g = new Governor(new GovernorOptions { MaxPerRoot = 2, MaxConcurrent = 100, MaxDepth = 9 });
        var s1 = g.BeginSession(new SpawnLineage("root1", 0));
        var s2 = g.BeginSession(new SpawnLineage("root1", 0));
        Assert.True(s1.Acquired);
        Assert.True(s2.Acquired);
        s1.Release.Dispose();                      // releasing concurrency does NOT refund the per-root total
        s2.Release.Dispose();

        var s3 = g.BeginSession(new SpawnLineage("root1", 0));
        Assert.False(s3.Acquired);                 // per-root budget exhausted
        Assert.Contains("per-root", s3.Reason);

        var other = g.BeginSession(new SpawnLineage("root2", 0));
        Assert.True(other.Acquired);               // a different root is unaffected
        other.Release.Dispose();
    }
}
