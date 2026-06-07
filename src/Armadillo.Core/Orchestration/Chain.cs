using Armadillo.Core.Review;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Orchestration;

/// <summary>One step in a cross-tool pipeline. The task template may reference {goal} (the original
/// input) and {input} (the previous step's output); with no placeholder, the previous output is
/// appended as context.</summary>
public sealed record ChainStep(string Name, ToolId Tool, string Persona, string TaskTemplate);

public sealed record ChainStepOutcome(
    string Name, ToolId Tool, string JobId, bool Ok, string Output, Verdict Verdict, double Confidence);

public sealed record ChainResult(IReadOnlyList<ChainStepOutcome> Steps, string FinalText, bool Ok);

/// <summary>
/// Runs an ordered, cross-tool chain: the output of step N feeds step N+1. Different steps can use
/// different tools (implementer → reviewer → tester across Claude/Codex/Gemini/…). Stops on the first
/// failed step. Decoupled from the concrete dispatcher (takes a dispatch delegate) for testability.
/// </summary>
public sealed class ChainRunner
{
    private readonly Func<SpawnRequest, CancellationToken, Task<SpawnOutcome>> _dispatch;

    public ChainRunner(Func<SpawnRequest, CancellationToken, Task<SpawnOutcome>> dispatch)
        => _dispatch = dispatch;

    public static ChainRunner For(AgentDispatcher dispatcher)
        => new((req, ct) => dispatcher.DispatchAsync(req, ct));

    public async Task<ChainResult> RunAsync(IReadOnlyList<ChainStep> steps, string goal,
        SpawnLineage? lineage = null, string? repoPath = null, CancellationToken ct = default)
    {
        lineage ??= SpawnLineage.NewRoot();
        var outcomes = new List<ChainStepOutcome>();
        var prev = "";
        var ok = true;

        foreach (var step in steps)
        {
            var task = Render(step.TaskTemplate, goal, prev);
            var outcome = await _dispatch(new SpawnRequest
            {
                Persona = step.Persona,
                Task = task,
                Tool = step.Tool,
                Lineage = lineage,
                RepoPath = repoPath,
            }, ct).ConfigureAwait(false);

            outcomes.Add(new ChainStepOutcome(step.Name, step.Tool, outcome.JobId, outcome.Ok,
                outcome.FinalText, outcome.Verdict, outcome.Confidence));
            prev = outcome.FinalText;
            if (!outcome.Ok) { ok = false; break; }
        }

        return new ChainResult(outcomes, prev, ok);
    }

    public static string Render(string template, string goal, string prev)
    {
        if (template.Contains("{goal}") || template.Contains("{input}"))
            return template.Replace("{goal}", goal).Replace("{input}", prev);
        return prev.Length == 0 ? template : $"{template}\n\n--- Previous step output ---\n{prev}";
    }
}
