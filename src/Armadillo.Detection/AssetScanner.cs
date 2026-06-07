using System.Text.Json;
using System.Text.RegularExpressions;
using Armadillo.Core.Tools;

namespace Armadillo.Detection;

/// <summary>
/// "Fetch everything": for a given tool variant, discover its skills (SKILL.md), MCP servers, and
/// config files. Knows where each tool/brand keeps assets — and crucially distinguishes a CLI's
/// locations (e.g. ~/.claude) from its desktop app's (e.g. %APPDATA%/Claude/claude_desktop_config.json).
/// </summary>
public sealed partial class AssetScanner
{
    private readonly string _home;
    public AssetScanner(string? home = null)
        => _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public ToolAssets Scan(ToolId id)
    {
        var (skillRoots, configFiles) = AssetsFor(id);
        var skills = new List<string>();
        var mcp = new List<string>();
        var foundConfigs = new List<string>();

        foreach (var root in skillRoots)
            CollectSkills(Expand(root), skills);

        foreach (var cf in configFiles)
        {
            var path = Expand(cf);
            if (!File.Exists(path)) continue;
            foundConfigs.Add(path);
            CollectMcpServers(path, mcp);
        }

        return new ToolAssets(
            Dedup(skills), Dedup(mcp), foundConfigs);
    }

    // --- where each tool keeps its assets ---

    private static (string[] SkillRoots, string[] ConfigFiles) AssetsFor(ToolId id) => id switch
    {
        ToolId.Claude => (new[] { "~/.claude/skills", "~/.agents/skills" },
                          new[] { "~/.claude/.mcp.json", "~/.claude.json", "~/.claude/settings.json" }),
        ToolId.ClaudeDesktop => (Array.Empty<string>(),
                          new[] { "%APPDATA%/Claude/claude_desktop_config.json" }),
        ToolId.Cursor or ToolId.CursorDesktop => (new[] { "~/.cursor/skills", "~/.agents/skills" },
                          new[] { "~/.cursor/mcp.json" }),
        ToolId.Codex or ToolId.CodexApp => (new[] { "~/.codex/skills", "~/.agents/skills" },
                          new[] { "~/.codex/config.toml" }),
        ToolId.Gemini => (new[] { "~/.gemini/skills" }, new[] { "~/.gemini/settings.json" }),
        ToolId.Qwen or ToolId.QwenDesktop => (new[] { "~/.qwen/skills" }, new[] { "~/.qwen/settings.json" }),
        ToolId.Antigravity or ToolId.AntigravityApp => (Array.Empty<string>(),
                          new[] { "~/.antigravity/mcp_config.json", "~/.gemini/settings.json" }),
        ToolId.Pi => (new[] { "~/.pi/agent/skills", "~/.agents/skills" }, Array.Empty<string>()),
        ToolId.OpenCode or ToolId.OpenCodeDesktop => (new[] { "~/.config/opencode/skills" },
                          new[] { "~/.opencode/opencode.json", "~/.config/opencode/opencode.json" }),
        ToolId.OpenClaw => (new[] { "~/.openclaw/skills", "~/.agents/skills" }, Array.Empty<string>()),
        ToolId.Hermes or ToolId.HermesDesktop => (new[] { "~/.hermes/skills" }, new[] { "~/.hermes/config.yaml" }),
        ToolId.Copilot or ToolId.CopilotDesktop => (Array.Empty<string>(),
                          new[] { "~/.copilot/mcp-config.json", "~/.config/github-copilot/mcp.json" }),
        ToolId.Kimi or ToolId.KimiDesktop => (Array.Empty<string>(), new[] { "~/.kimi/config.json" }),
        ToolId.MiniMax or ToolId.MiniMaxDesktop => (Array.Empty<string>(), new[] { "~/.minimax/config.json" }),
        _ => (Array.Empty<string>(), Array.Empty<string>()),
    };

    // --- helpers ---

    private string Expand(string template)
    {
        var p = template.Replace("~/", _home + "/").Replace("~\\", _home + "\\");
        p = Environment.ExpandEnvironmentVariables(p);
        return Path.GetFullPath(p);
    }

    private static void CollectSkills(string root, List<string> skills)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(file);
                if (dir is not null) skills.Add(Path.GetFileName(dir));
                if (skills.Count > 500) break;
            }
        }
        catch { /* unreadable dir */ }
    }

    private static void CollectMcpServers(string path, List<string> servers)
    {
        try
        {
            var text = File.ReadAllText(path);
            if (path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in TomlMcpHeader().Matches(text))
                    servers.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                return;
            }
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("mcpServers", out var ms) && ms.ValueKind == JsonValueKind.Object)
                foreach (var p in ms.EnumerateObject()) servers.Add(p.Name);
        }
        catch { /* malformed config */ }
    }

    private static IReadOnlyList<string> Dedup(IEnumerable<string> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return items.Where(seen.Add).ToList();
    }

    // Capture only the first segment after `mcp_servers.` so nested sub-tables
    // (e.g. [mcp_servers.foo.env]) collapse to the server name "foo".
    [GeneratedRegex(@"^\s*\[mcp_servers\.(?:""([^""]+)""|([^.\]\s]+))", RegexOptions.Multiline)]
    private static partial Regex TomlMcpHeader();
}
