namespace Armadillo.Core;

/// <summary>
/// Single source of truth for every path the harness uses. The ONE place OS path conventions
/// leak — a POSIX implementation later changes only this. Defaults to a per-user workspace under
/// <c>%LOCALAPPDATA%\Armadillo</c>.
/// </summary>
public interface IPathProvider
{
    /// <summary>Root workspace dir (everything lives under here).</summary>
    string Workspace { get; }

    /// <summary>The SQLite database file.</summary>
    string DatabaseFile { get; }

    /// <summary>Config dir (settings.json, personas/, playbooks/, chains/).</summary>
    string ConfigDir { get; }

    /// <summary>Per-job dir.</summary>
    string JobDir(string jobId);

    /// <summary>Per-session dir (brief, argv, env, transcript.ndjson, stdout.log, review.json).</summary>
    string SessionDir(string sessionId);

    /// <summary>Isolated working dir / git worktree for a session.</summary>
    string WorktreeDir(string sessionId);

    /// <summary>Daily log file dir.</summary>
    string LogDir { get; }

    /// <summary>The user's home directory (for locating tool dotfolders).</summary>
    string HomeDirectory { get; }

    /// <summary>Create the base workspace skeleton if missing. Idempotent.</summary>
    void EnsureWorkspace();
}

/// <inheritdoc />
public sealed class PathProvider : IPathProvider
{
    public string Workspace { get; }
    public string HomeDirectory { get; }

    public PathProvider(string? workspace = null, string? home = null)
    {
        HomeDirectory = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Workspace = workspace
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Armadillo");
    }

    public string DatabaseFile => Path.Combine(Workspace, "armadillo.db");
    public string ConfigDir => Path.Combine(Workspace, "config");
    public string LogDir => Path.Combine(Workspace, "logs");

    public string JobDir(string jobId) => Path.Combine(Workspace, "jobs", Safe(jobId));
    public string SessionDir(string sessionId) => Path.Combine(Workspace, "sessions", Safe(sessionId));
    public string WorktreeDir(string sessionId) => Path.Combine(Workspace, "worktrees", Safe(sessionId));

    public void EnsureWorkspace()
    {
        Directory.CreateDirectory(Workspace);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(Path.Combine(Workspace, "jobs"));
        Directory.CreateDirectory(Path.Combine(Workspace, "sessions"));
        Directory.CreateDirectory(Path.Combine(Workspace, "worktrees"));
    }

    /// <summary>Ids become path components — reject traversal/odd characters.</summary>
    private static string Safe(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException($"Unsafe id for a path component: '{id}'", nameof(id));
        return id;
    }
}
