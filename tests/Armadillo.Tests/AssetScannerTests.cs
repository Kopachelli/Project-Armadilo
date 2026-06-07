using Armadillo.Core.Tools;
using Armadillo.Detection;
using Xunit;

namespace Armadillo.Tests;

public class AssetScannerTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "armadillo-assets-" + Guid.NewGuid().ToString("N"));

    public AssetScannerTests() => Directory.CreateDirectory(_home);

    [Fact]
    public void Reads_mcp_servers_from_json_config()
    {
        var dir = Path.Combine(_home, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".mcp.json"),
            """{"mcpServers":{"github":{"type":"http"},"linear":{"type":"stdio"}}}""");

        var assets = new AssetScanner(_home).Scan(ToolId.Claude);

        Assert.Contains("github", assets.McpServers);
        Assert.Contains("linear", assets.McpServers);
        Assert.Single(assets.ConfigFiles);
    }

    [Fact]
    public void Reads_top_level_mcp_servers_from_toml_collapsing_subtables()
    {
        var dir = Path.Combine(_home, ".codex");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.toml"), """
            [mcp_servers.browser-use]
            command = "x"
            [mcp_servers.browser-use.http_headers]
            k = "v"
            [mcp_servers.linear]
            command = "y"
            """);

        var assets = new AssetScanner(_home).Scan(ToolId.Codex);

        Assert.Contains("browser-use", assets.McpServers);
        Assert.Contains("linear", assets.McpServers);
        Assert.DoesNotContain("browser-use.http_headers", assets.McpServers); // sub-table collapsed
    }

    [Fact]
    public void Reads_plugins_from_cache_manifest()
    {
        var dir = Path.Combine(_home, ".claude", "plugins", "cache", "myplugin", ".claude-plugin");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), """{"name":"my-plugin","version":"1.0"}""");

        var assets = new AssetScanner(_home).Scan(ToolId.Claude);

        Assert.Contains("my-plugin", assets.Plugins);
    }

    [Fact]
    public void Counts_skills_by_skill_md()
    {
        var skillDir = Path.Combine(_home, ".claude", "skills", "my-skill");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: my-skill\n---\n");

        var assets = new AssetScanner(_home).Scan(ToolId.Claude);

        Assert.Contains("my-skill", assets.Skills);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch { }
    }
}
