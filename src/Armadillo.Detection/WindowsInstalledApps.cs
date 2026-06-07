using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Armadillo.Detection;

public sealed record InstalledApp(string DisplayName, string? InstallLocation, string? DisplayIcon);

/// <summary>
/// Enumerates installed Windows desktop apps via the standard Uninstall registry keys (HKLM 64/32-bit
/// + HKCU). This is how desktop GUIs are detected generically by display name — no exe-path guessing,
/// and no collision with same-named CLIs. Returns empty on non-Windows (cross-platform later).
/// </summary>
public static class WindowsInstalledApps
{
    public static IReadOnlyList<InstalledApp> Scan()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<InstalledApp>();
        var list = new List<InstalledApp>();
        try
        {
            ReadHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", list);
            ReadHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", list);
            ReadHive(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", list);
        }
        catch { /* registry unavailable — best effort */ }
        return list;
    }

    /// <summary>First installed app whose DisplayName contains any of <paramref name="patterns"/>. Pure/testable.</summary>
    public static InstalledApp? Match(IEnumerable<InstalledApp> apps, IReadOnlyList<string> patterns)
    {
        foreach (var app in apps)
            foreach (var p in patterns)
                if (!string.IsNullOrEmpty(p) && app.DisplayName.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return app;
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static void ReadHive(RegistryKey root, string path, List<InstalledApp> list)
    {
        using var key = root.OpenSubKey(path);
        if (key is null) return;
        foreach (var subName in key.GetSubKeyNames())
        {
            try
            {
                using var sub = key.OpenSubKey(subName);
                if (sub?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue; // hide system components
                list.Add(new InstalledApp(name,
                    sub.GetValue("InstallLocation") as string,
                    sub.GetValue("DisplayIcon") as string));
            }
            catch { /* skip unreadable entries */ }
        }
    }
}
