using System.Text;
using Armadillo.Core.Adapters;
using Armadillo.Core.Llm;
using Armadillo.Core.Persistence;
using Armadillo.Core.Review;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Brain;

/// <summary>
/// The brain v0: capture every reviewed run as a distilled learning (semantic memory) and retrieve
/// relevant learnings to inject into future briefs (RAG). Embedding-based retrieval when a local
/// embed model is available; recency fallback otherwise. Self-improvement (A/B promotion) builds on
/// this in Phase 4.
/// </summary>
public interface IBrain
{
    Task<string> RetrieveContextAsync(string persona, string task, int k = 5, CancellationToken ct = default);

    Task CaptureAsync(JobRecord job, SessionRecord session, AdapterResult result, ReviewResult review,
        CancellationToken ct = default);
}

public sealed class Brain : IBrain
{
    private readonly IStore _store;
    private readonly OllamaClient _ollama;
    private readonly string? _embedModel;

    public Brain(IStore store, OllamaClient ollama, string? embedModel)
    {
        _store = store;
        _ollama = ollama;
        _embedModel = embedModel;
    }

    public async Task<string> RetrieveContextAsync(string persona, string task, int k = 5,
        CancellationToken ct = default)
    {
        IReadOnlyList<LearningRecord> hits;

        var queryVec = _embedModel is null ? null : await _ollama.EmbedAsync(_embedModel, task, ct).ConfigureAwait(false);
        if (queryVec is not null)
        {
            var candidates = _store.LearningsWithEmbeddings();
            hits = candidates
                .Where(l => l.Embedding is not null)
                .Select(l => (l, score: Cosine(queryVec, l.Embedding!)))
                .OrderByDescending(x => x.score)
                .Take(k)
                .Select(x => x.l)
                .ToList();
        }
        else
        {
            hits = _store.RecentLearnings(persona, k);
        }

        if (hits.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("Relevant learnings from prior runs (most useful first):");
        foreach (var l in hits)
            sb.AppendLine($"- [{l.Tool}] (conf {l.Confidence:0.00}) {l.Text}");
        return sb.ToString();
    }

    public async Task CaptureAsync(JobRecord job, SessionRecord session, AdapterResult result,
        ReviewResult review, CancellationToken ct = default)
    {
        var text = Distill(job, result, review);
        var embedding = _embedModel is null
            ? null
            : await _ollama.EmbedAsync(_embedModel, $"{job.Task}\n{text}", ct).ConfigureAwait(false);

        _store.SaveLearning(new LearningRecord
        {
            LearningId = Ids.New("lrn"),
            Persona = job.Persona,
            Tool = job.Tool,
            Text = text,
            Confidence = review.Verdict == Verdict.Skipped ? 0.3 : review.Confidence,
            SourceJob = job.JobId,
            Embedding = embedding,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static string Distill(JobRecord job, AdapterResult result, ReviewResult review)
    {
        var verdict = review.Verdict == Verdict.Skipped ? "unreviewed" : review.Verdict.ToString().ToLowerInvariant();
        var rationale = string.IsNullOrWhiteSpace(review.Rationale) ? "" : $" — {review.Rationale}";
        var taskHint = Summarize(job.Task, 120);
        return $"Task \"{taskHint}\" via {job.Tool}: {verdict}{rationale}";
    }

    private static string Summarize(string s, int max)
    {
        s = s.Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static double Cosine(float[] a, float[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < n; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
