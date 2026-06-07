using Armadillo.Core;
using Armadillo.Core.Brain;
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
    case "assets":
        return await AssetsAsync(args);
    case "chain":
        return await ChainAsync(args);
    case "improve":
        return await ImproveAsync(args);
    case "playbooks":
        return Playbooks(args);
    case "kill-switch":
        return KillSwitch(args);
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

    var drivable = snapshot.Tools.Count(t => t.Drivable);
    Console.WriteLine($"Detected {snapshot.Installed.Count()}/{snapshot.Tools.Count} tools  "
                      + $"({drivable} drivable headlessly)  (at {snapshot.TakenAt:u})");
    Console.WriteLine();

    const string fmt = "  {0,-2} {1,-26} {2,-12} {3}";
    Console.WriteLine(string.Format(fmt, "", "TOOL", "VERSION", "CAPABILITIES"));
    Console.WriteLine("  " + new string('-', 76));
    foreach (var t in snapshot.Tools)
    {
        var mark = t.Installed ? "ok" : "--";
        var version = Truncate(t.Version ?? (t.Installed ? "?" : "absent"), 12);
        var caps = !t.Installed ? ""
            : t.Kind == ToolKind.Desktop ? "desktop GUI (not drivable)"
            : t.Kind == ToolKind.Runtime ? "runtime " + RenderCaps(t.Capabilities)
            : RenderCaps(t.Capabilities);
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

    int timeoutSec = int.TryParse(opts.Get("timeout"), out var ts) ? ts : 600;
    int count = int.TryParse(opts.Get("count"), out var c) ? c : 1;

    using var reg = Registrator.Create(new RegistratorOptions
    {
        ReviewModel = opts.Get("review-model"),
        EmbedModel = opts.Get("embed-model"),
    }, log: line => Console.Error.WriteLine(line));

    var req = new SpawnRequest
    {
        Persona = opts.Get("persona") ?? "You are a focused helper agent. Complete the task directly.",
        Task = task,
        Tool = tool,
        Timeout = TimeSpan.FromSeconds(timeoutSec),
    };

    var outcomes = count > 1
        ? await reg.Dispatcher.DispatchManyAsync(req, count)
        : new[] { await reg.Dispatcher.DispatchAsync(req) };

    foreach (var outcome in outcomes)
    {
        Console.WriteLine();
        Console.WriteLine($"job:      {outcome.JobId}");
        Console.WriteLine($"session:  {outcome.SessionId}");
        Console.WriteLine($"status:   {(outcome.Ok ? "ok" : "not-ok")} ({outcome.Reason})");
        Console.WriteLine($"review:   {outcome.Verdict} ({outcome.Confidence:0.00})");
        Console.WriteLine($"tokens:   in={outcome.InputTokens} out={outcome.OutputTokens}");
        Console.WriteLine("--- result ---");
        Console.WriteLine(outcome.FinalText);
    }
    return outcomes.All(o => o.Ok) ? 0 : 1;
}

static async Task<int> ChainAsync(string[] args)
{
    var opts = ParseFlags(args);
    using var reg = Registrator.Create(new RegistratorOptions
    {
        ReviewModel = opts.Get("review-model"),
        EmbedModel = opts.Get("embed-model"),
    }, log: line => Console.Error.WriteLine(line));

    string goal;
    string? repo = opts.Get("repo");
    List<ChainStep> steps;

    var specPath = opts.Get("spec");
    if (specPath is not null)
    {
        var spec = System.Text.Json.JsonSerializer.Deserialize<ChainSpec>(
            await File.ReadAllTextAsync(specPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (spec?.Steps is null || spec.Steps.Count == 0) { Console.Error.WriteLine("spec has no steps"); return 2; }
        goal = spec.Goal ?? opts.Positional ?? "";
        repo ??= spec.Repo;
        steps = spec.Steps.Select(s => new ChainStep(
            s.Name ?? "step",
            Enum.TryParse<ToolId>(s.Tool, ignoreCase: true, out var t) ? t : ToolId.Claude,
            s.Persona ?? "You are a focused helper agent.",
            s.Task ?? "{input}")).ToList();
    }
    else
    {
        goal = opts.Positional ?? "";
        if (string.IsNullOrWhiteSpace(goal))
        {
            Console.Error.WriteLine("usage: armadillo chain \"<goal>\" [--repo PATH] [--review-model M]   OR   chain --spec file.json");
            return 2;
        }
        // Built-in demo: implement -> review, cross-step output threading.
        steps = new List<ChainStep>
        {
            new("implement", ToolId.Claude, "You are a senior engineer. Be concise and correct.", "{goal}"),
            new("review", ToolId.Claude, "You are a critical code/output reviewer.",
                "Review the following work for correctness and quality. End with 'Verdict: APPROVE' or 'Verdict: REVISE'.\n\n{input}"),
        };
    }

    Console.WriteLine($"Running chain ({steps.Count} steps){(repo is not null ? $" on repo {repo}" : "")}…");
    var result = await reg.Chains.RunAsync(steps, goal, repoPath: repo);

    foreach (var s in result.Steps)
    {
        Console.WriteLine();
        Console.WriteLine($"== step '{s.Name}' [{s.Tool}] {(s.Ok ? "ok" : "FAILED")} (job {s.JobId}) ==");
        Console.WriteLine(s.Output);
    }
    Console.WriteLine();
    Console.WriteLine(result.Ok ? "chain completed." : "chain stopped on a failed step.");
    return result.Ok ? 0 : 1;
}

static async Task<int> AssetsAsync(string[] args)
{
    var opts = ParseFlags(args);
    var filter = opts.Positional;

    var paths = new PathProvider();
    var detector = new ToolDetector(home: paths.HomeDirectory);
    var snapshot = await detector.DetectAsync();
    var installed = snapshot.Tools.Where(t => t.Installed).Select(t => t.Id).ToHashSet();
    var scanner = new AssetScanner(paths.HomeDirectory);

    Console.WriteLine("Armadillo assets — skills, MCP servers, and configs per tool variant");
    Console.WriteLine();

    int shown = 0;
    foreach (var d in ToolDescriptor.All)
    {
        if (filter is not null && !d.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            && !d.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
            continue;

        var assets = scanner.Scan(d.Id);
        var isInstalled = installed.Contains(d.Id);
        if (!isInstalled && assets.IsEmpty) continue;

        shown++;
        var tag = isInstalled ? (d.Kind == ToolKind.Desktop ? "[desktop]" : "[installed]") : "[configs only]";
        Console.WriteLine($"{d.DisplayName} {tag}");
        if (assets.Skills.Count > 0)
            Console.WriteLine($"  skills ({assets.Skills.Count}): {string.Join(", ", assets.Skills.Take(20))}{(assets.Skills.Count > 20 ? " …" : "")}");
        if (assets.McpServers.Count > 0)
            Console.WriteLine($"  mcp servers ({assets.McpServers.Count}): {string.Join(", ", assets.McpServers)}");
        if (assets.ConfigFiles.Count > 0)
            foreach (var c in assets.ConfigFiles) Console.WriteLine($"  config: {c}");
        if (assets.IsEmpty) Console.WriteLine("  (no skills/mcp/configs found)");
        Console.WriteLine();
    }

    if (shown == 0) Console.WriteLine("(nothing to show)");
    return 0;
}

static async Task<int> ImproveAsync(string[] args)
{
    var opts = ParseFlags(args);
    var name = opts.Positional ?? "default-helper";
    var seed = opts.Get("seed") ?? "You are a focused helper agent. Complete the task directly and concisely.";
    var tasks = (opts.Get("tasks") ?? "").Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (tasks.Length == 0)
    {
        Console.Error.WriteLine("usage: armadillo improve <playbook> --tasks \"task1||task2\" [--seed \"...\"] [--improve-model M] [--review-model M]");
        return 2;
    }

    using var reg = Registrator.Create(new RegistratorOptions
    {
        ReviewModel = opts.Get("review-model"),
        ImproveModel = opts.Get("improve-model") ?? opts.Get("review-model"),
        EmbedModel = opts.Get("embed-model"),
    }, log: line => Console.Error.WriteLine(line));

    Console.WriteLine($"Running self-improvement cycle for '{name}' over {tasks.Length} eval task(s)…");
    var decision = await reg.Improvement.RunCycleAsync(name, seed, tasks);

    Console.WriteLine();
    Console.WriteLine($"outcome:        {decision.Outcome}");
    Console.WriteLine($"active version: {decision.ActiveVersion}");
    Console.WriteLine($"detail:         {decision.Detail}");
    return 0;
}

static int Playbooks(string[] args)
{
    var opts = ParseFlags(args);
    using var reg = Registrator.Create();
    var name = opts.Positional;
    if (name is null)
    {
        Console.Error.WriteLine("usage: armadillo playbooks <name>");
        return 2;
    }
    var versions = reg.Store.GetPlaybookVersions(name);
    if (versions.Count == 0) { Console.WriteLine($"no playbook versions for '{name}'."); return 0; }
    foreach (var v in versions)
    {
        var mark = v.Active ? "* " : "  ";
        Console.WriteLine($"{mark}v{v.Version}  score={v.Score:0.00}  {v.SourceNote}  ({v.CreatedAt:u})");
    }
    return 0;
}

static int KillSwitch(string[] args)
{
    var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
    using var reg = Registrator.Create();
    switch (sub)
    {
        case "on": reg.Improvement.SetKillSwitch(true); Console.WriteLine("kill switch ENGAGED — self-modification halted."); break;
        case "off": reg.Improvement.SetKillSwitch(false); Console.WriteLine("kill switch released — self-modification allowed."); break;
        default: Console.WriteLine($"kill switch: {(reg.Improvement.KillSwitchEngaged ? "ENGAGED" : "off")}"); break;
    }
    return 0;
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
          doctor        Detect installed AI CLI tools + local model runtimes (capability matrix)
          serve         Run the Registrator daemon + MCP server
          run           Spawn headless session(s): run "<task>" [--tool T] [--count N] [--review-model M]
          assets        Show skills + MCP servers + configs per tool variant: assets [filter]
          chain         Run a cross-tool pipeline: chain "<goal>" [--repo PATH] | chain --spec file.json
          improve       Self-improvement cycle: improve <playbook> --tasks "a||b" [--review-model M]
          playbooks     List versions of a playbook: playbooks <name>
          kill-switch   Halt/allow self-modification: kill-switch <on|off|status>
          help          Show this help
        """);
}

internal sealed record FlagSet(Dictionary<string, string> Flags, string? Positional)
{
    public string? Get(string key) => Flags.TryGetValue(key, out var v) ? v : null;
}

internal sealed record ChainSpec(string? Goal, string? Repo, List<ChainStepSpec>? Steps);
internal sealed record ChainStepSpec(string? Name, string? Tool, string? Persona, string? Task);
