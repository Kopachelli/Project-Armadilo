using System.Security.Cryptography;
using System.Text.Json;
using Armadillo.Core.Orchestration;
using Armadillo.Core.Persistence;
using Armadillo.Core.Review;
using Armadillo.Core.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Armadillo.Mcp;

/// <summary>
/// Hosts the harness's MCP server over Streamable HTTP, bound to 127.0.0.1 only. Generates a shared
/// token, writes an operator <c>.mcp.json</c> clients can point at, and fills the late-bound
/// <see cref="McpEndpoint"/> so the dispatcher can wire spawned children back to this server.
/// </summary>
public static class McpDaemon
{
    public static async Task RunAsync(
        AgentDispatcher dispatcher,
        McpEndpoint endpoint,
        string operatorConfigPath,
        int port = 0,
        string? token = null,
        IStore? store = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        token ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(dispatcher);
        builder.Services.AddSingleton(endpoint);
        builder.Services.AddSingleton<JobResults>();
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<RegistratorTools>();

        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{port}");
        app.MapMcp();
        MapControlApi(app, store, log);

        await app.StartAsync(ct).ConfigureAwait(false);

        var baseUrl = app.Urls.FirstOrDefault() ?? $"http://127.0.0.1:{port}";
        endpoint.Url = baseUrl;
        endpoint.Token = token;

        WriteOperatorConfig(operatorConfigPath, baseUrl, token);

        var sidecarEnv = Path.Combine(Path.GetDirectoryName(operatorConfigPath)!, "sidecar.env");
        File.WriteAllText(sidecarEnv, $"ARMADILLO_URL={baseUrl}\nARMADILLO_TOKEN={token}\n");

        log?.Invoke($"MCP server listening on {baseUrl}");
        log?.Invoke($"control API + MCP on {baseUrl} (loopback, token-protected)");
        log?.Invoke($"operator config written to {operatorConfigPath}");
        log?.Invoke($"sidecar env written to    {sidecarEnv}");
        log?.Invoke("point a CLI at it, e.g.:  claude --mcp-config \"" + operatorConfigPath + "\" --strict-mcp-config -p \"use the request_agent tool to ...\"");
        log?.Invoke("press Ctrl+C to stop.");

        await app.WaitForShutdownAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Plain-HTTP control API alongside MCP — the integration surface the TS sidecar (ACP/A2A bridges)
    /// calls. Token-protected; honours the same lineage header as MCP so the fork-bomb guard applies.
    /// </summary>
    private static void MapControlApi(WebApplication app, IStore? store, Action<string>? log)
    {
        app.MapGet("/api/health", () => Results.Json(new { ok = true, service = "armadillo" }));

        // Hooks endpoint: any Claude session wired by `armadillo connect` POSTs lifecycle events here.
        // Observe-only by default (log + audit, allow the tool). The verb is the seam where a deny
        // policy could later block tools across all your sessions.
        app.MapPost("/api/hook", async (HttpContext ctx, McpEndpoint ep) =>
        {
            if (!string.IsNullOrEmpty(ep.Token) && ctx.Request.Headers["X-Armadillo-Token"].ToString() != ep.Token)
                return Results.Unauthorized();

            string? evt = null, tool = null, detail = null;
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
                var root = doc.RootElement;
                evt = root.TryGetProperty("hook_event_name", out var e) ? e.GetString() : null;
                tool = root.TryGetProperty("tool_name", out var t) ? t.GetString() : null;
                if (root.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object)
                    detail = ti.GetRawText();
            }
            catch { /* tolerate */ }

            log?.Invoke($"[hook] {evt}{(tool is not null ? " " + tool : "")}");
            store?.Audit("hook", evt ?? "event", tool ?? "", detail);
            return Results.Json(new { }); // allow / defer
        });

        app.MapPost("/api/request_agent", async (HttpContext ctx, AgentDispatcher dispatcher, McpEndpoint ep) =>
        {
            if (!string.IsNullOrEmpty(ep.Token) &&
                ctx.Request.Headers["X-Armadillo-Token"].ToString() != ep.Token)
                return Results.Unauthorized();

            var body = await ctx.Request.ReadFromJsonAsync<RequestAgentBody>(ctx.RequestAborted);
            if (body is null || string.IsNullOrWhiteSpace(body.Task))
                return Results.BadRequest(new { error = "task is required" });

            var req = new SpawnRequest
            {
                Persona = string.IsNullOrWhiteSpace(body.Persona)
                    ? "You are a focused helper agent. Complete the task directly and concisely."
                    : body.Persona!,
                Task = body.Task!,
                Tool = ParseTool(body.Tool),
                Lineage = ChildLineage(ctx.Request.Headers["X-Armadillo-Lineage"].ToString()),
            };

            var n = Math.Clamp(body.Count ?? 1, 1, 8);
            var outcomes = n == 1
                ? new[] { await dispatcher.DispatchAsync(req, ctx.RequestAborted) }
                : (await dispatcher.DispatchManyAsync(req, n, ct: ctx.RequestAborted)).ToArray();

            return Results.Json(new
            {
                results = outcomes.Select(o => new
                {
                    jobId = o.JobId, ok = o.Ok, reason = o.Reason, finalText = o.FinalText,
                    verdict = o.Verdict.ToString().ToLowerInvariant(), confidence = o.Confidence,
                    inputTokens = o.InputTokens, outputTokens = o.OutputTokens,
                }),
            });
        });
    }

    private static ToolId ParseTool(string? tool)
        => tool is not null && Enum.TryParse<ToolId>(tool, ignoreCase: true, out var t) ? t : ToolId.Claude;

    private static SpawnLineage ChildLineage(string lineageHeader)
    {
        if (!string.IsNullOrEmpty(lineageHeader))
        {
            var idx = lineageHeader.LastIndexOf(':');
            if (idx > 0 && int.TryParse(lineageHeader[(idx + 1)..], out var depth))
                return new SpawnLineage(lineageHeader[..idx], depth + 1);
        }
        return SpawnLineage.NewRoot();
    }

    private sealed record RequestAgentBody(string? Task, string? Persona, string? Tool, int? Count);

    private static void WriteOperatorConfig(string path, string url, string token)
    {
        // No lineage header here: an operator/editor session is "external" -> its first
        // request_agent starts a new root at depth 0.
        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["armadillo"] = new
                {
                    type = "http",
                    url,
                    headers = new Dictionary<string, string> { ["X-Armadillo-Token"] = token },
                },
            },
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
}
