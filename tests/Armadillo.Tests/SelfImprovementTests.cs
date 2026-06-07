using Armadillo.Core.Brain;
using Armadillo.Core.Persistence;
using Xunit;

namespace Armadillo.Tests;

public class SelfImprovementTests : IDisposable
{
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"armadillo-test-{Guid.NewGuid():N}.db");
    private readonly SqliteStore _store;

    public SelfImprovementTests()
    {
        _store = new SqliteStore(_dbFile);
        _store.Initialize();
    }

    [Fact]
    public async Task Promotes_candidate_when_it_beats_incumbent_by_margin()
    {
        var engine = Engine(candidate: "BETTER PROMPT",
            score: text => text == "BETTER PROMPT" ? 0.9 : 0.5,
            autonomy: AutonomyLevel.Autonomous);

        var d = await engine.RunCycleAsync("helper", "SEED PROMPT", new[] { "t1" });

        Assert.Equal("promoted", d.Outcome);
        var active = _store.GetActivePlaybook("helper");
        Assert.NotNull(active);
        Assert.Equal("BETTER PROMPT", active!.Text);
        Assert.True(active.Version >= 1);
    }

    [Fact]
    public async Task Rejects_candidate_that_does_not_beat_incumbent()
    {
        var engine = Engine(candidate: "MEH PROMPT", score: _ => 0.5, autonomy: AutonomyLevel.Autonomous);

        var d = await engine.RunCycleAsync("helper", "SEED PROMPT", new[] { "t1" });

        Assert.Equal("rejected", d.Outcome);
        var active = _store.GetActivePlaybook("helper");
        Assert.Equal("SEED PROMPT", active!.Text); // seed stays active
    }

    [Fact]
    public async Task Adaptive_stages_instead_of_promoting()
    {
        var engine = Engine(candidate: "BETTER", score: t => t == "BETTER" ? 0.95 : 0.4, autonomy: AutonomyLevel.Adaptive);

        var d = await engine.RunCycleAsync("helper", "SEED", new[] { "t1" });

        Assert.Equal("staged", d.Outcome);
        Assert.Equal("SEED", _store.GetActivePlaybook("helper")!.Text); // not auto-promoted
    }

    [Fact]
    public async Task Kill_switch_halts_self_modification()
    {
        var engine = Engine(candidate: "BETTER", score: _ => 0.99, autonomy: AutonomyLevel.Autonomous);
        engine.SetKillSwitch(true);

        var d = await engine.RunCycleAsync("helper", "SEED", new[] { "t1" });

        Assert.Equal("halted", d.Outcome);
        Assert.Null(_store.GetActivePlaybook("helper")); // nothing created
    }

    [Fact]
    public async Task Rollback_reverts_to_previous_version()
    {
        var engine = Engine(candidate: "V1", score: t => t == "V1" ? 0.9 : 0.5, autonomy: AutonomyLevel.Autonomous);
        await engine.RunCycleAsync("helper", "SEED", new[] { "t1" }); // promotes V1 (seed becomes v0)

        var d = await engine.RollbackAsync("helper");

        Assert.Equal("rolledback", d.Outcome);
        Assert.Equal("SEED", _store.GetActivePlaybook("helper")!.Text);
    }

    private SelfImprovementEngine Engine(string candidate, Func<string, double> score, AutonomyLevel autonomy)
        => new(_store, new FakeProposer(candidate), new FakeEvaluator(score),
            new SelfImprovementOptions { Autonomy = autonomy, PromotionMargin = 0.10 });

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_dbFile); } catch { }
    }

    private sealed class FakeProposer(string candidate) : IPlaybookProposer
    {
        public Task<string?> ProposeAsync(string name, string incumbentText, IReadOnlyList<LearningRecord> learnings,
            CancellationToken ct = default) => Task.FromResult<string?>(candidate);
    }

    private sealed class FakeEvaluator(Func<string, double> score) : IPlaybookEvaluator
    {
        public Task<double> ScoreAsync(string personaText, IReadOnlyList<string> tasks, CancellationToken ct = default)
            => Task.FromResult(score(personaText));
    }
}
