using Armadillo.Core.Tools;

namespace Armadillo.Core.Persistence;

public enum JobState { Received, Routed, Spawning, Supervised, Completed, Reviewed, Returned, Failed }

/// <summary>One inbound request (e.g. an MCP <c>request_agent</c> call). May own N sessions.</summary>
public sealed record JobRecord
{
    public required string JobId { get; init; }
    public string? ParentJobId { get; init; }
    public string? RequesterSession { get; init; }
    public required string RootId { get; init; }
    public int Depth { get; init; }
    public required string Persona { get; init; }
    public required string Task { get; init; }
    public ToolId Tool { get; init; }
    public JobState State { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One spawned agent process and its outcome.</summary>
public sealed record SessionRecord
{
    public required string SessionId { get; init; }
    public required string JobId { get; init; }
    public ToolId Tool { get; init; }
    public string? Model { get; init; }
    public string? ProviderSessionId { get; init; }
    public string ExitReason { get; init; } = "";
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public double CostUsd { get; init; }
    public string? TranscriptPath { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
}

/// <summary>A review verdict on a session's output (local-first).</summary>
public sealed record ReviewRecord
{
    public required string ReviewId { get; init; }
    public required string SessionId { get; init; }
    public string ReviewerModel { get; init; } = "";
    public string Verdict { get; init; } = "";   // approve | revise | reject | escalate
    public double Confidence { get; init; }       // 0..1
    public string Rationale { get; init; } = "";
    public DateTimeOffset At { get; init; }
}

/// <summary>
/// A versioned persona/system-prompt template — the unit the brain self-improves (procedural memory).
/// Exactly one version per name is <see cref="Active"/>.
/// </summary>
public sealed record PlaybookRecord
{
    public required string Name { get; init; }
    public int Version { get; init; }
    public required string Text { get; init; }
    public double Score { get; init; }
    public bool Active { get; init; }
    public string? SourceNote { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A recorded A/B comparison between an incumbent and a candidate playbook version.</summary>
public sealed record ExperimentRecord
{
    public required string ExperimentId { get; init; }
    public required string PlaybookName { get; init; }
    public int IncumbentVersion { get; init; }
    public int CandidateVersion { get; init; }
    public double IncumbentScore { get; init; }
    public double CandidateScore { get; init; }
    public int SampleSize { get; init; }
    public string Outcome { get; init; } = "";   // promoted | rejected | staged | rolledback | halted
    public string Detail { get; init; } = "";
    public DateTimeOffset At { get; init; }
}

/// <summary>A distilled, reusable learning captured from a reviewed run (the brain's semantic memory).</summary>
public sealed record LearningRecord
{
    public required string LearningId { get; init; }
    public required string Persona { get; init; }
    public ToolId Tool { get; init; }
    public required string Text { get; init; }
    public double Confidence { get; init; }
    public string? SourceJob { get; init; }
    public float[]? Embedding { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
