using Armadillo.Core.Events;
using Armadillo.Core.Providers;
using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Adapters;

/// <summary>The work order handed to an adapter to produce a runnable spec.</summary>
public sealed record AgentBrief
{
    public required string Persona { get; init; }
    public required string Task { get; init; }
    public ProviderProfile Provider { get; init; } = ProviderProfile.Inherit;
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> ExtraEnv { get; init; } = new Dictionary<string, string>();
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>If set, the spawned agent is wired to the harness's MCP server via this config file,
    /// so it can itself "call the administrator" (bounded by the Governor's lineage guard).</summary>
    public string? McpConfigPath { get; init; }
}

/// <summary>Normalized parse of a finished run.</summary>
public sealed record AdapterResult
{
    public required string FinalText { get; init; }
    public TokenUsage Usage { get; init; } = TokenUsage.Zero;
    public string? SessionId { get; init; }
    public string? Model { get; init; }
    public IReadOnlyList<AgentEvent> Events { get; init; } = Array.Empty<AgentEvent>();
    public bool IsError { get; init; }
}

/// <summary>
/// One driver per CLI/runtime. Adding a tool = implementing this once. <see cref="BuildRunSpec"/>
/// turns a brief + resolved executable into an argv/stdin/env plan (task ALWAYS via stdin);
/// <see cref="Parse"/> turns captured stdout into a normalized result.
/// </summary>
public interface IToolAdapter
{
    ToolId Id { get; }
    ToolCapabilities Capabilities { get; }

    RunSpec BuildRunSpec(AgentBrief brief, string executablePath);

    AdapterResult Parse(RunResult result);
}

/// <summary>Guards against flag-injection: untrusted text must never be argv that starts with '-'.</summary>
public static class ArgGuard
{
    public static string AssertSafeFlagValue(string value, string what)
    {
        if (value.StartsWith('-'))
            throw new ArgumentException($"{what} starts with '-' and would be parsed as a CLI flag.", nameof(value));
        return value;
    }
}
