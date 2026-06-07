namespace Armadillo.Core.Events;

/// <summary>Token accounting for one session (what we have; tools vary in what they report).</summary>
public sealed record TokenUsage(long InputTokens, long OutputTokens, double CostUsd = 0)
{
    public static readonly TokenUsage Zero = new(0, 0, 0);
    public long Total => InputTokens + OutputTokens;
}

/// <summary>
/// Normalized event emitted by any tool. Each adapter maps its native NDJSON/JSON into these,
/// so everything downstream (persistence, supervision, review, learning) is tool-agnostic.
/// </summary>
public abstract record AgentEvent(DateTimeOffset At);

public sealed record SessionStarted(DateTimeOffset At, string? SessionId, string? Model) : AgentEvent(At);
public sealed record TextDelta(DateTimeOffset At, string Text) : AgentEvent(At);
public sealed record ToolCall(DateTimeOffset At, string Name, string? InputJson) : AgentEvent(At);
public sealed record ToolResult(DateTimeOffset At, string Name, bool IsError, string? Text) : AgentEvent(At);
public sealed record FileWritten(DateTimeOffset At, string Path) : AgentEvent(At);
public sealed record TurnEnded(DateTimeOffset At) : AgentEvent(At);
public sealed record UsageReported(DateTimeOffset At, TokenUsage Usage) : AgentEvent(At);
public sealed record SessionStopped(DateTimeOffset At, string Reason) : AgentEvent(At);

/// <summary>An unparseable line preserved verbatim (text-fallback path).</summary>
public sealed record RawLine(DateTimeOffset At, string Line) : AgentEvent(At);
