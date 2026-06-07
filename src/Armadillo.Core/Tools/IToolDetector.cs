namespace Armadillo.Core.Tools;

/// <summary>
/// Detects installed AI CLI tools + local runtimes and builds a capability snapshot. The interface
/// lives in Core so the orchestration layer depends on the abstraction; the concrete probe lives in
/// Armadillo.Detection.
/// </summary>
public interface IToolDetector
{
    Task<CapabilitySnapshot> DetectAsync(CancellationToken ct = default);
    Task<DetectedTool> DetectAsync(ToolId id, CancellationToken ct = default);
}
