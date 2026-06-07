using System.Text;
using System.Text.Json;
using Armadillo.Core.Events;
using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Adapters;

/// <summary>
/// Claude Code adapter. Spawns <c>claude -p --output-format stream-json --verbose</c> with the task
/// on stdin, and parses the NDJSON event stream into a normalized result. Falls back to treating
/// stdout as plain text if it isn't valid stream-json (defensive against format drift).
/// </summary>
public sealed class ClaudeAdapter : IToolAdapter
{
    public ToolId Id => ToolId.Claude;
    public ToolCapabilities Capabilities => ToolDescriptor.For(ToolId.Claude).Capabilities;

    public RunSpec BuildRunSpec(AgentBrief brief, string executablePath)
    {
        var args = new List<string>
        {
            "-p",
            "--output-format", "stream-json",
            "--verbose",
        };

        if (!string.IsNullOrWhiteSpace(brief.Persona))
        {
            args.Add("--append-system-prompt");
            args.Add(brief.Persona); // distinct argv element — not flag-parsed even if it starts with '-'
        }

        if (!string.IsNullOrWhiteSpace(brief.Provider.Model))
        {
            args.Add("--model");
            args.Add(brief.Provider.Model!);
        }

        if (!string.IsNullOrWhiteSpace(brief.McpConfigPath))
        {
            args.Add("--mcp-config");
            args.Add(brief.McpConfigPath!);
            args.Add("--strict-mcp-config"); // only the harness server; ignore the user's own MCP config
        }

        var env = new Dictionary<string, string>(brief.Provider.EnvOverrides());
        foreach (var (k, v) in brief.ExtraEnv) env[k] = v;

        return new RunSpec
        {
            FilePath = executablePath,
            Arguments = args,
            StdinText = brief.Task,
            WorkingDirectory = brief.WorkingDirectory,
            Environment = env,
            Timeout = brief.Timeout,
        };
    }

    public AdapterResult Parse(RunResult result)
    {
        var events = new List<AgentEvent>();
        var assistantText = new StringBuilder();
        string? finalText = null;
        string? sessionId = null;
        string? model = null;
        var usage = TokenUsage.Zero;
        bool isError = result.ExitCode is not (0 or null) || result.TimedOut || result.Killed;
        int parsedJsonLines = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var line in result.Stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(trimmed); }
            catch { events.Add(new RawLine(now, trimmed)); continue; }

            using (doc)
            {
                parsedJsonLines++;
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

                switch (type)
                {
                    case "system":
                        sessionId ??= GetString(root, "session_id");
                        model ??= GetString(root, "model");
                        events.Add(new SessionStarted(now, sessionId, model));
                        break;

                    case "assistant":
                        ReadAssistant(root, assistantText, events, now);
                        break;

                    case "user":
                        ReadToolResults(root, events, now);
                        break;

                    case "result":
                        finalText = GetString(root, "result");
                        sessionId ??= GetString(root, "session_id");
                        if (root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True)
                            isError = true;
                        usage = ReadUsage(root, GetDouble(root, "total_cost_usd"));
                        events.Add(new UsageReported(now, usage));
                        events.Add(new TurnEnded(now));
                        break;
                }
            }
        }

        // Text fallback: nothing parsed as stream-json -> treat whole stdout as the answer.
        if (parsedJsonLines == 0)
            finalText = result.Stdout.Trim();

        var text = finalText
                   ?? (assistantText.Length > 0 ? assistantText.ToString().Trim() : result.Stdout.Trim());

        events.Add(new SessionStopped(now,
            result.TimedOut ? "timeout" : result.Killed ? "killed" : isError ? "error" : "completed"));

        return new AdapterResult
        {
            FinalText = text,
            Usage = usage,
            SessionId = sessionId,
            Model = model,
            Events = events,
            IsError = isError,
        };
    }

    private static void ReadAssistant(JsonElement root, StringBuilder text, List<AgentEvent> events, DateTimeOffset now)
    {
        if (!root.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;

        foreach (var part in content.EnumerateArray())
        {
            var ptype = GetString(part, "type");
            if (ptype == "text")
            {
                var s = GetString(part, "text");
                if (!string.IsNullOrEmpty(s)) { text.Append(s); events.Add(new TextDelta(now, s!)); }
            }
            else if (ptype == "tool_use")
            {
                var name = GetString(part, "name") ?? "tool";
                var input = part.TryGetProperty("input", out var inp) ? inp.GetRawText() : null;
                events.Add(new ToolCall(now, name, input));
            }
        }
    }

    private static void ReadToolResults(JsonElement root, List<AgentEvent> events, DateTimeOffset now)
    {
        if (!root.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;

        foreach (var part in content.EnumerateArray())
        {
            if (GetString(part, "type") != "tool_result") continue;
            var isErr = part.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
            string? body = part.TryGetProperty("content", out var c)
                ? (c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetRawText())
                : null;
            events.Add(new ToolResult(now, "tool", isErr, body));
        }
    }

    private static TokenUsage ReadUsage(JsonElement root, double cost)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
            return new TokenUsage(0, 0, cost);
        long input = GetLong(u, "input_tokens") + GetLong(u, "cache_read_input_tokens")
                     + GetLong(u, "cache_creation_input_tokens");
        long output = GetLong(u, "output_tokens");
        return new TokenUsage(input, output, cost);
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long GetLong(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private static double GetDouble(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
