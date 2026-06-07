namespace Armadillo.Core.Tools;

/// <summary>
/// What a specific tool variant actually has configured — its skills, MCP servers, and config files.
/// CLI and Desktop variants of the same brand keep these in different places, so they're scanned
/// per-variant ("fetch everything").
/// </summary>
public sealed record ToolAssets(
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> McpServers,
    IReadOnlyList<string> ConfigFiles)
{
    public static readonly ToolAssets Empty = new(
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    public bool IsEmpty => Skills.Count == 0 && McpServers.Count == 0 && ConfigFiles.Count == 0;
}
