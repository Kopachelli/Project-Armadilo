namespace Armadillo.Core.Tools;

/// <summary>The outcome of probing one tool on this machine.</summary>
public sealed record DetectedTool(
    ToolId Id,
    string DisplayName,
    bool Installed,
    string? ExecutablePath,
    string? Version,
    ToolCapabilities Capabilities,
    AdapterFamily Family,
    IReadOnlyList<string> Signals,
    IReadOnlyList<string> Models,
    string Notes)
{
    public ToolKind Kind { get; init; } = ToolKind.Cli;

    /// <summary>Installed AND headless-drivable (excludes desktop GUIs).</summary>
    public bool Drivable => Installed && Kind != ToolKind.Desktop;

    public static DetectedTool NotFound(ToolDescriptor d) => new(
        d.Id, d.DisplayName, Installed: false, ExecutablePath: null, Version: null,
        Capabilities: d.Capabilities, Family: d.Family,
        Signals: Array.Empty<string>(), Models: Array.Empty<string>(), Notes: d.Notes) { Kind = d.Kind };
}

/// <summary>A point-in-time snapshot of everything detected on the machine.</summary>
public sealed record CapabilitySnapshot(
    DateTimeOffset TakenAt,
    IReadOnlyList<DetectedTool> Tools)
{
    public IEnumerable<DetectedTool> Installed => Tools.Where(t => t.Installed);
}
