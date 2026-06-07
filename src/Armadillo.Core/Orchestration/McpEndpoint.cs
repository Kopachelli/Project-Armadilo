namespace Armadillo.Core.Orchestration;

/// <summary>
/// Late-bound handle to the harness's own MCP server. The daemon fills Url/Token in after it binds
/// an ephemeral port; the dispatcher reads it to wire spawned children back to the server (so they
/// can call <c>request_agent</c>). Null Url means "not serving" (e.g. the one-shot `run` command).
/// </summary>
public sealed class McpEndpoint
{
    public string? Url { get; set; }
    public string? Token { get; set; }
    public bool IsLive => !string.IsNullOrWhiteSpace(Url);
}
