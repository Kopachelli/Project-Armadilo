using System.Diagnostics;
using System.Text;

namespace Armadillo.Core.Spawning;

/// <summary>
/// Windows-first child-process runner. Pipes the task via stdin, streams stdout line-by-line
/// (NDJSON-friendly), caps stdout/stderr to bound memory, and on timeout/cancel kills the entire
/// process tree. Ported from the TS orchestrator's spawner hardening.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<RunResult> RunAsync(RunSpec spec, Action<string>? onStdoutLine = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var proc = new Process { StartInfo = BuildStartInfo(spec) };

        if (!proc.Start())
            return new RunResult { Stdout = "", Stderr = "failed to start process", Duration = sw.Elapsed };

        // Feed the task via stdin, then close so the child sees EOF.
        if (spec.StdinText is not null)
        {
            try
            {
                await proc.StandardInput.WriteAsync(spec.StdinText.AsMemory(), ct).ConfigureAwait(false);
            }
            catch { /* child may have exited already */ }
        }
        try { proc.StandardInput.Close(); } catch { /* ignore */ }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var truncated = new bool[1];

        var stdoutTask = PumpAsync(proc.StandardOutput, stdout, spec.StdoutCapBytes, truncated, onStdoutLine);
        var stderrTask = PumpAsync(proc.StandardError, stderr, spec.StderrCapBytes, new bool[1], null);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(spec.Timeout);

        bool timedOut = false, killed = false;
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            killed = true;
            timedOut = !ct.IsCancellationRequested; // distinguish timeout from caller-cancel
            KillTree(proc);
            try { await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }

        // Drain the readers (process has exited or been killed).
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        sw.Stop();

        int? exit = null;
        try { exit = proc.ExitCode; } catch { /* killed before exit code available */ }

        return new RunResult
        {
            ExitCode = exit,
            TimedOut = timedOut,
            Killed = killed,
            StdoutTruncated = truncated[0],
            Stdout = stdout.ToString(),
            Stderr = stderr.ToString(),
            Duration = sw.Elapsed,
        };
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder sink, long cap, bool[] truncated,
        Action<string>? onLine)
    {
        long bytes = 0;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (!truncated[0])
            {
                bytes += Encoding.UTF8.GetByteCount(line) + 1;
                if (bytes > cap)
                {
                    truncated[0] = true;
                    sink.Append("\n[...output truncated: cap exceeded...]");
                }
                else
                {
                    sink.Append(line).Append('\n');
                }
            }
            // Always invoke the live callback even past the cap (it can apply its own bounds).
            if (onLine is not null)
            {
                try { onLine(line); } catch { /* a bad consumer must not break the pump */ }
            }
        }
    }

    private static void KillTree(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }

    /// <summary>.exe runs directly; .cmd/.bat through cmd.exe; .ps1 through PowerShell.</summary>
    private static ProcessStartInfo BuildStartInfo(RunSpec spec)
    {
        var ext = Path.GetExtension(spec.FilePath).ToLowerInvariant();
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = spec.WorkingDirectory ?? Environment.CurrentDirectory,
        };

        if (ext is ".cmd" or ".bat")
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(spec.FilePath);
        }
        else if (ext == ".ps1")
        {
            psi.FileName = "powershell.exe";
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(spec.FilePath);
        }
        else
        {
            psi.FileName = spec.FilePath;
        }

        foreach (var arg in spec.Arguments) psi.ArgumentList.Add(arg);
        foreach (var (k, v) in spec.Environment) psi.Environment[k] = v;
        return psi;
    }
}
