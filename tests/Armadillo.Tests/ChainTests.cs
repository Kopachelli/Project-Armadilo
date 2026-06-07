using Armadillo.Core.Orchestration;
using Armadillo.Core.Review;
using Armadillo.Core.Tools;
using Xunit;

namespace Armadillo.Tests;

public class ChainTests
{
    [Fact]
    public async Task Threads_output_of_each_step_into_the_next()
    {
        var seenTasks = new List<string>();
        var n = 0;
        var runner = new ChainRunner((req, ct) =>
        {
            seenTasks.Add(req.Task);
            n++;
            return Task.FromResult(new SpawnOutcome
            { JobId = $"j{n}", SessionId = "s", Ok = true, FinalText = $"RESULT_{n}", Verdict = Verdict.Approve, Confidence = 1 });
        });

        var steps = new[]
        {
            new ChainStep("implement", ToolId.Claude, "p", "{goal}"),
            new ChainStep("review", ToolId.Gemini, "p", "review: {input}"),
        };
        var result = await runner.RunAsync(steps, "GOAL");

        Assert.Equal("GOAL", seenTasks[0]);             // {goal} substituted
        Assert.Equal("review: RESULT_1", seenTasks[1]); // {input} = previous output
        Assert.Equal("RESULT_2", result.FinalText);
        Assert.True(result.Ok);
        Assert.Equal(2, result.Steps.Count);
    }

    [Fact]
    public async Task Stops_on_first_failed_step()
    {
        var runner = new ChainRunner((req, ct) => Task.FromResult(new SpawnOutcome
        { JobId = "j", Ok = false, Reason = "boom", FinalText = "", Verdict = Verdict.Reject }));

        var steps = new[]
        {
            new ChainStep("a", ToolId.Claude, "p", "{goal}"),
            new ChainStep("b", ToolId.Claude, "p", "{input}"),
        };
        var result = await runner.RunAsync(steps, "g");

        Assert.False(result.Ok);
        Assert.Single(result.Steps);  // second step never ran
    }

    [Theory]
    [InlineData("review: {input}", "g", "X", "review: X")]
    [InlineData("{goal}", "g", "X", "g")]
    public void Render_substitutes_placeholders(string tpl, string goal, string prev, string expected)
        => Assert.Equal(expected, ChainRunner.Render(tpl, goal, prev));

    [Fact]
    public void Render_without_placeholder_appends_previous_output()
        => Assert.Equal("do\n\n--- Previous step output ---\nprev", ChainRunner.Render("do", "g", "prev"));

    [Fact]
    public void IsGitRepo_detects_dot_git()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "armadillo-wt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Assert.False(WorktreeManager.IsGitRepo(tmp));
            Directory.CreateDirectory(Path.Combine(tmp, ".git"));
            Assert.True(WorktreeManager.IsGitRepo(tmp));
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
