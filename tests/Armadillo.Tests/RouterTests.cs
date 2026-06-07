using Armadillo.Core;
using Armadillo.Core.Adapters;
using Armadillo.Core.Orchestration;
using Armadillo.Core.Persistence;
using Armadillo.Core.Tools;
using Xunit;

namespace Armadillo.Tests;

public class RouterTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"armadillo-router-{Guid.NewGuid():N}.db");
    private readonly SqliteStore _store;
    private readonly AdapterRegistry _adapters = new(new IToolAdapter[] { new ClaudeAdapter(), new GeminiAdapter() });

    public RouterTests()
    {
        _store = new SqliteStore(_db);
        _store.Initialize();
    }

    [Fact]
    public async Task Explicit_available_tool_is_respected()
    {
        var router = new Router(new FakeDetector(ToolId.Claude, ToolId.Gemini), _adapters, _store);
        Assert.Equal(ToolId.Gemini, await router.ChooseAsync(ToolId.Gemini));
    }

    [Fact]
    public async Task With_no_data_falls_back_to_static_priority()
    {
        var router = new Router(new FakeDetector(ToolId.Claude, ToolId.Gemini), _adapters, _store);
        Assert.Equal(ToolId.Claude, await router.ChooseAsync(null)); // Claude is highest static priority
    }

    [Fact]
    public async Task Learned_priors_pick_the_higher_scoring_tool_over_static_order()
    {
        Seed(ToolId.Gemini, verdict: "approve", confidence: 0.9, n: 5);
        Seed(ToolId.Claude, verdict: "revise", confidence: 0.4, n: 5);

        var router = new Router(new FakeDetector(ToolId.Claude, ToolId.Gemini), _adapters, _store);

        // Gemini measured ~0.9 beats Claude (~0.2 revise-weighted) despite Claude's higher static priority.
        Assert.Equal(ToolId.Gemini, await router.ChooseAsync(null));
    }

    private void Seed(ToolId tool, string verdict, double confidence, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var sid = Ids.New("ses");
            _store.SaveSession(new SessionRecord
            {
                SessionId = sid, JobId = Ids.New("job"), Tool = tool, ExitReason = "completed",
                StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow,
            });
            _store.SaveReview(new ReviewRecord
            {
                ReviewId = Ids.New("rev"), SessionId = sid, ReviewerModel = "m",
                Verdict = verdict, Confidence = confidence, At = DateTimeOffset.UtcNow,
            });
        }
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_db); } catch { }
    }

    private sealed class FakeDetector(params ToolId[] installed) : IToolDetector
    {
        private readonly HashSet<ToolId> _installed = installed.ToHashSet();

        public Task<CapabilitySnapshot> DetectAsync(CancellationToken ct = default) =>
            Task.FromResult(new CapabilitySnapshot(DateTimeOffset.UtcNow,
                ToolDescriptor.All.Select(d => _installed.Contains(d.Id) ? Installed(d) : DetectedTool.NotFound(d)).ToList()));

        public Task<DetectedTool> DetectAsync(ToolId id, CancellationToken ct = default)
        {
            var d = ToolDescriptor.For(id);
            return Task.FromResult(_installed.Contains(id) ? Installed(d) : DetectedTool.NotFound(d));
        }

        private static DetectedTool Installed(ToolDescriptor d) =>
            DetectedTool.NotFound(d) with { Installed = true, ExecutablePath = "x" };
    }
}
