namespace Armadillo.Core.Tools;

/// <summary>Adapter family — tools in the same family share spawn/parse mechanics.</summary>
public enum AdapterFamily
{
    /// <summary>One-off CLI with its own flags (Claude, Codex, Cursor).</summary>
    Generic,

    /// <summary>Gemini-CLI lineage: <c>-p</c>/<c>--yolo</c>, settings.json MCP, OpenAI-compatible providers (Gemini, Qwen).</summary>
    Gemini,

    /// <summary>Long-lived local HTTP daemon driven over an API (OpenCode serve, Copilot SDK).</summary>
    Daemon,

    /// <summary>Local model runtime accessed over HTTP, not a coding agent (Ollama).</summary>
    Runtime,
}

/// <summary>
/// Static, compile-time metadata for each tool: how to find it, how to invoke it headlessly,
/// and what it can do. Detection turns a descriptor + the machine state into a <see cref="DetectedTool"/>.
/// </summary>
public sealed record ToolDescriptor(
    ToolId Id,
    string DisplayName,
    IReadOnlyList<string> ExecutableNames,
    IReadOnlyList<string> DotFolders,
    AdapterFamily Family,
    ToolCapabilities Capabilities,
    IReadOnlyList<string> VersionArgs,
    string? McpConfigHint,
    string Notes)
{
    private const ToolCapabilities Cli =
        ToolCapabilities.Headless | ToolCapabilities.StreamJson | ToolCapabilities.McpClient;

    /// <summary>The canonical catalog, in display order.</summary>
    public static readonly IReadOnlyList<ToolDescriptor> All = new[]
    {
        new ToolDescriptor(ToolId.Claude, "Claude Code",
            ExecutableNames: new[] { "claude" },
            DotFolders: new[] { ".claude" },
            Family: AdapterFamily.Generic,
            Capabilities: Cli | ToolCapabilities.Hooks | ToolCapabilities.Resume | ToolCapabilities.Acp,
            VersionArgs: new[] { "--version" },
            McpConfigHint: ".claude.json",
            Notes: "Richest surface: -p --output-format stream-json, hooks, --resume, --mcp-config."),

        new ToolDescriptor(ToolId.Codex, "Codex",
            ExecutableNames: new[] { "codex" },
            DotFolders: new[] { ".codex" },
            Family: AdapterFamily.Generic,
            Capabilities: Cli | ToolCapabilities.Resume,
            VersionArgs: new[] { "--version" },
            McpConfigHint: ".codex/config.toml",
            Notes: "codex exec --json --full-auto; MCP via config.toml; NO hooks."),

        new ToolDescriptor(ToolId.Cursor, "Cursor",
            ExecutableNames: new[] { "cursor-agent" }, // the headless agent, NOT the `cursor` IDE launcher
            DotFolders: new[] { ".cursor" },
            Family: AdapterFamily.Generic,
            Capabilities: Cli | ToolCapabilities.Resume | ToolCapabilities.Acp,
            VersionArgs: new[] { "--version" },
            McpConfigHint: ".cursor/mcp.json",
            Notes: "cursor-agent -p --output-format stream-json --resume."),

        new ToolDescriptor(ToolId.Gemini, "Gemini CLI",
            ExecutableNames: new[] { "gemini" },
            DotFolders: new[] { ".gemini" },
            Family: AdapterFamily.Gemini,
            Capabilities: Cli | ToolCapabilities.LocalProvider | ToolCapabilities.Acp,
            VersionArgs: new[] { "--version" },
            McpConfigHint: ".gemini/settings.json",
            Notes: "gemini -p --yolo --output-format json. Gemini-family base."),

        new ToolDescriptor(ToolId.Qwen, "Qwen Code",
            ExecutableNames: new[] { "qwen" },
            DotFolders: new[] { ".qwen" },
            Family: AdapterFamily.Gemini,
            Capabilities: Cli | ToolCapabilities.LocalProvider,
            VersionArgs: new[] { "--version" },
            McpConfigHint: ".qwen/settings.json",
            Notes: "Gemini-CLI fork; OpenAI/Anthropic-compatible providers; qwen -p."),

        new ToolDescriptor(ToolId.Copilot, "GitHub Copilot CLI",
            ExecutableNames: new[] { "copilot" },
            DotFolders: new[] { ".copilot" },
            Family: AdapterFamily.Daemon,
            Capabilities: ToolCapabilities.Headless | ToolCapabilities.McpClient
                          | ToolCapabilities.HttpDaemon | ToolCapabilities.Acp,
            VersionArgs: new[] { "--version" },
            McpConfigHint: null,
            Notes: "copilot -p -s; Copilot SDK headless server; MCP stdio+HTTP; /fleet."),

        new ToolDescriptor(ToolId.Pi, "Pi",
            ExecutableNames: new[] { "pi" },
            DotFolders: new[] { ".pi" },
            Family: AdapterFamily.Generic,
            Capabilities: ToolCapabilities.Headless | ToolCapabilities.LocalProvider,
            VersionArgs: new[] { "--version" },
            McpConfigHint: null,
            Notes: "Reshapeable terminal agent; skills in ~/.pi/agent/skills; unified LLM API (local models)."),

        new ToolDescriptor(ToolId.OpenCode, "OpenCode",
            ExecutableNames: new[] { "opencode", "open-code", "openclaw", "open-claw" },
            DotFolders: new[] { ".opencode", ".openclaw" },
            Family: AdapterFamily.Daemon,
            Capabilities: Cli | ToolCapabilities.HttpDaemon,
            VersionArgs: new[] { "--version" },
            McpConfigHint: ".opencode/opencode.json",
            Notes: "opencode run --format json OR opencode serve (HTTP+OpenAPI); @opencode-ai/sdk."),

        new ToolDescriptor(ToolId.Zai, "z.ai (GLM) CLI",
            ExecutableNames: new[] { "zai", "zai-cli" },
            DotFolders: Array.Empty<string>(),
            Family: AdapterFamily.Generic,
            Capabilities: ToolCapabilities.Headless | ToolCapabilities.LocalProvider,
            VersionArgs: new[] { "--version" },
            McpConfigHint: null,
            Notes: "Optional standalone GLM CLI; z.ai also usable as a ProviderProfile for other tools."),

        new ToolDescriptor(ToolId.Ollama, "Ollama",
            ExecutableNames: new[] { "ollama" },
            DotFolders: new[] { ".ollama" },
            Family: AdapterFamily.Runtime,
            Capabilities: ToolCapabilities.LocalProvider | ToolCapabilities.HttpDaemon,
            VersionArgs: new[] { "--version" },
            McpConfigHint: null,
            Notes: "Local model runtime; HTTP :11434 (/api/chat, /v1/..., /api/tags). Default reviewer/embeddings."),
    };

    public static ToolDescriptor For(ToolId id) => All.First(d => d.Id == id);
}
