using System.Collections.Concurrent;
using System.ComponentModel;
using Armadillo.Core.Orchestration;
using Armadillo.Core.Review;
using Armadillo.Core.Tools;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace Armadillo.Mcp;

/// <summary>In-memory index of finished job results, so get_job_result works after a synchronous request.</summary>
public sealed class JobResults
{
    private readonly ConcurrentDictionary<string, string> _results = new();
    public void Put(string jobId, string text) => _results[jobId] = text;
    public string? Get(string jobId) => _results.TryGetValue(jobId, out var v) ? v : null;
}

/// <summary>
/// The MCP tool surface — this is how a running AI session "calls the administrator". Exposed over
/// Streamable HTTP on 127.0.0.1. A spawned agent is wired back to this same server (with an
/// incremented lineage header), so the Governor can bound recursion.
/// </summary>
[McpServerToolType]
public sealed class RegistratorTools
{
    private readonly AgentDispatcher _dispatcher;
    private readonly McpEndpoint _endpoint;
    private readonly JobResults _results;
    private readonly IHttpContextAccessor _http;

    public RegistratorTools(AgentDispatcher dispatcher, McpEndpoint endpoint, JobResults results,
        IHttpContextAccessor http)
    {
        _dispatcher = dispatcher;
        _endpoint = endpoint;
        _results = results;
        _http = http;
    }

    [McpServerTool(Name = "request_agent")]
    [Description("Delegate a sub-task to a fresh standalone AI agent. The harness picks/spawns a " +
                 "headless CLI agent, captures and reviews its work, and returns the result. Use when " +
                 "you need an independent agent to handle a self-contained piece of work.")]
    public async Task<string> RequestAgent(
        [Description("The self-contained task for the sub-agent to complete.")] string task,
        [Description("Optional persona/system instructions for the sub-agent.")] string? persona = null,
        [Description("Optional tool to use (e.g. 'claude'). Defaults to the best available.")] string? tool = null,
        CancellationToken ct = default)
    {
        var (ok, reason) = Authorize();
        if (!ok) return $"DENIED: {reason}";

        var lineage = CallerChildLineage();
        var toolId = ParseTool(tool);

        var outcome = await _dispatcher.DispatchAsync(new SpawnRequest
        {
            Persona = persona ?? "You are a focused helper agent. Complete the task directly and concisely.",
            Task = task,
            Tool = toolId,
            Lineage = lineage,
            RequesterSession = CallerLineageHeader(),
        }, ct);

        if (!outcome.Ok && outcome.SessionId is null)
            return $"DENIED: {outcome.Reason}";

        _results.Put(outcome.JobId, outcome.FinalText);

        var verdict = outcome.Verdict == Verdict.Skipped ? "" : $" [review: {outcome.Verdict} {outcome.Confidence:0.00}]";
        return $"job={outcome.JobId} status={(outcome.Ok ? "ok" : outcome.Reason)}{verdict}\n\n{outcome.FinalText}";
    }

    [McpServerTool(Name = "get_job_result")]
    [Description("Fetch the final result text for a previously requested job id.")]
    public string GetJobResult([Description("The job id returned by request_agent.")] string jobId)
        => _results.Get(jobId) ?? $"unknown or unfinished job: {jobId}";

    [McpServerTool(Name = "get_job_status")]
    [Description("Check whether a previously requested job has a result available.")]
    public string GetJobStatus([Description("The job id returned by request_agent.")] string jobId)
        => _results.Get(jobId) is null ? $"pending/unknown: {jobId}" : $"completed: {jobId}";

    // --- helpers ---

    private (bool, string?) Authorize()
    {
        if (string.IsNullOrEmpty(_endpoint.Token)) return (true, null); // no token configured
        var header = _http.HttpContext?.Request.Headers["X-Armadillo-Token"].ToString();
        return header == _endpoint.Token ? (true, null) : (false, "invalid or missing X-Armadillo-Token");
    }

    private string? CallerLineageHeader()
        => _http.HttpContext?.Request.Headers["X-Armadillo-Lineage"].ToString() is { Length: > 0 } s ? s : null;

    /// <summary>Child runs at caller-depth + 1; a request with no lineage header starts a new root at depth 0.</summary>
    private SpawnLineage CallerChildLineage()
    {
        var raw = CallerLineageHeader();
        if (raw is not null)
        {
            var idx = raw.LastIndexOf(':');
            if (idx > 0 && int.TryParse(raw[(idx + 1)..], out var depth))
                return new SpawnLineage(raw[..idx], depth + 1);
        }
        return SpawnLineage.NewRoot();
    }

    private static ToolId ParseTool(string? tool)
        => tool is not null && Enum.TryParse<ToolId>(tool, ignoreCase: true, out var t) ? t : ToolId.Claude;
}
