namespace Armadillo.Core;

/// <summary>Short, filesystem- and id-safe identifiers.</summary>
public static class Ids
{
    public static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 1 + 16)];
    public static string NewRaw() => Guid.NewGuid().ToString("N")[..16];
}
