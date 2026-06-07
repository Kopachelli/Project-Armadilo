using Armadillo.Core.Spawning;

namespace Armadillo.Core.Orchestration;

/// <summary>
/// Creates/removes git worktrees so parallel sessions working on the same repo are isolated (each on
/// its own branch + directory) and can't stomp each other. Falls back gracefully when the target
/// isn't a git repo.
/// </summary>
public sealed class WorktreeManager
{
    private readonly IProcessRunner _runner;
    public WorktreeManager(IProcessRunner runner) => _runner = runner;

    public static bool IsGitRepo(string path)
    {
        try
        {
            if (Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git")))
                return true;
            // also handle subdirectories of a repo
            var dir = new DirectoryInfo(path);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return true;
                dir = dir.Parent;
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>git worktree add -b armadillo/&lt;sid&gt; &lt;worktreePath&gt; HEAD. Returns the path, or null on failure.</summary>
    public async Task<string?> CreateAsync(string repoPath, string sessionId, string worktreePath,
        CancellationToken ct = default)
    {
        var branch = "armadillo/" + sessionId;
        var r = await Git(repoPath, new[] { "worktree", "add", "-b", branch, worktreePath, "HEAD" }, ct)
            .ConfigureAwait(false);
        return r.Success ? worktreePath : null;
    }

    public async Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct = default)
    {
        await Git(repoPath, new[] { "worktree", "remove", "--force", worktreePath }, ct).ConfigureAwait(false);
        await Git(repoPath, new[] { "worktree", "prune" }, ct).ConfigureAwait(false);
    }

    private Task<RunResult> Git(string repoPath, string[] args, CancellationToken ct)
    {
        var all = new List<string> { "-C", repoPath };
        all.AddRange(args);
        return _runner.RunAsync(new RunSpec
        {
            FilePath = "git",
            Arguments = all,
            Timeout = TimeSpan.FromSeconds(60),
        }, onStdoutLine: null, ct);
    }
}
