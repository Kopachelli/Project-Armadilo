using System.Text.Json;
using Armadillo.Core.Llm;

namespace Armadillo.Core.Review;

public enum Verdict { Approve, Revise, Reject, Escalate, Skipped }

public sealed record ReviewResult(Verdict Verdict, double Confidence, string Rationale, string ReviewerModel)
{
    public static ReviewResult Skipped(string reason) => new(Verdict.Skipped, 0, reason, "");
}

public interface IReviewer
{
    Task<ReviewResult> ReviewAsync(string task, string output, CancellationToken ct = default);
}

/// <summary>
/// Local-first reviewer: asks an Ollama model for a structured verdict + confidence. Degrades to
/// <see cref="Verdict.Skipped"/> if no local model is available. Verdict parsing ports the TS
/// checker's robustness (JSON first, keyword fallback, "no verdict = escalate").
/// </summary>
public sealed class OllamaReviewer : IReviewer
{
    private const string System =
        "You are a strict, terse code/output reviewer. You are given a TASK and the OUTPUT an AI agent " +
        "produced for it. Judge whether the output satisfies the task. Respond ONLY with JSON: " +
        "{\"verdict\":\"approve|revise|reject\",\"confidence\":<0-100>,\"rationale\":\"one sentence\"}.";

    private readonly OllamaClient _ollama;
    private readonly string? _model;

    public OllamaReviewer(OllamaClient ollama, string? model)
    {
        _ollama = ollama;
        _model = model;
    }

    public async Task<ReviewResult> ReviewAsync(string task, string output, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_model))
            return ReviewResult.Skipped("no local review model configured");

        var user = $"TASK:\n{task}\n\n=== OUTPUT START ===\n{Trim(output, 12000)}\n=== OUTPUT END ===";
        var raw = await _ollama.ChatAsync(_model, System, user, json: true, ct).ConfigureAwait(false);
        if (raw is null) return ReviewResult.Skipped("local review model unavailable");

        return Interpret(raw, _model);
    }

    public static ReviewResult Interpret(string raw, string model)
    {
        // Try JSON first.
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;
            var verdictStr = root.TryGetProperty("verdict", out var v) ? v.GetString() : null;
            var conf = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetDouble() : 50;
            var rationale = (root.TryGetProperty("rationale", out var r) ? r.GetString() : null) ?? "";
            var verdict = ParseVerdict(verdictStr);
            if (verdict is not null)
                return new ReviewResult(verdict.Value, Normalize(conf), rationale, model);
        }
        catch { /* fall through to keyword search */ }

        // Keyword fallback.
        var lower = raw.ToLowerInvariant();
        if (lower.Contains("reject")) return new ReviewResult(Verdict.Reject, 0.5, raw.Trim(), model);
        if (lower.Contains("revise")) return new ReviewResult(Verdict.Revise, 0.5, raw.Trim(), model);
        if (lower.Contains("approve") || lower.Contains("lgtm"))
            return new ReviewResult(Verdict.Approve, 0.5, raw.Trim(), model);

        // No parseable verdict = reviewer failure -> escalate, never silently pass.
        return new ReviewResult(Verdict.Escalate, 0, "reviewer returned no parseable verdict", model);
    }

    private static Verdict? ParseVerdict(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "approve" => Verdict.Approve,
        "revise" => Verdict.Revise,
        "reject" => Verdict.Reject,
        _ => null,
    };

    private static double Normalize(double confidence) => confidence > 1 ? Math.Clamp(confidence / 100.0, 0, 1) : Math.Clamp(confidence, 0, 1);

    private static string ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw.Substring(start, end - start + 1) : raw;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "\n[...truncated...]";
}
