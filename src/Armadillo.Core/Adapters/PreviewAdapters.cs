using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Adapters;

// Preview adapters for tools not installed on the dev machine. Best-effort invocation + text/stderr
// fallback parsing; verify against the real binary before promoting out of "preview".

/// <summary>Google Antigravity CLI (`agy`) — Gemini-CLI successor. Command mode: <c>agy -p "prompt"</c>.</summary>
public sealed class AntigravityAdapter : CliAdapterBase
{
    public override ToolId Id => ToolId.Antigravity;

    public override RunSpec BuildRunSpec(AgentBrief brief, string executablePath)
        => ArgvPromptSpec(brief, executablePath, Array.Empty<string>(), "-p");

    public override AdapterResult Parse(RunResult result) => TextFallback(result);
}

/// <summary>Nous Research Hermes Agent. Single-query mode: <c>hermes chat -q "query"</c>.</summary>
public sealed class HermesAdapter : CliAdapterBase
{
    public override ToolId Id => ToolId.Hermes;

    public override RunSpec BuildRunSpec(AgentBrief brief, string executablePath)
    {
        var leading = new List<string> { "chat" };
        if (!string.IsNullOrWhiteSpace(brief.Provider.Model)) { leading.Add("--model"); leading.Add(brief.Provider.Model!); }
        return ArgvPromptSpec(brief, executablePath, leading, "-q");
    }

    public override AdapterResult Parse(RunResult result) => TextFallback(result);
}

/// <summary>OpenClaw orchestration platform. Headless surface is uncertain; assume <c>openclaw run</c> + stdin.</summary>
public sealed class OpenClawAdapter : CliAdapterBase
{
    public override ToolId Id => ToolId.OpenClaw;

    protected override IReadOnlyList<string> BuildArgs(AgentBrief brief) => new[] { "run" };

    public override AdapterResult Parse(RunResult result) => TextFallback(result);
}

/// <summary>Moonshot Kimi Code CLI. Task via stdin; text fallback. Verify invocation against the binary.</summary>
public sealed class KimiAdapter : CliAdapterBase
{
    public override ToolId Id => ToolId.Kimi;
    public override AdapterResult Parse(RunResult result) => TextFallback(result);
}

/// <summary>MiniMax MMX-CLI. Task via stdin; text fallback. Verify invocation against the binary.</summary>
public sealed class MiniMaxAdapter : CliAdapterBase
{
    public override ToolId Id => ToolId.MiniMax;
    public override AdapterResult Parse(RunResult result) => TextFallback(result);
}

/// <summary>
/// Drives a LOCAL Ollama model as a chain/agent participant via <c>ollama run &lt;model&gt;</c> (task on
/// stdin). Free + private — useful as a no-cost second engine in cross-tool chains. Model comes from
/// ProviderProfile.Model, else ARMADILLO_OLLAMA_MODEL, else a sensible default.
/// </summary>
public sealed class OllamaCliAdapter : CliAdapterBase
{
    public override ToolId Id => ToolId.Ollama;

    public override RunSpec BuildRunSpec(AgentBrief brief, string executablePath)
    {
        var model = !string.IsNullOrWhiteSpace(brief.Provider.Model)
            ? brief.Provider.Model!
            : Environment.GetEnvironmentVariable("ARMADILLO_OLLAMA_MODEL") ?? "qwen2.5-coder:7b";
        return new RunSpec
        {
            FilePath = executablePath,
            Arguments = new[] { "run", model },
            StdinText = ComposeStdin(brief),
            WorkingDirectory = brief.WorkingDirectory,
            Environment = MergeEnv(brief),
            Timeout = brief.Timeout,
        };
    }

    // Ollama's CLI renders a streaming spinner with ANSI cursor/erase codes even when piped — strip them.
    private static readonly System.Text.RegularExpressions.Regex Ansi =
        new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public override AdapterResult Parse(RunResult result)
        => TextFallback(result with { Stdout = Ansi.Replace(result.Stdout, "") });
}
