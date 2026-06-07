namespace Armadillo.Core.Tools;

/// <summary>
/// What a tool can do. Mostly static per tool (set in <see cref="ToolDescriptor"/>), refined
/// at detection time by version. The Router/brain reads these to decide what to spawn.
/// </summary>
[Flags]
public enum ToolCapabilities
{
    None = 0,

    /// <summary>Can run a single prompt non-interactively and exit (e.g. <c>-p</c> / <c>exec</c>).</summary>
    Headless = 1 << 0,

    /// <summary>Emits machine-readable JSON / NDJSON events we can parse for transcript + usage.</summary>
    StreamJson = 1 << 1,

    /// <summary>Supports lifecycle hooks an external supervisor can intercept (Claude-style).</summary>
    Hooks = 1 << 2,

    /// <summary>Can resume/continue a prior session by id.</summary>
    Resume = 1 << 3,

    /// <summary>Can act as an MCP client (connect to our MCP server).</summary>
    McpClient = 1 << 4,

    /// <summary>Runs as a long-lived local HTTP daemon (e.g. <c>opencode serve</c>, Copilot SDK).</summary>
    HttpDaemon = 1 << 5,

    /// <summary>Can target a local / custom OpenAI- or Anthropic-compatible provider endpoint.</summary>
    LocalProvider = 1 << 6,

    /// <summary>Speaks Zed's Agent Client Protocol (editor &lt;-&gt; agent).</summary>
    Acp = 1 << 7,
}
