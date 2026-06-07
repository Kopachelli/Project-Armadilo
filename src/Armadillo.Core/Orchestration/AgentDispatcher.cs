using Armadillo.Core.Adapters;
using Armadillo.Core.Brain;
using Armadillo.Core.Providers;
using Armadillo.Core.Persistence;
using Armadillo.Core.Review;
using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Orchestration;

/// <summary>An ask to spawn one agent. The lineage carries the fork-bomb guard's accounting.</summary>
public sealed record SpawnRequest
{
    public required string Persona { get; init; }
    public required string Task { get; init; }
    public ToolId Tool { get; init; } = ToolId.Claude;
    public SpawnLineage Lineage { get; init; } = SpawnLineage.NewRoot();
    public ProviderProfile Provider { get; init; } = ProviderProfile.Inherit;
    public string? RequesterSession { get; init; }
    public string? ParentJobId { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed record SpawnOutcome
{
    public required string JobId { get; init; }
    public string? SessionId { get; init; }
    public bool Ok { get; init; }
    public string FinalText { get; init; } = "";
    public Verdict Verdict { get; init; } = Verdict.Skipped;
    public double Confidence { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public string? Reason { get; init; }

    public static SpawnOutcome Denied(string jobId, string reason) =>
        new() { JobId = jobId, Ok = false, Reason = reason };
}

/// <summary>
/// The heart of the spine: take a request, guard it, build a brief (with brain-retrieved context),
/// resolve the tool, spawn it headlessly, capture the transcript, persist, review locally, and
/// capture a learning. Returns the agent's result to the caller.
/// </summary>
public sealed class AgentDispatcher
{
    private readonly AdapterRegistry _adapters;
    private readonly IToolDetector _detector;
    private readonly IProcessRunner _runner;
    private readonly IStore _store;
    private readonly IBrain _brain;
    private readonly IReviewer _reviewer;
    private readonly IGovernor _governor;
    private readonly IPathProvider _paths;
    private readonly McpEndpoint? _mcp;
    private readonly Action<string>? _log;

    public AgentDispatcher(
        AdapterRegistry adapters, IToolDetector detector, IProcessRunner runner, IStore store,
        IBrain brain, IReviewer reviewer, IGovernor governor, IPathProvider paths,
        McpEndpoint? mcp = null, Action<string>? log = null)
    {
        _adapters = adapters;
        _detector = detector;
        _runner = runner;
        _store = store;
        _brain = brain;
        _reviewer = reviewer;
        _governor = governor;
        _paths = paths;
        _mcp = mcp;
        _log = log;
    }

    public async Task<SpawnOutcome> DispatchAsync(SpawnRequest req, CancellationToken ct = default)
    {
        var jobId = Ids.New("job");
        var now = DateTimeOffset.UtcNow;

        // 1. Fork-bomb / budget guard FIRST.
        var (acquired, release, denyReason) = _governor.BeginSession(req.Lineage);
        if (!acquired)
        {
            _store.Audit("governor", "deny", jobId, denyReason);
            _log?.Invoke($"[governor] denied {jobId}: {denyReason}");
            return SpawnOutcome.Denied(jobId, denyReason ?? "denied");
        }

        using var _ = release;
        try
        {
            // 2. Persist the job.
            var job = new JobRecord
            {
                JobId = jobId, ParentJobId = req.ParentJobId, RequesterSession = req.RequesterSession,
                RootId = req.Lineage.RootId, Depth = req.Lineage.Depth, Persona = req.Persona,
                Task = req.Task, Tool = req.Tool, State = JobState.Received, CreatedAt = now, UpdatedAt = now,
            };
            _store.SaveJob(job);

            // 3. Resolve the tool.
            if (!_adapters.Supports(req.Tool))
                return Fail(job, "no adapter registered for tool");
            var detected = await _detector.DetectAsync(req.Tool, ct).ConfigureAwait(false);
            if (!detected.Installed || detected.ExecutablePath is null)
                return Fail(job, $"{req.Tool} is not installed on this machine");

            // 4. Brain: retrieve relevant prior learnings, fold into the persona.
            var context = await _brain.RetrieveContextAsync(req.Persona, req.Task, ct: ct).ConfigureAwait(false);
            var persona = string.IsNullOrWhiteSpace(context) ? req.Persona : $"{req.Persona}\n\n{context}";

            // 5. Build the brief + run spec. Child lineage is injected so a sub-spawn is bounded.
            var sessionId = Ids.New("ses");
            var sessionDir = _paths.SessionDir(sessionId);
            var worktree = _paths.WorktreeDir(sessionId);
            Directory.CreateDirectory(sessionDir);
            Directory.CreateDirectory(worktree);

            var childLineage = req.Lineage; // the spawned process runs AT this depth
            var mcpConfigPath = WriteChildMcpConfig(sessionDir, childLineage);
            var brief = new AgentBrief
            {
                Persona = persona,
                Task = req.Task,
                Provider = req.Provider,
                WorkingDirectory = worktree,
                Timeout = req.Timeout,
                McpConfigPath = mcpConfigPath,
                ExtraEnv = new Dictionary<string, string>
                {
                    ["ARMADILLO_ROOT"] = childLineage.RootId,
                    ["ARMADILLO_DEPTH"] = childLineage.Depth.ToString(),
                },
            };

            var adapter = _adapters.Get(req.Tool);
            var spec = adapter.BuildRunSpec(brief, detected.ExecutablePath);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "brief.json"),
                System.Text.Json.JsonSerializer.Serialize(new { req.Persona, req.Task, req.Tool, brief.Provider }),
                ct).ConfigureAwait(false);

            job = job with { State = JobState.Spawning, UpdatedAt = DateTimeOffset.UtcNow };
            _store.SaveJob(job);
            _log?.Invoke($"[dispatch] {jobId} -> spawning {req.Tool} (depth {childLineage.Depth})");

            // 6. Run + capture.
            var startedAt = DateTimeOffset.UtcNow;
            var result = await _runner.RunAsync(spec, onStdoutLine: null, ct).ConfigureAwait(false);
            var endedAt = DateTimeOffset.UtcNow;

            var transcriptPath = Path.Combine(sessionDir, "transcript.ndjson");
            await File.WriteAllTextAsync(transcriptPath, result.Stdout, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result.Stderr))
                await File.WriteAllTextAsync(Path.Combine(sessionDir, "stderr.log"), result.Stderr, ct).ConfigureAwait(false);

            var parsed = adapter.Parse(result);

            var session = new SessionRecord
            {
                SessionId = sessionId, JobId = jobId, Tool = req.Tool, Model = parsed.Model,
                ProviderSessionId = parsed.SessionId,
                ExitReason = result.TimedOut ? "timeout" : result.Killed ? "killed" : parsed.IsError ? "error" : "completed",
                InputTokens = parsed.Usage.InputTokens, OutputTokens = parsed.Usage.OutputTokens,
                CostUsd = parsed.Usage.CostUsd, TranscriptPath = transcriptPath,
                StartedAt = startedAt, EndedAt = endedAt,
            };
            _store.SaveSession(session);

            job = job with { State = JobState.Completed, UpdatedAt = DateTimeOffset.UtcNow };
            _store.SaveJob(job);

            // 7. Local-first review.
            var review = await _reviewer.ReviewAsync(req.Task, parsed.FinalText, ct).ConfigureAwait(false);
            _store.SaveReview(new ReviewRecord
            {
                ReviewId = Ids.New("rev"), SessionId = sessionId, ReviewerModel = review.ReviewerModel,
                Verdict = review.Verdict.ToString().ToLowerInvariant(), Confidence = review.Confidence,
                Rationale = review.Rationale, At = DateTimeOffset.UtcNow,
            });
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "review.json"),
                System.Text.Json.JsonSerializer.Serialize(review), ct).ConfigureAwait(false);

            job = job with { State = JobState.Reviewed, UpdatedAt = DateTimeOffset.UtcNow };
            _store.SaveJob(job);

            // 8. Brain: capture the learning.
            await _brain.CaptureAsync(job, session, parsed, review, ct).ConfigureAwait(false);

            job = job with { State = JobState.Returned, UpdatedAt = DateTimeOffset.UtcNow };
            _store.SaveJob(job);
            _store.Audit("dispatcher", "completed", jobId,
                $"tool={req.Tool};exit={session.ExitReason};verdict={review.Verdict};in={session.InputTokens};out={session.OutputTokens}");

            _log?.Invoke($"[dispatch] {jobId} done: {session.ExitReason}, review={review.Verdict} ({review.Confidence:0.00})");

            return new SpawnOutcome
            {
                JobId = jobId, SessionId = sessionId, Ok = !parsed.IsError,
                FinalText = parsed.FinalText, Verdict = review.Verdict, Confidence = review.Confidence,
                InputTokens = session.InputTokens, OutputTokens = session.OutputTokens,
                Reason = session.ExitReason,
            };
        }
        catch (Exception ex)
        {
            _store.Audit("dispatcher", "error", jobId, ex.Message);
            _log?.Invoke($"[dispatch] {jobId} ERROR: {ex.Message}");
            return SpawnOutcome.Denied(jobId, "error: " + ex.Message);
        }
    }

    /// <summary>
    /// If the harness MCP server is live, write a per-session <c>.mcp.json</c> pointing the spawned
    /// agent back at it, carrying the shared token + this session's lineage header. That lineage lets
    /// the Governor bound recursion when the spawned agent itself calls <c>request_agent</c>.
    /// </summary>
    private string? WriteChildMcpConfig(string sessionDir, SpawnLineage lineage)
    {
        if (_mcp is not { IsLive: true }) return null;

        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["armadillo"] = new
                {
                    type = "http",
                    url = _mcp.Url,
                    headers = new Dictionary<string, string>
                    {
                        ["X-Armadillo-Token"] = _mcp.Token ?? "",
                        ["X-Armadillo-Lineage"] = $"{lineage.RootId}:{lineage.Depth}",
                    },
                },
            },
        };
        var path = Path.Combine(sessionDir, "mcp.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(config,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private SpawnOutcome Fail(JobRecord job, string reason)
    {
        _store.SaveJob(job with { State = JobState.Failed, UpdatedAt = DateTimeOffset.UtcNow });
        _store.Audit("dispatcher", "failed", job.JobId, reason);
        _log?.Invoke($"[dispatch] {job.JobId} failed: {reason}");
        return SpawnOutcome.Denied(job.JobId, reason);
    }
}
