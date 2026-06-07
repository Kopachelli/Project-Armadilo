using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Armadillo.Mcp;

/// <summary>A single observed hook event from a supervised session.</summary>
public sealed record HookEventInfo(string Event, string? Tool, string? Detail, bool Blocked, string? Reason);

/// <summary>
/// In-process loopback endpoint that a supervised Claude session's hooks POST to. It observes every
/// lifecycle event live and can INTERRUPT a tool call (PreToolUse → permissionDecision "deny"), which
/// is the sanctioned way to steer a running session. Token-protected; bound to 127.0.0.1.
/// </summary>
public sealed class SupervisorHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    public string Url { get; }
    public string Token { get; }

    private SupervisorHost(WebApplication app, string url, string token)
    {
        _app = app;
        Url = url;
        Token = token;
    }

    public static async Task<SupervisorHost> StartAsync(
        IReadOnlySet<string> denyTools, Action<HookEventInfo> onEvent, CancellationToken ct = default)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");

        app.MapPost("/api/hook", async (HttpContext ctx) =>
        {
            if (ctx.Request.Headers["X-Armadillo-Token"].ToString() != token)
                return Results.Unauthorized();

            string? evt = null, tool = null, detail = null;
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
                var root = doc.RootElement;
                evt = GetStr(root, "hook_event_name");
                tool = GetStr(root, "tool_name");
                detail = Summarize(root);
            }
            catch { /* tolerate malformed event */ }

            var block = evt == "PreToolUse" && tool is not null && denyTools.Contains(tool);
            var reason = block ? $"Blocked by Armadillo supervisor: tool '{tool}' is on the deny list." : null;

            try { onEvent(new HookEventInfo(evt ?? "?", tool, detail, block, reason)); } catch { }

            if (block)
                return Results.Json(new
                {
                    hookSpecificOutput = new
                    {
                        hookEventName = "PreToolUse",
                        permissionDecision = "deny",
                        permissionDecisionReason = reason,
                    },
                });

            return Results.Json(new { }); // allow / defer
        });

        await app.StartAsync(ct).ConfigureAwait(false);
        var url = app.Urls.FirstOrDefault() ?? "http://127.0.0.1:0";
        return new SupervisorHost(app, url, token);
    }

    private static string? GetStr(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A short human summary of a tool call (command / path / first input field).</summary>
    private static string? Summarize(JsonElement root)
    {
        if (!root.TryGetProperty("tool_input", out var ti) || ti.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in new[] { "command", "file_path", "path", "url", "pattern", "query" })
            if (ti.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return Trunc(v.GetString(), 120);
        return Trunc(ti.GetRawText(), 120);
    }

    private static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n] + "…";

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _app.StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch { }
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
