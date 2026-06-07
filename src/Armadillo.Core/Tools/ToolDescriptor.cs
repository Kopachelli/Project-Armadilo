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

/// <summary>What kind of thing this is — determines whether the harness can drive it headlessly.</summary>
public enum ToolKind
{
    /// <summary>A headless CLI we can spawn as a child process and orchestrate.</summary>
    Cli,
    /// <summary>A local model runtime reached over HTTP (Ollama, LM Studio…).</summary>
    Runtime,
    /// <summary>A GUI desktop app — detected/reported, but NOT drivable as a headless child.</summary>
    Desktop,
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
    /// <summary>CLI / Runtime / Desktop. Defaults to CLI.</summary>
    public ToolKind Kind { get; init; } = ToolKind.Cli;

    /// <summary>For desktop apps: candidate install paths (env vars expanded) checked as a fallback.</summary>
    public IReadOnlyList<string> AppPaths { get; init; } = Array.Empty<string>();

    /// <summary>For desktop apps: substrings matched against installed-app display names (registry scan).
    /// The primary, reliable way to detect a GUI without guessing exe paths or colliding with a CLI.</summary>
    public IReadOnlyList<string> AppNamePatterns { get; init; } = Array.Empty<string>();

    /// <summary>Whether the harness can spawn + orchestrate it headlessly (everything but desktop GUIs).</summary>
    public bool Drivable => Kind != ToolKind.Desktop;

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
            Notes: "gemini -p --yolo --output-format json. Gemini-family base. NOTE: being superseded by Antigravity CLI (free tier ends 2026-06-18)."),

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
            ExecutableNames: new[] { "opencode", "open-code" },
            DotFolders: new[] { ".opencode" },
            Family: AdapterFamily.Daemon,
            Capabilities: Cli | ToolCapabilities.HttpDaemon,
            VersionArgs: new[] { "--version" },
            McpConfigHint: ".opencode/opencode.json",
            Notes: "opencode run --format json OR opencode serve (HTTP+OpenAPI); @opencode-ai/sdk."),

        new ToolDescriptor(ToolId.OpenClaw, "OpenClaw",
            ExecutableNames: new[] { "openclaw", "open-claw" },
            DotFolders: new[] { ".openclaw" },
            Family: AdapterFamily.Generic,
            Capabilities: ToolCapabilities.Headless | ToolCapabilities.McpClient | ToolCapabilities.LocalProvider,
            VersionArgs: new[] { "--version" },
            McpConfigHint: null,
            Notes: "Life-automation/orchestration platform (openclaw.ai); persistent memory; can itself spawn coding agents. Preview."),

        new ToolDescriptor(ToolId.Hermes, "Hermes Agent",
            ExecutableNames: new[] { "hermes", "hermes-cli", "tirith" },
            DotFolders: new[] { ".hermes" },
            Family: AdapterFamily.Generic,
            Capabilities: ToolCapabilities.Headless | ToolCapabilities.Resume | ToolCapabilities.McpClient
                          | ToolCapabilities.LocalProvider,
            VersionArgs: new[] { "--version" },
            McpConfigHint: null,
            Notes: "Nous Research autonomous agent: `hermes chat -q \"...\"`; `-w` isolated worktree. Linux/macOS/WSL2."),

        new ToolDescriptor(ToolId.Antigravity, "Google Antigravity CLI",
            ExecutableNames: new[] { "agy", "antigravity" },
            DotFolders: new[] { ".antigravity" },
            Family: AdapterFamily.Gemini,
            Capabilities: ToolCapabilities.Headless | ToolCapabilities.McpClient | ToolCapabilities.LocalProvider,
            VersionArgs: new[] { "--version" },
            McpConfigHint: "mcp_config.json",
            Notes: "Successor to Gemini CLI (Go). Command mode: `agy -p \"prompt\"`; async subagents; MCP stdio+HTTP."),

        new ToolDescriptor(ToolId.Kimi, "Kimi Code CLI",
            ExecutableNames: new[] { "kimi", "kimi-code" },
            DotFolders: new[] { ".kimi" },
            Family: AdapterFamily.Generic,
            Capabilities: ToolCapabilities.Headless | ToolCapabilities.McpClient | ToolCapabilities.LocalProvider,
            VersionArgs: new[] { "--version" },
            McpConfigHint: null,
            Notes: "Moonshot Kimi Code CLI (K2.6); Anthropic-compatible; agent swarm. Also a ProviderProfile. Preview."),

        new ToolDescriptor(ToolId.MiniMax, "MiniMax CLI",
            ExecutableNames: new[] { "mmx", "mmx-cli", "minimax" },
            DotFolders: new[] { ".minimax", ".mmx" },
            Family: AdapterFamily.Generic,
            Capabilities: ToolCapabilities.Headless | ToolCapabilities.McpClient | ToolCapabilities.LocalProvider,
            VersionArgs: new[] { "--version" },
            McpConfigHint: null,
            Notes: "MiniMax MMX-CLI (M2.5); Anthropic-compatible. Also a ProviderProfile. Preview."),

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
            Notes: "Local model runtime; HTTP :11434 (/api/chat, /v1/..., /api/tags). Default reviewer/embeddings.")
            { Kind = ToolKind.Runtime },

        // --- Desktop GUI apps: detected & reported (registry display-name match), but NOT
        //     headless-drivable (no adapter). Drive the matching CLI instead. ---

        Desktop(ToolId.CopilotDesktop, "GitHub Copilot (Desktop)", new[] { "GitHub Copilot" },
            "Standalone agent-native desktop app (preview). GUI — drive the `copilot` CLI instead."),
        Desktop(ToolId.CodexApp, "OpenAI Codex (App)", new[] { "OpenAI Codex", "Codex" },
            "Codex desktop app for parallel threads. GUI — drive the `codex` CLI instead."),
        Desktop(ToolId.ClaudeDesktop, "Claude (Desktop)", new[] { "Claude" },
            "Claude desktop app. GUI — drive the `claude` CLI instead."),
        Desktop(ToolId.CursorDesktop, "Cursor (IDE)", new[] { "Cursor" },
            "Cursor agentic IDE (VS Code fork). GUI — drive the `cursor-agent` CLI instead."),
        Desktop(ToolId.QwenDesktop, "Qwen Chat (Desktop)", new[] { "Qwen" },
            "Qwen Chat/Studio desktop app. GUI — drive the `qwen` Code CLI instead."),
        Desktop(ToolId.KimiDesktop, "Kimi (Desktop)", new[] { "Kimi" },
            "Moonshot Kimi desktop app (+ WebBridge). GUI — drive the `kimi` CLI instead."),
        Desktop(ToolId.MiniMaxDesktop, "MiniMax (Desktop)", new[] { "MiniMax" },
            "MiniMax Agent/Code desktop app. GUI — drive the `mmx` CLI instead."),
        Desktop(ToolId.OpenCodeDesktop, "OpenCode (Desktop)", new[] { "OpenCode", "opencode" },
            "OpenCode desktop app (beta). GUI — drive the `opencode` CLI / `opencode serve` instead."),
        Desktop(ToolId.HermesDesktop, "Hermes (Desktop)", new[] { "Hermes" },
            "Native Hermes GUI (preview). GUI — drive the `hermes` CLI instead."),
        Desktop(ToolId.AntigravityApp, "Google Antigravity (IDE)", new[] { "Antigravity" },
            "Antigravity agent-first desktop IDE. GUI — drive the `agy` CLI instead."),
    };

    /// <summary>Helper for desktop-app catalog entries (detected by registry display-name match).</summary>
    private static ToolDescriptor Desktop(ToolId id, string name, string[] namePatterns, string notes) => new(
        id, name, ExecutableNames: Array.Empty<string>(), DotFolders: Array.Empty<string>(),
        Family: AdapterFamily.Generic, Capabilities: ToolCapabilities.None,
        VersionArgs: Array.Empty<string>(), McpConfigHint: null, Notes: notes)
        { Kind = ToolKind.Desktop, AppNamePatterns = namePatterns };

    public static ToolDescriptor For(ToolId id) => All.First(d => d.Id == id);
}
