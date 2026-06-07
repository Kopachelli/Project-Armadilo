using Armadillo.Core.Tools;
using Armadillo.Detection;
using Xunit;

namespace Armadillo.Tests;

public class InstalledAppsTests
{
    [Fact]
    public void Match_is_case_insensitive_substring()
    {
        var apps = new[]
        {
            new InstalledApp("Cursor", @"C:\Users\x\AppData\Local\Programs\cursor", null),
            new InstalledApp("OpenCode 1.2.3", null, null),
        };
        Assert.Equal("Cursor", WindowsInstalledApps.Match(apps, new[] { "cursor" })?.DisplayName);
        Assert.Equal("OpenCode 1.2.3", WindowsInstalledApps.Match(apps, new[] { "OpenCode" })?.DisplayName);
        Assert.Null(WindowsInstalledApps.Match(apps, new[] { "Photoshop" }));
    }

    [Theory]
    [InlineData(ToolId.Kimi)]
    [InlineData(ToolId.MiniMax)]
    public void New_cli_tools_are_drivable(ToolId id) => Assert.True(ToolDescriptor.For(id).Drivable);

    [Theory]
    [InlineData(ToolId.CursorDesktop)]
    [InlineData(ToolId.QwenDesktop)]
    [InlineData(ToolId.KimiDesktop)]
    [InlineData(ToolId.MiniMaxDesktop)]
    [InlineData(ToolId.OpenCodeDesktop)]
    [InlineData(ToolId.AntigravityApp)]
    public void New_desktop_apps_detect_by_name_pattern(ToolId id)
    {
        var d = ToolDescriptor.For(id);
        Assert.Equal(ToolKind.Desktop, d.Kind);
        Assert.False(d.Drivable);
        Assert.NotEmpty(d.AppNamePatterns);
    }
}
