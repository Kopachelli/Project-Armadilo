namespace Armadillo.Core.Spawning;

/// <summary>
/// A fully-resolved request to run a child process. The user task goes via <see cref="StdinText"/>,
/// NEVER into <see cref="Arguments"/> — argv is for flags we control (avoids the Windows cmdline cap,
/// quoting breakage, and flag-injection where task text starting with '-' becomes a CLI flag).
/// </summary>
public sealed record RunSpec
{
    public required string FilePath { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string? StdinText { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>();
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
    public long StdoutCapBytes { get; init; } = 10L * 1024 * 1024;
    public long StderrCapBytes { get; init; } = 1L * 1024 * 1024;
}

/// <summary>The result of a finished (or terminated) child process.</summary>
public sealed record RunResult
{
    public int? ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool Killed { get; init; }
    public bool StdoutTruncated { get; init; }
    public required string Stdout { get; init; }
    public required string Stderr { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>Clean exit with code 0 and no truncation/kill.</summary>
    public bool Success => ExitCode == 0 && !TimedOut && !Killed;
}
