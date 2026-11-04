using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Content;

namespace API.Services;

/// <summary>Best-effort index from package inputs to previously published, verified revisions.</summary>
internal sealed class StartupContentCache(string storePath)
{
    private const int Version = 1;
    private readonly string _indexPath = Path.Combine(storePath, "startup-index-v1.json");

    internal sealed record Entry(int Version, string Fingerprint, Dictionary<string, string> Revisions, string Checksum);

    private static string Checksum(string fingerprint, IReadOnlyDictionary<string, string> revisions)
    {
        var ordered = revisions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new[] { pair.Key, pair.Value }).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version, Fingerprint = fingerprint, Revisions = ordered });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string? Fingerprint(IReadOnlyList<string> roots)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void Append(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }
            Append("heroscript-startup-cache-v1");
            Append(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            foreach (var assembly in new[] { typeof(StartupContentCache).Assembly, typeof(ContentBundle).Assembly,
                         typeof(Mods.SettingCompiler).Assembly, typeof(JsonSerializer).Assembly })
            {
                var path = assembly.Location;
                if (string.IsNullOrEmpty(path)) return null;
                Append(assembly.GetName().Name ?? "");
                hash.AppendData(SHA256.HashData(File.ReadAllBytes(path)));
            }
            for (var index = 0; index < roots.Count; index++)
            {
                var root = Path.GetFullPath(roots[index]);
                Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(root);
                if (!Directory.Exists(root)) return null;
                var pending = new Stack<string>();
                var files = new List<string>();
                pending.Push(root);
                while (pending.TryPop(out var directory))
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return null;
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) return null;
                        if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                        else files.Add(entry);
                    }
                }
                foreach (var file in files.OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal))
                {
                    Append(Path.GetRelativePath(root, file).Replace('\\', '/'));
                    using var stream = File.OpenRead(file);
                    hash.AppendData(SHA256.HashData(stream));
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public Entry? Read(string fingerprint, IReadOnlyList<string> settingIds)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllBytes(_indexPath));
            if (entry is null || entry.Version != Version || entry.Fingerprint != fingerprint ||
                entry.Revisions is null || entry.Revisions.Count != settingIds.Count ||
                settingIds.Any(id => !entry.Revisions.TryGetValue(id, out var revision) || string.IsNullOrWhiteSpace(revision)) ||
                !string.Equals(entry.Checksum, Checksum(entry.Fingerprint, entry.Revisions), StringComparison.Ordinal)) return null;
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public void Write(string fingerprint, Dictionary<string, string> revisions)
    {
        var temporary = _indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new Entry(Version, fingerprint, revisions, Checksum(fingerprint, revisions))));
            File.Move(temporary, _indexPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
