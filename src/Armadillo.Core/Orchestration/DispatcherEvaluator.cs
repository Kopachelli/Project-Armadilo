using Armadillo.Core.Brain;
using Armadillo.Core.Review;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Orchestration;

/// <summary>
/// Real playbook evaluator: runs each eval task through the dispatcher with the given persona and scores
/// it by the local reviewer's verdict/confidence. This is what makes self-improvement's A/B promotion
/// grounded in measured outcomes (it spawns real agents, so it's an opt-in, operator-triggered path).
/// </summary>
public sealed class DispatcherEvaluator : IPlaybookEvaluator
{
    private readonly AgentDispatcher _dispatcher;
    private readonly ToolId _tool;

    public DispatcherEvaluator(AgentDispatcher dispatcher, ToolId tool = ToolId.Claude)
    {
        _dispatcher = dispatcher;
        _tool = tool;
    }

    public async Task<double> ScoreAsync(string personaText, IReadOnlyList<string> tasks, CancellationToken ct = default)
    {
        if (tasks.Count == 0) return 0;
        double total = 0;
        foreach (var task in tasks)
        {
            var outcome = await _dispatcher.DispatchAsync(new SpawnRequest
            {
                Persona = personaText,
                Task = task,
                Tool = _tool,
                Lineage = SpawnLineage.NewRoot(),
            }, ct).ConfigureAwait(false);

            total += outcome.Verdict switch
            {
                Verdict.Approve => outcome.Confidence,
                Verdict.Revise => outcome.Confidence * 0.5,
                Verdict.Skipped => 0.5,   // no reviewer available -> neutral
                _ => 0.0,                 // reject / escalate
            };
        }
        return total / tasks.Count;
    }
}
