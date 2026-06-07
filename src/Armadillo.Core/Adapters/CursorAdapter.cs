using System.Text;
using System.Text.Json;
using Armadillo.Core.Events;
using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Adapters;

/// <summary>
/// Cursor CLI (<c>cursor-agent</c>). Piped stdin infers print mode; emits an NDJSON event union
/// (system | assistant | tool_call | tool_result | result | error). Preview-quality until verified
/// against the installed binary. Falls back to plain text.
/// </summary>
public sealed class CursorAdapter : CliAdapterBase
{
    public override ToolId Id => ToolId.Cursor;

    protected override IReadOnlyList<string> BuildArgs(AgentBrief brief)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json", "--force" };
        if (!string.IsNullOrWhiteSpace(brief.Provider.Model)) { args.Add("--model"); args.Add(brief.Provider.Model!); }
        return args;
    }

    public override AdapterResult Parse(RunResult result)
    {
        var events = new List<AgentEvent>();
        var assistant = new StringBuilder();
        string? finalText = null, sessionId = null, model = null;
        var usage = TokenUsage.Zero;
        var now = DateTimeOffset.UtcNow;
        bool isError = IsProcessError(result);

        int parsed = ForEachJsonLine(result.Stdout, events, now, root =>
        {
            switch (Str(root, "type"))
            {
                case "system":
                    sessionId ??= Str(root, "session_id");
                    model ??= Str(root, "model");
                    events.Add(new SessionStarted(now, sessionId, model));
                    break;
                case "assistant":
                    AppendAssistant(root, assistant, events, now);
                    break;
                case "result":
                    finalText = Str(root, "result") ?? Str(root, "text");
                    sessionId ??= Str(root, "session_id");
                    if (root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True) isError = true;
                    usage = ReadUsage(root);
                    events.Add(new TurnEnded(now));
                    break;
            }
        });

        if (parsed == 0) return TextFallback(result);

        var text = finalText ?? (assistant.Length > 0 ? assistant.ToString().Trim() : result.Stdout.Trim());
        events.Add(new SessionStopped(now, StopReason(result, isError)));
        return new AdapterResult { FinalText = text, Usage = usage, SessionId = sessionId, Model = model, Events = events, IsError = isError };
    }

    private static void AppendAssistant(JsonElement root, StringBuilder sb, List<AgentEvent> events, DateTimeOffset now)
    {
        // Cursor mirrors Claude's shape: message.content[] with text parts. Be defensive.
        if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
                if (Str(part, "type") == "text" && Str(part, "text") is { } s) { sb.Append(s); events.Add(new TextDelta(now, s)); }
        }
        else if (Str(root, "text") is { } flat) { sb.Append(flat); events.Add(new TextDelta(now, flat)); }
    }

    private static TokenUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return TokenUsage.Zero;
        return new TokenUsage(Long(u, "input_tokens"), Long(u, "output_tokens"), Dbl(root, "total_cost_usd"));
    }
}
