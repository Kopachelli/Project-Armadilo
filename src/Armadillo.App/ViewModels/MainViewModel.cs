using System.Collections.ObjectModel;
using Armadillo.Core.Orchestration;
using Armadillo.Core.Tools;
using Armadillo.Detection;
using Armadillo.Runtime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Armadillo.App.ViewModels;

public sealed record ToolRow(string Tool, string Kind, bool Installed, string Version, string Capabilities);
public sealed record AssetRow(string Tool, int Skills, int Mcp, int Plugins, string Configs);
public sealed record JobRow(string Job, string Tool, string State, string Created, string Task);

/// <summary>Backs the dashboard: detected tools, per-tool assets, recent activity, and a run panel.
/// All work runs against the in-process <see cref="Registrator"/> (same engine as the CLI).</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly Registrator _reg;

    public ObservableCollection<ToolRow> Tools { get; } = new();
    public ObservableCollection<AssetRow> Assets { get; } = new();
    public ObservableCollection<JobRow> Jobs { get; } = new();
    public ObservableCollection<string> RunTools { get; } =
        new(new[] { "claude", "cursor", "gemini", "qwen", "codex", "ollama", "copilot", "opencode" });

    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _runTask = "";
    [ObservableProperty] private string _runTool = "claude";
    [ObservableProperty] private string _runResult = "";
    [ObservableProperty] private string? _reviewModel;

    public MainViewModel(Registrator reg) => _reg = reg;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Busy = true;
        Status = "Scanning tools + assets…";
        try
        {
            var snapshot = await _reg.Detector.DetectAsync();
            var scanner = new AssetScanner(_reg.Paths.HomeDirectory);

            Tools.Clear();
            Assets.Clear();
            foreach (var t in snapshot.Tools)
            {
                Tools.Add(new ToolRow(t.DisplayName, t.Kind.ToString(), t.Installed,
                    t.Version ?? (t.Installed ? "?" : "absent"), RenderCaps(t)));

                var a = scanner.Scan(t.Id);
                if (t.Installed || !a.IsEmpty)
                    Assets.Add(new AssetRow(t.DisplayName, a.Skills.Count, a.McpServers.Count,
                        a.Plugins.Count, string.Join(" | ", a.ConfigFiles)));
            }

            Jobs.Clear();
            foreach (var j in _reg.Store.RecentJobs(100))
                Jobs.Add(new JobRow(j.JobId, j.Tool.ToString(), j.State.ToString(),
                    j.CreatedAt.LocalDateTime.ToString("g"), Trunc(j.Task, 80)));

            var drivable = snapshot.Tools.Count(t => t.Drivable);
            Status = $"{snapshot.Installed.Count()} tools installed, {drivable} drivable. {Jobs.Count} recent jobs.";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(RunTask)) { Status = "Enter a task first."; return; }
        Busy = true;
        Status = $"Running on {RunTool}…";
        RunResult = "";
        try
        {
            var tool = Enum.TryParse<ToolId>(RunTool, ignoreCase: true, out var t) ? t : ToolId.Claude;
            var outcome = await _reg.Dispatcher.DispatchAsync(new SpawnRequest
            {
                Persona = "You are a focused helper agent. Complete the task directly and concisely.",
                Task = RunTask,
                Tool = tool,
            });
            RunResult = outcome.FinalText;
            Status = $"Done: {(outcome.Ok ? "ok" : outcome.Reason)} — review {outcome.Verdict} ({outcome.Confidence:0.00}), " +
                     $"tokens in={outcome.InputTokens} out={outcome.OutputTokens}";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { Busy = false; }
    }

    private static string RenderCaps(DetectedTool t)
    {
        if (!t.Installed) return "";
        if (t.Kind == ToolKind.Desktop) return "desktop GUI (not drivable)";
        var c = t.Capabilities;
        var parts = new List<string>();
        if (c.HasFlag(ToolCapabilities.Headless)) parts.Add("headless");
        if (c.HasFlag(ToolCapabilities.StreamJson)) parts.Add("json");
        if (c.HasFlag(ToolCapabilities.Hooks)) parts.Add("hooks");
        if (c.HasFlag(ToolCapabilities.Resume)) parts.Add("resume");
        if (c.HasFlag(ToolCapabilities.McpClient)) parts.Add("mcp");
        if (c.HasFlag(ToolCapabilities.LocalProvider)) parts.Add("local");
        if (c.HasFlag(ToolCapabilities.Acp)) parts.Add("acp");
        return string.Join(" ", parts);
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
