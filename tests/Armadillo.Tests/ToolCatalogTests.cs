using Armadillo.Core.Adapters;
using Armadillo.Core.Tools;
using Xunit;

namespace Armadillo.Tests;

public class ToolCatalogTests
{
    [Theory]
    [InlineData(ToolId.Antigravity)]
    [InlineData(ToolId.Hermes)]
    [InlineData(ToolId.OpenClaw)]
    [InlineData(ToolId.Gemini)]
    public void Cli_tools_are_drivable(ToolId id)
        => Assert.True(ToolDescriptor.For(id).Drivable);

    [Theory]
    [InlineData(ToolId.CopilotDesktop)]
    [InlineData(ToolId.CodexApp)]
    [InlineData(ToolId.ClaudeDesktop)]
    public void Desktop_apps_are_not_drivable(ToolId id)
    {
        var d = ToolDescriptor.For(id);
        Assert.Equal(ToolKind.Desktop, d.Kind);
        Assert.False(d.Drivable);
        Assert.NotEmpty(d.AppPaths);          // detected by install path…
        Assert.Empty(d.ExecutableNames);      // …never by a PATH name that could collide with a CLI
    }

    [Fact]
    public void OpenClaw_and_OpenCode_are_distinct_tools()
    {
        Assert.DoesNotContain("openclaw", ToolDescriptor.For(ToolId.OpenCode).ExecutableNames);
        Assert.Contains("openclaw", ToolDescriptor.For(ToolId.OpenClaw).ExecutableNames);
    }

    [Fact]
    public void Antigravity_takes_prompt_as_argv_flag_not_stdin()
    {
        var spec = new AntigravityAdapter().BuildRunSpec(
            new AgentBrief { Persona = "", Task = "do x" }, @"C:\bin\agy.exe");
        Assert.Null(spec.StdinText);
        Assert.Contains("-p", spec.Arguments);
        Assert.Contains("do x", spec.Arguments);
    }

    [Fact]
    public void Hermes_uses_chat_dash_q()
    {
        var spec = new HermesAdapter().BuildRunSpec(
            new AgentBrief { Persona = "", Task = "do y" }, @"C:\bin\hermes.exe");
        Assert.Equal(new[] { "chat", "-q", "do y" }, spec.Arguments);
    }
}
