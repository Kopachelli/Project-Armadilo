using System.Text.Json;
using Armadillo.Core.Events;
using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Adapters;

/// <summary>
/// Gemini CLI. Headless single-JSON output (<c>--output-format json</c>, <c>--yolo</c>). This is the
/// base of the Gemini family (Qwen Code is a fork). Falls back to plain text. Preview until verified.
/// </summary>
public class GeminiAdapter : CliAdapterBase
{
    public override ToolId Id => ToolId.Gemini;

    protected override IReadOnlyList<string> BuildArgs(AgentBrief brief)
    {
        var args = new List<string> { "--output-format", "json", "--yolo" };
        if (!string.IsNullOrWhiteSpace(brief.Provider.Model)) { args.Add("--model"); args.Add(brief.Provider.Model!); }
        return args;
    }

    public override AdapterResult Parse(RunResult result)
    {
        var now = DateTimeOffset.UtcNow;
        bool isError = IsProcessError(result);

        // Gemini emits a single JSON object. Try to parse the whole stdout.
        var text = result.Stdout.Trim();
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                using var doc = JsonDocument.Parse(text.Substring(start, end - start + 1));
                var root = doc.RootElement;
                var response = Str(root, "response") ?? Str(root, "result") ?? Str(root, "text") ?? Str(root, "output");
                var usage = ReadStats(root);
                if (root.TryGetProperty("error", out var e) && e.ValueKind != JsonValueKind.Null) isError = true;
                if (response is not null)
                {
                    return new AdapterResult
                    {
                        FinalText = response,
                        Usage = usage,
                        IsError = isError,
                        Events = new AgentEvent[]
                        {
                            new TextDelta(now, response),
                            new UsageReported(now, usage),
                            new SessionStopped(now, StopReason(result, isError)),
                        },
                    };
                }
            }
        }
        catch { /* fall through to text */ }

        return TextFallback(result);
    }

    private static TokenUsage ReadStats(JsonElement root)
    {
        // Gemini reports token counts under "stats"/"usageMetadata" in various shapes; best-effort.
        if (root.TryGetProperty("stats", out var s) && s.ValueKind == JsonValueKind.Object)
            return new TokenUsage(Long(s, "promptTokenCount") + Long(s, "input_tokens"),
                                  Long(s, "candidatesTokenCount") + Long(s, "output_tokens"));
        if (root.TryGetProperty("usageMetadata", out var u) && u.ValueKind == JsonValueKind.Object)
            return new TokenUsage(Long(u, "promptTokenCount"), Long(u, "candidatesTokenCount"));
        return TokenUsage.Zero;
    }
}

/// <summary>Qwen Code — a Gemini-CLI fork. Same flags/output, different binary + provider story.</summary>
public sealed class QwenAdapter : GeminiAdapter
{
    public override ToolId Id => ToolId.Qwen;
}
