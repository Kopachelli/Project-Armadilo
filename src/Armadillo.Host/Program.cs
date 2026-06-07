using Armadillo.Core;
using Armadillo.Core.Adapters;
using Armadillo.Core.Brain;
using Armadillo.Core.Orchestration;
using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Armadillo.Detection;
using Armadillo.Mcp;
using Armadillo.Runtime;

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
    case "supervise":
        return await SuperviseAsync(args);
    case "hook":
        return await HookAsync(args);
    case "connect":
        return await ConnectAsync(args);
    case "disconnect":
        return await DisconnectAsync(args);
    case "autostart":
        return Autostart(args);
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
    int port = int.TryParse(opts.Get("port"), out var p) ? p : 8787; // stable default so registered configs don't go stale

    using var reg = Registrator.Create(new RegistratorOptions
    {
        ReviewModel = opts.Get("review-model"),
        EmbedModel = opts.Get("embed-model"),
    }, log: line => Console.Error.WriteLine(line));

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var token = GetOrCreateDaemonToken(reg.Paths);
    var operatorConfig = Path.Combine(reg.Paths.ConfigDir, "operator.mcp.json");
    Console.WriteLine("Armadillo Registrator — starting MCP daemon…");

    try
    {
        await McpDaemon.RunAsync(reg.Dispatcher, reg.McpEndpoint, operatorConfig, port,
            token, reg.Store, log: Console.WriteLine, ct: cts.Token);
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

    ToolId? tool = null;
    var toolName = opts.Get("tool");
    if (toolName is not null)
    {
        if (!Enum.TryParse<ToolId>(toolName, ignoreCase: true, out var t))
        {
            Console.Error.WriteLine($"unknown tool '{toolName}'.");
            return 2;
        }
        tool = t;
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

static async Task<int> SuperviseAsync(string[] args)
{
    var opts = ParseFlags(args);
    var task = opts.Positional;
    if (string.IsNullOrWhiteSpace(task))
    {
        Console.Error.WriteLine("usage: armadillo supervise \"<task>\" [--deny Tool1,Tool2]");
        return 2;
    }
    var deny = (opts.Get("deny") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var paths = new PathProvider(); paths.EnsureWorkspace();
    var detected = await new ToolDetector(home: paths.HomeDirectory).DetectAsync(ToolId.Claude);
    if (!detected.Installed || detected.ExecutablePath is null)
    {
        Console.Error.WriteLine("Claude Code is not installed (supervision is Claude-first — it has hooks).");
        return 1;
    }

    await using var host = await SupervisorHost.StartAsync(deny, RenderHook);
    Console.WriteLine($"Supervisor live on {host.Url}  (deny: {(deny.Count == 0 ? "(none — watch only)" : string.Join(", ", deny))})");

    // Hooks settings: every PreToolUse/PostToolUse/Stop event POSTs to us via `armadillo hook`.
    var hookCmd = BuildHookCommand();
    var settings = new
    {
        hooks = new Dictionary<string, object[]>
        {
            ["PreToolUse"] = new object[] { new { matcher = "*", hooks = new[] { new { type = "command", command = hookCmd, timeout = 20 } } } },
            ["PostToolUse"] = new object[] { new { matcher = "*", hooks = new[] { new { type = "command", command = hookCmd, timeout = 20 } } } },
            ["Stop"] = new object[] { new { hooks = new[] { new { type = "command", command = hookCmd, timeout = 20 } } } },
        },
    };
    var sessionDir = paths.SessionDir(Ids.New("sup"));
    Directory.CreateDirectory(sessionDir);
    var settingsPath = Path.Combine(sessionDir, "hooks.settings.json");
    await File.WriteAllTextAsync(settingsPath, System.Text.Json.JsonSerializer.Serialize(settings));

    var spec = new RunSpec
    {
        FilePath = detected.ExecutablePath,
        Arguments = new[] { "-p", "--output-format", "stream-json", "--verbose",
                            "--settings", settingsPath, "--permission-mode", "acceptEdits" },
        StdinText = task,
        WorkingDirectory = sessionDir,
        Environment = new Dictionary<string, string>
        {
            ["ARMADILLO_HOOK_URL"] = host.Url,
            ["ARMADILLO_HOOK_TOKEN"] = host.Token,
        },
        Timeout = TimeSpan.FromMinutes(5),
    };

    Console.WriteLine("--- live session (hook events stream below) ---");
    var result = await new ProcessRunner().RunAsync(spec);
    var parsed = new ClaudeAdapter().Parse(result);

    Console.WriteLine();
    Console.WriteLine("--- session result ---");
    Console.WriteLine(parsed.FinalText);
    return 0;

    static void RenderHook(HookEventInfo e)
    {
        if (e.Blocked) Console.WriteLine($"  [BLOCKED] {e.Event} {e.Tool} — {e.Reason}");
        else Console.WriteLine($"  [hook] {e.Event}{(e.Tool is not null ? " " + e.Tool : "")}{(e.Detail is not null ? ": " + e.Detail : "")}");
    }
}

// The hook handler Claude invokes per event: forward stdin event to the supervisor, return its decision.
static async Task<int> HookAsync(string[] args)
{
    var opts = ParseFlags(args);
    var url = opts.Get("url") ?? Environment.GetEnvironmentVariable("ARMADILLO_HOOK_URL");
    var token = opts.Get("token") ?? Environment.GetEnvironmentVariable("ARMADILLO_HOOK_TOKEN");
    var payload = await Console.In.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(url)) return 0; // no supervisor -> allow

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var req = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/api/hook")
        { Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json") };
        if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Armadillo-Token", token);
        var resp = await http.SendAsync(req);
        Console.Out.Write(await resp.Content.ReadAsStringAsync());
    }
    catch { /* supervisor unreachable -> allow (print nothing) */ }
    return 0;
}

static string BuildHookCommand()
{
    // Single-file: ProcessPath is armadillo.exe itself -> use it directly.
    // `dotnet run` dev: ProcessPath is dotnet.exe -> we need the managed dll path.
    var exe = Environment.ProcessPath ?? "dotnet";
    if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
    {
#pragma warning disable IL3000 // only reached under `dotnet run` (not single-file), where Location is valid
        var dll = System.Reflection.Assembly.GetEntryAssembly()?.Location;
#pragma warning restore IL3000
        if (!string.IsNullOrEmpty(dll)) return $"\"{exe}\" \"{dll}\" hook";
    }
    return $"\"{exe}\" hook";
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
        if (assets.Plugins.Count > 0)
            Console.WriteLine($"  plugins ({assets.Plugins.Count}): {string.Join(", ", assets.Plugins.Take(20))}{(assets.Plugins.Count > 20 ? " …" : "")}");
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

static async Task<int> ConnectAsync(string[] args)
{
    var opts = ParseFlags(args);
    int port = int.TryParse(opts.Get("port"), out var p) ? p : 8787;
    var url = $"http://127.0.0.1:{port}";
    var paths = new PathProvider(); paths.EnsureWorkspace();
    var token = GetOrCreateDaemonToken(paths);

    // Hook command: absolute path to this armadillo exe when we are it, else assume `armadillo` on PATH.
    var exe = Environment.ProcessPath;
    var hookExe = exe is not null && Path.GetFileNameWithoutExtension(exe).Equals("armadillo", StringComparison.OrdinalIgnoreCase)
        ? exe : "armadillo";
    var hookCmd = $"\"{hookExe}\" hook --url {url} --token {token}";

    Console.WriteLine($"Connecting Claude Code to Armadillo ({url}) …");

    var detected = await new ToolDetector(home: paths.HomeDirectory).DetectAsync(ToolId.Claude);
    if (detected.ExecutablePath is null)
    {
        Console.Error.WriteLine("claude CLI not found — install Claude Code first.");
        return 1;
    }
    var claude = detected.ExecutablePath;

    // 1) MCP server (user scope).
    var mcpJson = System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "http", url,
        headers = new Dictionary<string, string> { ["X-Armadillo-Token"] = token },
    });
    await RunClaude(claude, new[] { "mcp", "remove", "armadillo", "-s", "user" }); // idempotent
    var mcpOk = await RunClaude(claude, new[] { "mcp", "add-json", "armadillo", mcpJson, "-s", "user" });
    if (!mcpOk)
        mcpOk = await RunClaude(claude, new[] { "mcp", "add", "--transport", "http", "armadillo", url,
            "--header", $"X-Armadillo-Token: {token}", "-s", "user" });
    Console.WriteLine(mcpOk
        ? "  MCP: registered 'armadillo' (user scope)."
        : "  MCP: auto-register failed; run: claude mcp add-json armadillo '" + mcpJson + "' -s user");

    // 2) Hooks merged into ~/.claude/settings.json (existing hooks preserved; backup written).
    var settingsPath = Path.Combine(paths.HomeDirectory, ".claude", "settings.json");
    InstallHooks(settingsPath, hookCmd);
    Console.WriteLine("  Hooks: PreToolUse/PostToolUse/Stop installed into ~/.claude/settings.json (.bak backup).");

    Console.WriteLine();
    Console.WriteLine("Connected. Run `armadillo serve` and keep it up. Then EVERY Claude Code session you open can");
    Console.WriteLine("call the request_agent tool (MCP) and is observed by Armadillo (hooks). Undo: `armadillo disconnect`.");
    return 0;
}

static async Task<int> DisconnectAsync(string[] args)
{
    var paths = new PathProvider();
    Console.WriteLine("Disconnecting Claude Code from Armadillo …");
    var detected = await new ToolDetector(home: paths.HomeDirectory).DetectAsync(ToolId.Claude);
    if (detected.ExecutablePath is not null)
        await RunClaude(detected.ExecutablePath, new[] { "mcp", "remove", "armadillo", "-s", "user" });
    Console.WriteLine("  MCP: removed 'armadillo' (user scope).");
    RemoveHooks(Path.Combine(paths.HomeDirectory, ".claude", "settings.json"));
    Console.WriteLine("  Hooks: removed Armadillo entries from ~/.claude/settings.json.");
    return 0;
}

// Admin-free autostart: a hidden VBS launcher in the user's Startup folder runs the daemon at login
// (no scheduled-task elevation, no console window). on = write it, off = delete it, status = check.
static int Autostart(string[] args)
{
    var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
    var exe = Environment.ProcessPath ?? "armadillo";
    var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    var vbs = Path.Combine(startup, "Armadillo.vbs");

    switch (sub)
    {
        case "on":
            if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            { Console.Error.WriteLine("Run `autostart on` from the installed armadillo.exe, not via dotnet run."); return 2; }
            // VBS: WScript.Shell.Run "<exe> serve", 0 (hidden), False (don't wait).
            var script = "CreateObject(\"WScript.Shell\").Run \"\"\"" + exe + "\"\" serve\", 0, False";
            Directory.CreateDirectory(startup);
            File.WriteAllText(vbs, script);
            Console.WriteLine($"Autostart ON — the daemon launches hidden at every login.\n  Startup launcher: {vbs}");
            Console.WriteLine("Start it now without logging out:  armadillo serve   (or run the .vbs).");
            return 0;
        case "off":
            if (File.Exists(vbs)) File.Delete(vbs);
            Console.WriteLine("Autostart OFF.");
            return 0;
        default:
            Console.WriteLine(File.Exists(vbs) ? $"Autostart: ON  ({vbs})" : "Autostart: off.");
            return 0;
    }
}

static string GetOrCreateDaemonToken(IPathProvider paths)
{
    var file = Path.Combine(paths.ConfigDir, "daemon.token");
    if (File.Exists(file)) { var t = File.ReadAllText(file).Trim(); if (t.Length > 0) return t; }
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    Directory.CreateDirectory(paths.ConfigDir);
    File.WriteAllText(file, token);
    return token;
}

static async Task<bool> RunClaude(string claudeExe, string[] args)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = claudeExe, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi);
        if (proc is null) return false;
        await proc.WaitForExitAsync();
        return proc.ExitCode == 0;
    }
    catch { return false; }
}

static void InstallHooks(string settingsPath, string hookCmd)
{
    var root = LoadJsonObject(settingsPath);
    if (root["hooks"] is not JsonObject hooks) { hooks = new JsonObject(); root["hooks"] = hooks; }

    JsonObject Entry(bool withMatcher)
    {
        var e = new JsonObject();
        if (withMatcher) e["matcher"] = "*";
        e["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = hookCmd, ["timeout"] = 20 });
        return e;
    }
    void Add(string evt, bool withMatcher)
    {
        if (hooks[evt] is not JsonArray arr) { arr = new JsonArray(); hooks[evt] = arr; }
        if (arr.Any(i => i is JsonObject o && EntryIsOurs(o))) return; // idempotent
        arr.Add(Entry(withMatcher));
    }
    Add("PreToolUse", true);
    Add("PostToolUse", true);
    Add("Stop", false);
    SaveJsonObject(settingsPath, root);
}

static void RemoveHooks(string settingsPath)
{
    if (!File.Exists(settingsPath)) return;
    var root = LoadJsonObject(settingsPath);
    if (root["hooks"] is not JsonObject hooks) return;
    foreach (var evt in hooks.Select(kv => kv.Key).ToList())
    {
        if (hooks[evt] is not JsonArray arr) continue;
        var kept = new JsonArray();
        foreach (var item in arr)
            if (!(item is JsonObject o && EntryIsOurs(o))) kept.Add(item?.DeepClone());
        hooks[evt] = kept;
    }
    SaveJsonObject(settingsPath, root);
}

static bool EntryIsOurs(JsonObject entry)
{
    if (entry["hooks"] is not JsonArray hs) return false;
    return hs.Any(h =>
    {
        var cmd = (h as JsonObject)?["command"]?.GetValue<string>() ?? "";
        return cmd.Contains("armadillo", StringComparison.OrdinalIgnoreCase) && cmd.Contains(" hook", StringComparison.OrdinalIgnoreCase);
    });
}

static JsonObject LoadJsonObject(string path)
{
    try { if (File.Exists(path)) return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
    catch { }
    return new JsonObject();
}

static void SaveJsonObject(string path, JsonObject root)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
    File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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
          serve         Run the Registrator daemon + MCP/control/hook server (default 127.0.0.1:8787)
          connect       Wire your Claude Code into Armadillo (user-scope MCP + global hooks)
          disconnect    Remove the Armadillo MCP server + hooks from your Claude config
          autostart     Run the daemon at login: autostart <on|off|status>
          run           Spawn headless session(s): run "<task>" [--tool T] [--count N] [--review-model M]
          assets        Show skills + MCP servers + configs per tool variant: assets [filter]
          chain         Run a cross-tool pipeline: chain "<goal>" [--repo PATH] | chain --spec file.json
          supervise     Run + live-supervise a Claude session via hooks: supervise "<task>" [--deny Tool1,Tool2]
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
