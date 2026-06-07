using Armadillo.Core.Tools;

namespace Armadillo.Core.Orchestration;

/// <summary>
/// The Registrator's "decide what to call". v1 = preference → availability over a sane priority
/// order. Learned routing priors (from review outcomes) plug in here in Phase 4.
/// </summary>
public interface IRouter
{
    /// <summary>Resolve the tool to actually use: the preferred one if available, else the best installed
    /// alternative the harness can drive, else null.</summary>
    Task<ToolId?> ChooseAsync(ToolId? preferred, CancellationToken ct = default);
}

public sealed class Router : IRouter
{
    // Richest/most-reliable first.
    private static readonly ToolId[] Priority =
    {
        ToolId.Claude, ToolId.Cursor, ToolId.Codex, ToolId.Gemini,
        ToolId.Qwen, ToolId.Copilot, ToolId.OpenCode, ToolId.Pi,
    };

    private readonly IToolDetector _detector;
    private readonly AdapterRegistry _adapters;

    public Router(IToolDetector detector, AdapterRegistry adapters)
    {
        _detector = detector;
        _adapters = adapters;
    }

    public async Task<ToolId?> ChooseAsync(ToolId? preferred, CancellationToken ct = default)
    {
        if (preferred is { } p && _adapters.Supports(p) && (await _detector.DetectAsync(p, ct)).Installed)
            return p;

        var snapshot = await _detector.DetectAsync(ct);
        var installed = snapshot.Tools.Where(t => t.Installed).Select(t => t.Id).ToHashSet();
        foreach (var tool in Priority)
            if (_adapters.Supports(tool) && installed.Contains(tool))
                return tool;
        return null;
    }
}
