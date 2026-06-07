using Armadillo.Core.Adapters;
using Armadillo.Core.Spawning;
using Xunit;

namespace Armadillo.Tests;

public class ClaudeAdapterTests
{
    [Fact]
    public void Parses_stream_json_result_usage_and_session()
    {
        var stdout = string.Join('\n',
            """{"type":"system","subtype":"init","session_id":"abc","model":"claude-opus-4-8"}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Hello "}]}}""",
            """{"type":"result","subtype":"success","result":"Hello world","session_id":"abc","is_error":false,"usage":{"input_tokens":10,"output_tokens":5},"total_cost_usd":0.01}""");

        var result = new RunResult { ExitCode = 0, Stdout = stdout, Stderr = "" };
        var parsed = new ClaudeAdapter().Parse(result);

        Assert.Equal("Hello world", parsed.FinalText);
        Assert.Equal("abc", parsed.SessionId);
        Assert.Equal("claude-opus-4-8", parsed.Model);
        Assert.Equal(10, parsed.Usage.InputTokens);
        Assert.Equal(5, parsed.Usage.OutputTokens);
        Assert.False(parsed.IsError);
    }

    [Fact]
    public void Falls_back_to_plain_text_when_not_stream_json()
    {
        var result = new RunResult { ExitCode = 0, Stdout = "just plain text output", Stderr = "" };
        var parsed = new ClaudeAdapter().Parse(result);
        Assert.Equal("just plain text output", parsed.FinalText);
    }

    [Fact]
    public void Ollama_adapter_strips_ansi_spinner_codes()
    {
        var result = new RunResult { ExitCode = 0, Stdout = "hello\x1b[1D\x1b[Kworld", Stderr = "" };
        var parsed = new OllamaCliAdapter().Parse(result);
        Assert.Equal("helloworld", parsed.FinalText);
    }

    [Fact]
    public void Ollama_adapter_runs_model_with_stdin_task()
    {
        var brief = new AgentBrief { Persona = "", Task = "hi", Provider = new Armadillo.Core.Providers.ProviderProfile("m", Armadillo.Core.Providers.ProviderKind.Local, Model: "qwen2.5-coder:7b") };
        var spec = new OllamaCliAdapter().BuildRunSpec(brief, @"C:\bin\ollama.exe");
        Assert.Equal(new[] { "run", "qwen2.5-coder:7b" }, spec.Arguments);
        Assert.Equal("hi", spec.StdinText);
    }

    [Fact]
    public void Builds_run_spec_with_stdin_task_and_stream_json_flags()
    {
        var brief = new AgentBrief { Persona = "be terse", Task = "do the thing" };
        var spec = new ClaudeAdapter().BuildRunSpec(brief, @"C:\bin\claude.exe");

        Assert.Equal("do the thing", spec.StdinText);          // task via stdin, never argv
        Assert.Contains("-p", spec.Arguments);
        Assert.Contains("stream-json", spec.Arguments);
        Assert.DoesNotContain("do the thing", spec.Arguments);  // task must not leak into argv
    }
}
