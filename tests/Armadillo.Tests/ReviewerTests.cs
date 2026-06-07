using Armadillo.Core.Review;
using Xunit;

namespace Armadillo.Tests;

public class ReviewerTests
{
    [Fact]
    public void Parses_json_verdict_and_normalizes_confidence()
    {
        var r = OllamaReviewer.Interpret("""{"verdict":"approve","confidence":85,"rationale":"looks good"}""", "m");
        Assert.Equal(Verdict.Approve, r.Verdict);
        Assert.Equal(0.85, r.Confidence, 3);
        Assert.Equal("looks good", r.Rationale);
    }

    [Fact]
    public void Extracts_json_embedded_in_prose()
    {
        var r = OllamaReviewer.Interpret("Sure!\n{\"verdict\":\"reject\",\"confidence\":0.4}\nhope that helps", "m");
        Assert.Equal(Verdict.Reject, r.Verdict);
        Assert.Equal(0.4, r.Confidence, 3);
    }

    [Fact]
    public void Falls_back_to_keywords_when_not_json()
    {
        Assert.Equal(Verdict.Revise, OllamaReviewer.Interpret("Please REVISE the second part.", "m").Verdict);
    }

    [Fact]
    public void No_parseable_verdict_escalates()
    {
        var r = OllamaReviewer.Interpret("I am not sure what to make of this.", "m");
        Assert.Equal(Verdict.Escalate, r.Verdict);
    }
}
