using Armadillo.Core;
using Armadillo.Core.Orchestration;
using Armadillo.Core.Tools;
using Armadillo.Detection;
using Armadillo.Host;
using Armadillo.Mcp;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

switch (command)
{
    case "doctor":
        return await DoctorAsync();
    case "serve":
        return await ServeAsync(args);
    case "run":
        return await RunAsync(args);
    default:
        PrintHelp();
        return command is "help" or "-h" or "--help" ? 0 : 1;
}

static async Task<int> DoctorAsync()
{
    var paths = new PathProvider();
    paths.EnsureWorkspace();

    Console.WriteLine("Armadillo doctor");
    Console.WriteLine($"  workspace: {paths.Workspace}");
    Console.WriteLine($"  home:      {paths.HomeDirectory}");
    Console.WriteLine();

    var detector = new ToolDetector(home: paths.HomeDirectory);
    var snapshot = await detector.DetectAsync();

    Console.WriteLine($"Detected {snapshot.Installed.Count()}/{snapshot.Tools.Count} tools  "
                      + $"(at {snapshot.TakenAt:u})");
    Console.WriteLine();

    const string fmt = "  {0,-2} {1,-20} {2,-12} {3}";
    Console.WriteLine(string.Format(fmt, "", "TOOL", "VERSION", "CAPABILITIES"));
    Console.WriteLine("  " + new string('-', 72));
    foreach (var t in snapshot.Tools)
    {
        var mark = t.Installed ? "ok" : "--";
        var version = Truncate(t.Version ?? (t.Installed ? "?" : "absent"), 12);
        var caps = t.Installed ? RenderCaps(t.Capabilities) : "";
        Console.WriteLine(string.Format(fmt, mark, t.DisplayName, version, caps));
        if (t.Id == ToolId.Ollama && t.Models.Count > 0)
            Console.WriteLine($"        models: {string.Join(", ", t.Models)}");
    }

    Console.WriteLine();
    var ollama = snapshot.Tools.FirstOrDefault(t => t.Id == ToolId.Ollama);
    if (ollama is { Installed: true, Models.Count: > 0 })
        Console.WriteLine($"Local review/embeddings available via Ollama ({ollama.Models.Count} model(s)).");
    else
        Console.WriteLine("No local Ollama models found — pull one (e.g. `ollama pull qwen2.5-coder`) for local review.");

    return 0;
}

static string RenderCaps(ToolCapabilities c)
{
    var parts = new List<string>();
    if (c.HasFlag(ToolCapabilities.Headless)) parts.Add("headless");
    if (c.HasFlag(ToolCapabilities.StreamJson)) parts.Add("json");
    if (c.HasFlag(ToolCapabilities.Hooks)) parts.Add("hooks");
    if (c.HasFlag(ToolCapabilities.Resume)) parts.Add("resume");
    if (c.HasFlag(ToolCapabilities.McpClient)) parts.Add("mcp");
    if (c.HasFlag(ToolCapabilities.HttpDaemon)) parts.Add("daemon");
    if (c.HasFlag(ToolCapabilities.LocalProvider)) parts.Add("local");
    if (c.HasFlag(ToolCapabilities.Acp)) parts.Add("acp");
    return string.Join(" ", parts);
}

static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

static async Task<int> ServeAsync(string[] args)
{
    var opts = ParseFlags(args);
    int port = int.TryParse(opts.Get("port"), out var p) ? p : 0;

    using var reg = Registrator.Create(new RegistratorOptions
    {
        ReviewModel = opts.Get("review-model"),
        EmbedModel = opts.Get("embed-model"),
    }, log: line => Console.Error.WriteLine(line));

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var operatorConfig = Path.Combine(reg.Paths.ConfigDir, "operator.mcp.json");
    Console.WriteLine("Armadillo Registrator — starting MCP daemon…");

    try
    {
        await McpDaemon.RunAsync(reg.Dispatcher, reg.McpEndpoint, operatorConfig, port,
            log: Console.WriteLine, ct: cts.Token);
    }
    catch (OperationCanceledException) { /* clean shutdown */ }

    Console.WriteLine("Registrator stopped.");
    return 0;
}

static async Task<int> RunAsync(string[] args)
{
    var opts = ParseFlags(args);
    var task = opts.Positional;
    if (string.IsNullOrWhiteSpace(task))
    {
        Console.Error.WriteLine("usage: armadillo run \"<task>\" [--tool claude] [--persona \"...\"] [--review-model M] [--embed-model M]");
        return 2;
    }

    var toolName = opts.Get("tool") ?? "claude";
    if (!Enum.TryParse<ToolId>(toolName, ignoreCase: true, out var tool))
    {
        Console.Error.WriteLine($"unknown tool '{toolName}'. Supported in v1: claude.");
        return 2;
    }

    using var reg = Registrator.Create(new RegistratorOptions
    {
        ReviewModel = opts.Get("review-model"),
        EmbedModel = opts.Get("embed-model"),
    }, log: line => Console.Error.WriteLine(line));

    var outcome = await reg.Dispatcher.DispatchAsync(new SpawnRequest
    {
        Persona = opts.Get("persona") ?? "You are a focused helper agent. Complete the task directly.",
        Task = task,
        Tool = tool,
    });

    Console.WriteLine();
    Console.WriteLine($"job:      {outcome.JobId}");
    Console.WriteLine($"session:  {outcome.SessionId}");
    Console.WriteLine($"status:   {(outcome.Ok ? "ok" : "not-ok")} ({outcome.Reason})");
    Console.WriteLine($"review:   {outcome.Verdict} ({outcome.Confidence:0.00})");
    Console.WriteLine($"tokens:   in={outcome.InputTokens} out={outcome.OutputTokens}");
    Console.WriteLine("--- result ---");
    Console.WriteLine(outcome.FinalText);
    return outcome.Ok ? 0 : 1;
}

static FlagSet ParseFlags(string[] args)
{
    var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string? positional = null;
    for (int i = 1; i < args.Length; i++)
    {
        var a = args[i];
        if (a.StartsWith("--"))
        {
            var key = a[2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
            flags[key] = value;
        }
        else
        {
            positional ??= a;
        }
    }
    return new FlagSet(flags, positional);
}

static void PrintHelp()
{
    Console.WriteLine("""
        Armadillo — self-learning AI core + CLI-agent orchestration harness

        Usage: armadillo <command>

        Commands:
          doctor    Detect installed AI CLI tools + local model runtimes (capability matrix)
          serve     Run the Registrator daemon + MCP server   (Phase 1)
          run       Spawn and capture a single headless session (Phase 1)
          help      Show this help
        """);
}

internal sealed record FlagSet(Dictionary<string, string> Flags, string? Positional)
{
    public string? Get(string key) => Flags.TryGetValue(key, out var v) ? v : null;
}
