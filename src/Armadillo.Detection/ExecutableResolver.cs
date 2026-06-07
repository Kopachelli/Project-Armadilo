namespace Armadillo.Detection;

/// <summary>
/// Resolves CLI executables Windows-natively: search dirs × candidate names × PATHEXT.
/// There is no Unix exec bit on Windows, so a name resolves if a file with a PATHEXT extension
/// exists in a search directory. Ported from Quiver-Pro's AgentEnvironment / PlatformSourceDetector.
/// </summary>
public sealed class ExecutableResolver
{
    private readonly IReadOnlyList<string> _searchDirs;
    private readonly string[] _pathExt;

    public ExecutableResolver(IReadOnlyList<string>? searchDirs = null)
    {
        _searchDirs = searchDirs ?? BuildDefaultSearchDirs();
        _pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public IReadOnlyList<string> SearchDirectories => _searchDirs;

    /// <summary>First resolved absolute path for any of <paramref name="names"/>, or null.</summary>
    public string? Resolve(IReadOnlyList<string> names)
    {
        foreach (var dir in _searchDirs)
        {
            foreach (var name in names)
            {
                var hit = ResolveIn(dir, name);
                if (hit is not null) return hit;
            }
        }
        return null;
    }

    private string? ResolveIn(string dir, string name)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        var bare = Path.Combine(dir, name);
        if (Path.HasExtension(bare) && File.Exists(bare)) return bare;
        foreach (var ext in _pathExt)
        {
            var candidate = Path.Combine(dir, name + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IReadOnlyList<string> BuildDefaultSearchDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var dirs = new List<string>
        {
            Path.Combine(home, ".local", "bin"),
            Path.Combine(home, ".opencode", "bin"),
            Path.Combine(home, ".bun", "bin"),
            Path.Combine(localAppData, "Microsoft", "WindowsApps"),
            Path.Combine(appData, "npm"),
            programFiles,
        };

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            dirs.Add(dir);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return dirs.Where(d => !string.IsNullOrWhiteSpace(d) && seen.Add(d)).ToList();
    }
}
