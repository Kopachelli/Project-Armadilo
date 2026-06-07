using System.Text;
using System.Text.Json;
using Armadillo.Core.Events;
using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Adapters;

/// <summary>
/// Shared plumbing for CLI adapters: builds a run spec (task via stdin, provider env, working dir)
/// and offers JSON helpers + a text-fallback path. Subclasses supply argv and the parse logic.
/// </summary>
public abstract class CliAdapterBase : IToolAdapter
{
    public abstract ToolId Id { get; }
    public ToolCapabilities Capabilities => ToolDescriptor.For(Id).Capabilities;

    /// <summary>argv (flags only — the task goes via stdin). May read brief.Provider.Model etc.
    /// Adapters that override <see cref="BuildRunSpec"/> (argv-prompt tools) can ignore this.</summary>
    protected virtual IReadOnlyList<string> BuildArgs(AgentBrief brief) => Array.Empty<string>();

    public virtual RunSpec BuildRunSpec(AgentBrief brief, string executablePath)
        => new()
        {
            FilePath = executablePath,
            Arguments = BuildArgs(brief),
            StdinText = ComposeStdin(brief),
            WorkingDirectory = brief.WorkingDirectory,
            Environment = MergeEnv(brief),
            Timeout = brief.Timeout,
        };

    /// <summary>For tools that take the prompt as a flag VALUE (argv) rather than via stdin
    /// (e.g. <c>agy -p "..."</c>, <c>hermes chat -q "..."</c>).</summary>
    protected RunSpec ArgvPromptSpec(AgentBrief brief, string executablePath,
        IReadOnlyList<string> leadingArgs, string promptFlag)
    {
        var args = new List<string>(leadingArgs) { promptFlag, ComposeStdin(brief) };
        return new RunSpec
        {
            FilePath = executablePath,
            Arguments = args,
            StdinText = null,
            WorkingDirectory = brief.WorkingDirectory,
            Environment = MergeEnv(brief),
            Timeout = brief.Timeout,
        };
    }

    protected static IReadOnlyDictionary<string, string> MergeEnv(AgentBrief brief)
    {
        var env = new Dictionary<string, string>(brief.Provider.EnvOverrides());
        foreach (var (k, v) in brief.ExtraEnv) env[k] = v;
        return env;
    }

    /// <summary>Tools without a system-prompt flag get the persona folded into the task.</summary>
    protected static string ComposeStdin(AgentBrief brief)
        => string.IsNullOrWhiteSpace(brief.Persona)
            ? brief.Task
            : $"{brief.Persona}\n\n---\nTask:\n{brief.Task}";

    public abstract AdapterResult Parse(RunResult result);

    // --- shared helpers ---

    protected static bool IsProcessError(RunResult r) => r.ExitCode is not (0 or null) || r.TimedOut || r.Killed;

    protected static string StopReason(RunResult r, bool isError)
        => r.TimedOut ? "timeout" : r.Killed ? "killed" : isError ? "error" : "completed";

    /// <summary>Iterate stdout as NDJSON, calling <paramref name="onObject"/> per parsed line; unparseable
    /// lines become <see cref="RawLine"/> events. Returns the count of parsed JSON lines.</summary>
    protected static int ForEachJsonLine(string stdout, List<AgentEvent> events, DateTimeOffset now,
        Action<JsonElement> onObject)
    {
        int parsed = 0;
        foreach (var line in stdout.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(t); }
            catch { events.Add(new RawLine(now, t)); continue; }
            using (doc) { parsed++; onObject(doc.RootElement); }
        }
        return parsed;
    }

    protected static AdapterResult TextFallback(RunResult result)
    {
        var isError = IsProcessError(result);
        var text = result.Stdout.Trim();
        if (text.Length == 0 && result.Stderr.Trim().Length > 0)
            text = result.Stderr.Trim(); // surface diagnostics (e.g. auth errors) instead of empty output
        return new AdapterResult
        {
            FinalText = text,
            IsError = isError,
            Events = new AgentEvent[] { new SessionStopped(DateTimeOffset.UtcNow, StopReason(result, isError)) },
        };
    }

    protected static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    protected static long Long(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    protected static double Dbl(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
