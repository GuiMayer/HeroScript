using System.Security.Cryptography;
using Core.Determinism;

namespace Mods;

/// <summary>
/// A strong HTTP validator for an immutable workspace source snapshot.
/// Use workspace-relative paths, with a forward slash as directory separator.
/// </summary>
public static class AuthoringSourceVersion
{
    public static string CreateETag(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var entries = files.OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => new SourceEntry(file.Key, Convert.ToHexString(SHA256.HashData(file.Value.Span))))
            .ToArray();
        return $"\"{CanonicalJson.ComputeHash(entries)}\"";
    }

    private sealed record SourceEntry(string Path, string Hash);
}
