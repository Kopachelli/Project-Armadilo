using System.Security.Cryptography;
using System.Text.Json;
using Armadillo.Core.Orchestration;
using Microsoft.AspNetCore.Builder;
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
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

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

        await app.StartAsync(ct).ConfigureAwait(false);

        var baseUrl = app.Urls.FirstOrDefault() ?? $"http://127.0.0.1:{port}";
        endpoint.Url = baseUrl;
        endpoint.Token = token;

        WriteOperatorConfig(operatorConfigPath, baseUrl, token);

        log?.Invoke($"MCP server listening on {baseUrl}");
        log?.Invoke($"operator config written to {operatorConfigPath}");
        log?.Invoke("point a CLI at it, e.g.:  claude --mcp-config \"" + operatorConfigPath + "\" --strict-mcp-config -p \"use the request_agent tool to ...\"");
        log?.Invoke("press Ctrl+C to stop.");

        await app.WaitForShutdownAsync(ct).ConfigureAwait(false);
    }

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
