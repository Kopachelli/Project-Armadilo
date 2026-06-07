namespace Armadillo.Core.Spawning;

/// <summary>
/// Spawns and supervises child processes. The seam that lets tests swap in a fake instead of
/// launching real CLIs. Implementations must enforce timeout + stdout cap and kill the whole
/// process tree on timeout/cancellation.
/// </summary>
public interface IProcessRunner
{
    /// <param name="spec">What to run.</param>
    /// <param name="onStdoutLine">Optional callback invoked per stdout line (for live NDJSON parsing).</param>
    Task<RunResult> RunAsync(RunSpec spec, Action<string>? onStdoutLine = null, CancellationToken ct = default);
}
