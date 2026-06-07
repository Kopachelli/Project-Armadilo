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
