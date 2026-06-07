using System.Text;
using System.Text.Json;
using Armadillo.Core.Events;
using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Adapters;

/// <summary>
/// OpenAI Codex CLI (<c>codex exec --json --full-auto</c>). Emits a JSONL event stream
/// (thread/turn/item/error). The answer is the concatenation of agent_message items. Preview-quality
/// until verified; falls back to plain text. Codex has no hooks.
/// </summary>
public sealed class CodexAdapter : CliAdapterBase
{
    public override ToolId Id => ToolId.Codex;

    protected override IReadOnlyList<string> BuildArgs(AgentBrief brief)
    {
        var args = new List<string> { "exec", "--json", "--full-auto" };
        if (!string.IsNullOrWhiteSpace(brief.Provider.Model)) { args.Add("--model"); args.Add(brief.Provider.Model!); }
        return args;
    }

    public override AdapterResult Parse(RunResult result)
    {
        var events = new List<AgentEvent>();
        var messages = new StringBuilder();
        var now = DateTimeOffset.UtcNow;
        bool isError = IsProcessError(result);
        var usage = TokenUsage.Zero;
        string? sessionId = null;

        int parsed = ForEachJsonLine(result.Stdout, events, now, root =>
        {
            var type = Str(root, "type") ?? "";
            sessionId ??= Str(root, "thread_id") ?? Str(root, "session_id");

            if (type.StartsWith("item") && root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
            {
                var itype = Str(item, "type") ?? "";
                if (itype.Contains("message") || itype.Contains("agent"))
                {
                    var text = Str(item, "text") ?? Str(item, "content");
                    if (!string.IsNullOrEmpty(text)) { messages.Clear(); messages.Append(text); events.Add(new TextDelta(now, text!)); }
                }
                else if (itype.Contains("command"))
                    events.Add(new ToolCall(now, "command", item.GetRawText()));
                else if (itype.Contains("file"))
                    events.Add(new FileWritten(now, Str(item, "path") ?? "?"));
            }
            else if (type.StartsWith("turn.completed"))
            {
                if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
                    usage = new TokenUsage(Long(u, "input_tokens"), Long(u, "output_tokens"));
                events.Add(new TurnEnded(now));
            }
            else if (type == "error" || type.EndsWith("failed"))
            {
                isError = true;
            }
        });

        if (parsed == 0) return TextFallback(result);

        var text = messages.Length > 0 ? messages.ToString().Trim() : result.Stdout.Trim();
        events.Add(new SessionStopped(now, StopReason(result, isError)));
        return new AdapterResult { FinalText = text, Usage = usage, SessionId = sessionId, Events = events, IsError = isError };
    }
}
