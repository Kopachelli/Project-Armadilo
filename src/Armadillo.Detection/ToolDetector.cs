using System.Diagnostics;
using Armadillo.Core.Tools;

namespace Armadillo.Detection;

/// <summary>Concrete tool/runtime probe. Implements <see cref="IToolDetector"/> from Core.</summary>
public sealed class ToolDetector : IToolDetector
{
    private readonly ExecutableResolver _resolver;
    private readonly OllamaProbe _ollama;
    private readonly string _home;

    public ToolDetector(ExecutableResolver? resolver = null, OllamaProbe? ollama = null, string? home = null)
    {
        _resolver = resolver ?? new ExecutableResolver();
        _ollama = ollama ?? new OllamaProbe();
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public async Task<CapabilitySnapshot> DetectAsync(CancellationToken ct = default)
    {
        var tools = await Task.WhenAll(ToolDescriptor.All.Select(d => DetectAsync(d, ct))).ConfigureAwait(false);
        return new CapabilitySnapshot(DateTimeOffset.UtcNow, tools);
    }

    public Task<DetectedTool> DetectAsync(ToolId id, CancellationToken ct = default)
        => DetectAsync(ToolDescriptor.For(id), ct);

    private async Task<DetectedTool> DetectAsync(ToolDescriptor d, CancellationToken ct)
    {
        try
        {
            var signals = new List<string>();

            // Secondary signal: a tool dotfolder under home.
            foreach (var folder in d.DotFolders)
            {
                var path = Path.Combine(_home, folder);
                if (Directory.Exists(path)) signals.Add($"dir:{folder}");
            }

            // Primary signal: a resolvable executable on the search path.
            var exe = _resolver.Resolve(d.ExecutableNames);
            if (exe is null)
            {
                // A dotfolder alone counts as "present but no runnable binary".
                return DetectedTool.NotFound(d) with { Signals = signals };
            }
            signals.Insert(0, $"exe:{exe}");

            var version = await ProbeVersionAsync(exe, d.VersionArgs, ct).ConfigureAwait(false);

            IReadOnlyList<string> models = Array.Empty<string>();
            if (d.Id == ToolId.Ollama)
                models = await _ollama.ListModelsAsync(ct).ConfigureAwait(false);

            return new DetectedTool(
                d.Id, d.DisplayName, Installed: true, ExecutablePath: exe, Version: version,
                Capabilities: d.Capabilities, Family: d.Family,
                Signals: signals, Models: models, Notes: d.Notes);
        }
        catch
        {
            return DetectedTool.NotFound(d);
        }
    }

    /// <summary>Runs <c>&lt;exe&gt; --version</c> with a short timeout; returns the first non-empty line or null.</summary>
    private static async Task<string?> ProbeVersionAsync(string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            var psi = BuildStartInfo(exe, args);
            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            try
            {
                await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                return null;
            }

            var output = (await stdoutTask.ConfigureAwait(false)).Trim();
            if (string.IsNullOrEmpty(output)) output = (await stderrTask.ConfigureAwait(false)).Trim();
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>.exe runs directly; .cmd/.bat go through cmd.exe; .ps1 through PowerShell.</summary>
    private static ProcessStartInfo BuildStartInfo(string exe, IReadOnlyList<string> args)
    {
        var ext = Path.GetExtension(exe).ToLowerInvariant();
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (ext is ".cmd" or ".bat")
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(exe);
        }
        else if (ext == ".ps1")
        {
            psi.FileName = "powershell.exe";
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(exe);
        }
        else
        {
            psi.FileName = exe;
        }

        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}
