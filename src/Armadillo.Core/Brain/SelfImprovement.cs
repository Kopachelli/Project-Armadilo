using Armadillo.Core.Llm;
using Armadillo.Core.Persistence;

namespace Armadillo.Core.Brain;

/// <summary>How far the brain may change itself. The user's chosen ceiling is <see cref="Autonomous"/>.</summary>
public enum AutonomyLevel
{
    /// <summary>Only capture/retrieve; never change playbooks.</summary>
    CaptureOnly,
    /// <summary>Propose + A/B test, but a human must approve promotion.</summary>
    Adaptive,
    /// <summary>Promote automatically when a candidate beats the incumbent by the margin (still gated by
    /// measurement, versioning, auto-rollback, audit, and the kill switch).</summary>
    Autonomous,
}

public sealed record SelfImprovementOptions
{
    /// <summary>Candidate must beat incumbent score by at least this to be promoted.</summary>
    public double PromotionMargin { get; init; } = 0.10;
    public AutonomyLevel Autonomy { get; init; } = AutonomyLevel.Autonomous;
}

public sealed record ImprovementDecision(string PlaybookName, string Outcome, int? ActiveVersion, string Detail);

/// <summary>Scores how well a persona/system-prompt performs on a set of tasks (0..1). Pluggable so the
/// promotion logic is testable without spawning real agents.</summary>
public interface IPlaybookEvaluator
{
    Task<double> ScoreAsync(string personaText, IReadOnlyList<string> tasks, CancellationToken ct = default);
}

/// <summary>Proposes an improved playbook from the incumbent + recent learnings. Pluggable so promotion
/// logic is testable without an LLM. Default impl uses a local model.</summary>
public interface IPlaybookProposer
{
    Task<string?> ProposeAsync(string name, string incumbentText, IReadOnlyList<LearningRecord> learnings,
        CancellationToken ct = default);
}

/// <summary>Local-model proposer (Ollama). Returns null when no model is configured.</summary>
public sealed class OllamaProposer : IPlaybookProposer
{
    private const string System =
        "You improve an AI agent's system prompt. Given the CURRENT prompt and RECENT OUTCOMES, write an " +
        "improved system prompt that fixes recurring failure patterns while staying general. Output ONLY " +
        "the new system prompt text — no preamble, no markdown fences.";

    private readonly OllamaClient _ollama;
    private readonly string? _model;

    public OllamaProposer(OllamaClient ollama, string? model)
    {
        _ollama = ollama;
        _model = model;
    }

    public async Task<string?> ProposeAsync(string name, string incumbentText,
        IReadOnlyList<LearningRecord> learnings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_model)) return null;
        var learningText = learnings.Count == 0
            ? "(no recorded outcomes yet)"
            : string.Join("\n", learnings.Select(l => $"- ({l.Confidence:0.00}) {l.Text}"));
        var user = $"CURRENT PROMPT:\n{incumbentText}\n\nRECENT OUTCOMES:\n{learningText}";
        return await _ollama.ChatAsync(_model!, System, user, json: false, ct).ConfigureAwait(false);
    }
}

public interface ISelfImprovementEngine
{
    bool KillSwitchEngaged { get; }
    void SetKillSwitch(bool on);

    Task<string?> ProposeAsync(string playbookName, string seedPersona, CancellationToken ct = default);
    Task<ImprovementDecision> RunCycleAsync(string playbookName, string seedPersona,
        IReadOnlyList<string> evalTasks, CancellationToken ct = default);
    Task<ImprovementDecision> RollbackAsync(string playbookName, CancellationToken ct = default);
}

/// <summary>
/// The autonomous self-improvement loop: propose a better playbook (local model) → A/B evaluate vs the
/// incumbent on held-out tasks → promote ONLY on a measured win (margin) → fully versioned with
/// auto-rollback, append-only audit, and a global kill switch. Autonomy is earned by measurement, never
/// blind.
/// </summary>
public sealed class SelfImprovementEngine : ISelfImprovementEngine
{
    private const string KillSwitchKey = "kill_switch";

    private readonly IStore _store;
    private readonly IPlaybookProposer _proposer;
    private readonly IPlaybookEvaluator _evaluator;
    private readonly SelfImprovementOptions _opts;
    private readonly Action<string>? _log;

    public SelfImprovementEngine(IStore store, IPlaybookProposer proposer, IPlaybookEvaluator evaluator,
        SelfImprovementOptions? opts = null, Action<string>? log = null)
    {
        _store = store;
        _proposer = proposer;
        _evaluator = evaluator;
        _opts = opts ?? new SelfImprovementOptions();
        _log = log;
    }

    public bool KillSwitchEngaged => _store.GetSetting(KillSwitchKey) == "on";

    public void SetKillSwitch(bool on)
    {
        _store.SetSetting(KillSwitchKey, on ? "on" : "off");
        _store.Audit("self-improvement", on ? "kill-switch-engaged" : "kill-switch-released", KillSwitchKey);
    }

    public async Task<string?> ProposeAsync(string playbookName, string seedPersona, CancellationToken ct = default)
    {
        var incumbent = _store.GetActivePlaybook(playbookName)?.Text ?? seedPersona;
        var learnings = _store.RecentLearnings(playbookName, 20);
        var candidate = (await _proposer.ProposeAsync(playbookName, incumbent, learnings, ct).ConfigureAwait(false))?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate == incumbent.Trim()) return null;
        return candidate;
    }

    public async Task<ImprovementDecision> RunCycleAsync(string playbookName, string seedPersona,
        IReadOnlyList<string> evalTasks, CancellationToken ct = default)
    {
        if (KillSwitchEngaged)
            return Record(playbookName, 0, 0, 0, 0, "halted", "kill switch engaged");

        if (evalTasks.Count == 0)
            return new ImprovementDecision(playbookName, "no-eval-tasks", ActiveVersion(playbookName), "provide tasks to measure against");

        var incumbent = _store.GetActivePlaybook(playbookName);
        var incumbentText = incumbent?.Text ?? seedPersona;
        var incumbentVersion = incumbent?.Version ?? 0;

        var candidateText = await ProposeAsync(playbookName, seedPersona, ct).ConfigureAwait(false);
        if (candidateText is null)
            return new ImprovementDecision(playbookName, "no-proposal", incumbentVersion,
                "no propose model configured or no improvement suggested");

        // Ensure the seed exists as a versioned, rollback-able baseline.
        if (incumbent is null)
        {
            _store.SavePlaybook(new PlaybookRecord
            {
                Name = playbookName, Version = 0, Text = seedPersona, Score = 0, Active = true,
                SourceNote = "seed", CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        _log?.Invoke($"[self-improve] evaluating candidate for '{playbookName}' over {evalTasks.Count} task(s)…");
        var incumbentScore = await _evaluator.ScoreAsync(incumbentText, evalTasks, ct).ConfigureAwait(false);
        var candidateScore = await _evaluator.ScoreAsync(candidateText, evalTasks, ct).ConfigureAwait(false);

        var nextVersion = (_store.GetPlaybookVersions(playbookName).Select(p => p.Version).DefaultIfEmpty(0).Max()) + 1;
        var beatsBy = candidateScore - incumbentScore;

        if (beatsBy >= _opts.PromotionMargin && _opts.Autonomy == AutonomyLevel.Autonomous)
        {
            _store.SavePlaybook(new PlaybookRecord
            {
                Name = playbookName, Version = nextVersion, Text = candidateText, Score = candidateScore,
                Active = false, SourceNote = "auto-promoted", CreatedAt = DateTimeOffset.UtcNow,
            });
            _store.SetActivePlaybook(playbookName, nextVersion);
            _log?.Invoke($"[self-improve] PROMOTED '{playbookName}' v{incumbentVersion}->v{nextVersion} " +
                         $"({incumbentScore:0.00} -> {candidateScore:0.00})");
            return Record(playbookName, incumbentVersion, nextVersion, incumbentScore, candidateScore,
                "promoted", $"beats incumbent by {beatsBy:0.00}");
        }

        if (beatsBy >= _opts.PromotionMargin && _opts.Autonomy == AutonomyLevel.Adaptive)
        {
            _store.SavePlaybook(new PlaybookRecord
            {
                Name = playbookName, Version = nextVersion, Text = candidateText, Score = candidateScore,
                Active = false, SourceNote = "staged-for-approval", CreatedAt = DateTimeOffset.UtcNow,
            });
            return Record(playbookName, incumbentVersion, nextVersion, incumbentScore, candidateScore,
                "staged", $"awaiting human approval (beats by {beatsBy:0.00})");
        }

        _store.SavePlaybook(new PlaybookRecord
        {
            Name = playbookName, Version = nextVersion, Text = candidateText, Score = candidateScore,
            Active = false, SourceNote = "rejected", CreatedAt = DateTimeOffset.UtcNow,
        });
        return Record(playbookName, incumbentVersion, nextVersion, incumbentScore, candidateScore,
            "rejected", $"did not beat incumbent by margin ({beatsBy:0.00} < {_opts.PromotionMargin:0.00})");
    }

    public Task<ImprovementDecision> RollbackAsync(string playbookName, CancellationToken ct = default)
    {
        var versions = _store.GetPlaybookVersions(playbookName).OrderBy(p => p.Version).ToList();
        var active = versions.FirstOrDefault(p => p.Active);
        if (active is null || versions.Count < 2)
            return Task.FromResult(new ImprovementDecision(playbookName, "nothing-to-rollback", active?.Version, ""));

        var previous = versions.Where(p => p.Version < active.Version).OrderByDescending(p => p.Version).FirstOrDefault();
        if (previous is null)
            return Task.FromResult(new ImprovementDecision(playbookName, "nothing-to-rollback", active.Version, "already at oldest"));

        _store.SetActivePlaybook(playbookName, previous.Version);
        _store.Audit("self-improvement", "rolledback", playbookName, $"v{active.Version} -> v{previous.Version}");
        return Task.FromResult(Record(playbookName, active.Version, previous.Version, active.Score, previous.Score,
            "rolledback", $"reverted to v{previous.Version}"));
    }

    private int? ActiveVersion(string name) => _store.GetActivePlaybook(name)?.Version;

    private ImprovementDecision Record(string name, int incV, int candV, double incS, double candS,
        string outcome, string detail)
    {
        _store.SaveExperiment(new ExperimentRecord
        {
            ExperimentId = Ids.New("exp"), PlaybookName = name, IncumbentVersion = incV, CandidateVersion = candV,
            IncumbentScore = incS, CandidateScore = candS, SampleSize = 0, Outcome = outcome, Detail = detail,
            At = DateTimeOffset.UtcNow,
        });
        _store.Audit("self-improvement", outcome, name, detail);
        return new ImprovementDecision(name, outcome, ActiveVersion(name), detail);
    }
}
