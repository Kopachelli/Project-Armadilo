using Armadillo.Core.Persistence;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Orchestration;

/// <summary>
/// The Registrator's "decide what to call". Resolution order: an explicitly-requested available tool →
/// otherwise the best installed tool ranked by LEARNED priors (approve-weighted review scores from past
/// runs) with a neutral baseline for untested tools, tie-broken by a static priority. This is where the
/// self-learning loop closes into tool selection.
/// </summary>
public interface IRouter
{
    Task<ToolId?> ChooseAsync(ToolId? preferred, CancellationToken ct = default);
}

public sealed class Router : IRouter
{
    // Richest/most-reliable first (tie-breaker + fallback when there's no learned data).
    private static readonly ToolId[] Priority =
    {
        ToolId.Claude, ToolId.Cursor, ToolId.Codex, ToolId.Antigravity, ToolId.Gemini,
        ToolId.Qwen, ToolId.Kimi, ToolId.MiniMax, ToolId.Copilot, ToolId.OpenCode,
        ToolId.Hermes, ToolId.OpenClaw, ToolId.Pi,
    };

    private const int MinSamples = 3;     // don't trust a prior below this many reviewed runs
    private const double Baseline = 0.5;  // untested tools sit mid-rank; a measured-good tool rises above

    private readonly IToolDetector _detector;
    private readonly AdapterRegistry _adapters;
    private readonly IStore _store;

    public Router(IToolDetector detector, AdapterRegistry adapters, IStore store)
    {
        _detector = detector;
        _adapters = adapters;
        _store = store;
    }

    public async Task<ToolId?> ChooseAsync(ToolId? preferred, CancellationToken ct = default)
    {
        // 1. Explicit request wins if it's drivable + installed.
        if (preferred is { } p && _adapters.Supports(p) && (await _detector.DetectAsync(p, ct)).Installed)
            return p;

        // 2. Otherwise rank installed+supported tools by learned prior (neutral baseline), then priority.
        var snapshot = await _detector.DetectAsync(ct);
        var installed = snapshot.Tools.Where(t => t.Installed).Select(t => t.Id).ToHashSet();
        var candidates = Priority.Where(t => _adapters.Supports(t) && installed.Contains(t)).ToList();
        if (candidates.Count == 0) return null;

        var priors = _store.ToolPriors()
            .Where(pr => pr.Samples >= MinSamples)
            .ToDictionary(pr => pr.Tool, pr => pr.Score);

        double Score(ToolId t) => priors.TryGetValue(t, out var s) ? s : Baseline;

        return candidates
            .OrderByDescending(Score)
            .ThenBy(t => Array.IndexOf(Priority, t))
            .First();
    }
}
