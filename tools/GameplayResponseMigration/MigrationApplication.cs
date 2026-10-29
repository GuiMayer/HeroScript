using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Common;
using Core.Content;
using Mods;

internal static class MigrationApplication
{
    internal static int Apply(string root, string backup, IReadOnlyList<FileProposal> proposals, JsonSerializerOptions options)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (backup.Equals(root, comparison) || backup.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison) || Directory.Exists(backup))
        { Console.Error.WriteLine("Use a new backup directory outside the package root."); return 2; }
        var opened = new List<(string Relative, FileStream Stream, byte[] Before, byte[] After)>();
        var changed = false;
        try
        {
            foreach (var proposal in proposals.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                var path = Path.GetFullPath(Path.Combine(root, proposal.Path));
                if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                    throw new IOException("Migration target is outside the package root");
                var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                byte[] original;
                try
                {
                    using var buffer = new MemoryStream(); stream.CopyTo(buffer); original = buffer.ToArray();
                    if (Convert.ToHexStringLower(SHA256.HashData(original)) != proposal.OriginalSha256)
                        throw new IOException($"Stale migration preimage: {proposal.Path}");
                }
                catch { stream.Dispose(); throw; }
                opened.Add((proposal.Path, stream, original, Encoding.UTF8.GetBytes(proposal.ProposedDocument.ToJsonString(options) + "\n")));
            }
            var provider = new ProposedProvider(new DirectoryPackageProvider("migration", root), proposals);
            var compiler = new SettingCompiler([provider]);
            var catalog = compiler.GetCatalogAsync().GetAwaiter().GetResult();
            if (catalog.Settings.Count == 0 || catalog.Diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
                throw new IOException("Package discovery failed or has no settings");
            foreach (var setting in catalog.Settings)
            {
                var compiled = compiler.CompileBundleAsync(setting.Definition.SettingId).GetAwaiter().GetResult();
                if (compiled.IsFailure) throw new IOException(compiled.Error);
                var validation = new ContentGraphValidator().Validate(compiled.Value);
                if (!validation.IsValid) throw new IOException(string.Join("; ", validation.Errors));
            }
            Directory.CreateDirectory(backup);
            foreach (var file in opened)
            {
                var path = Path.Combine(backup, file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Before);
            }
            File.WriteAllText(Path.Combine(backup, "migration.json"), JsonSerializer.Serialize(new { root, proposals }, options), new UTF8Encoding(false));
            changed = true;
            foreach (var file in opened) Write(file.Stream, file.After);
            File.WriteAllText(Path.Combine(backup, "completed"), "Validated content migration completed. Published bundles and saves were not changed.\n");
            Console.WriteLine($"Applied {opened.Count} files. Backup: {backup}");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (changed) foreach (var file in opened) Write(file.Stream, file.Before);
            Console.Error.WriteLine(exception.Message); return 2;
        }
        finally { foreach (var file in opened) file.Stream.Dispose(); }
    }
    private static void Write(FileStream stream, byte[] bytes)
    { stream.Position = 0; stream.Write(bytes); stream.SetLength(bytes.Length); stream.Flush(flushToDisk: true); }

    private sealed class ProposedProvider(DirectoryPackageProvider inner, IReadOnlyList<FileProposal> proposals) : IPackageProvider
    {
        public string ProviderId => inner.ProviderId;
        public Task<PackageDiscovery> DiscoverAsync(CancellationToken token = default) => inner.DiscoverAsync(token);
        public Task<Result<JsonElement>> ReadJsonAsync(PackageSource package, string relative, CancellationToken token = default)
        {
            var path = (package.SourceId.TrimEnd('/') + "/" + relative).TrimStart('.','/').Replace('\\', '/');
            var proposed = proposals.SingleOrDefault(item => item.Path.Replace('\\', '/') == path);
            return proposed != null ? Task.FromResult(Result<JsonElement>.Success(JsonSerializer.SerializeToElement(proposed.ProposedDocument)))
                : inner.ReadJsonAsync(package, relative, token);
        }
    }
}
