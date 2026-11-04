using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Hashes startup build inputs without trusting file timestamps.</summary>
public static class HeroScriptBuildFingerprint
{
    public static string Compute(string repositoryRoot, string buildRoot, bool outputs, string sdk, string[] environment)
    {
        var files = new List<string>();
        var roots = outputs
            ? new[] { Path.Combine(buildRoot, "bin"), Path.Combine(buildRoot, "obj") }
            : new[] { Path.Combine(repositoryRoot, "src"), Path.Combine(repositoryRoot, "data", "configs"), Path.Combine(repositoryRoot, "docs", "schemas") };
        if (!outputs)
        {
            // Custom build imports may read inputs outside the known source graph.
            // Fall back to MSBuild instead of accepting an incomplete fingerprint.
            for (var directory = new DirectoryInfo(repositoryRoot); directory != null; directory = directory.Parent)
                foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
                    if (File.Exists(Path.Combine(directory.FullName, name))) return null;
            foreach (var name in new[] { "global.json", "NuGet.Config", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            {
                var file = Path.Combine(repositoryRoot, name);
                if (File.Exists(file)) files.Add(file);
            }
        }
        foreach (var root in roots)
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return null;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                foreach (var path in Directory.GetFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return null;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        var name = Path.GetFileName(path);
                        if (!outputs && (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase))) continue;
                        pending.Push(path);
                    }
                    else if (!outputs || !root.EndsWith("obj", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(Path.GetFileName(path), "project.assets.json", StringComparison.Ordinal))
                    {
                        if (!outputs && string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
                        {
                            var project = File.ReadAllText(path);
                            foreach (var tag in new[] { "<Import", "<Compile", "<AdditionalFiles", "<Analyzer", "<None" })
                                if (project.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0) return null;
                            var references = Regex.Matches(project,
                                "<\\s*(ProjectReference|Content|EmbeddedResource)\\b[^>]*\\bInclude\\s*=\\s*[\"']([^\"']+)[\"']",
                                RegexOptions.IgnoreCase);
                            foreach (Match reference in references)
                            {
                                var kind = reference.Groups[1].Value;
                                var include = reference.Groups[2].Value.Replace('\\', '/');
                                if (string.Equals(kind, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (include.Contains("$")) return null;
                                    var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), include));
                                    var sourceRoot = Path.Combine(repositoryRoot, "src").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                                    if (!resolved.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)) return null;
                                }
                                else if (include != "../../data/configs/**/*" &&
                                         include != "../../docs/schemas/gameplay-response.schema.json") return null;
                            }
                        }
                        if (!outputs && (string.Equals(Path.GetFileName(path), "Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(Path.GetFileName(path), "Directory.Build.targets", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(Path.GetFileName(path), "Directory.Packages.props", StringComparison.OrdinalIgnoreCase))) return null;
                        files.Add(path);
                    }
                }
            }
        }
        files.Sort(StringComparer.Ordinal);
        var basePath = (outputs ? buildRoot : repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var entries = new StringBuilder("heroscript-build-v2\n");
        foreach (var file in files)
        {
            var relative = file.Substring(basePath.Length + 1).Replace('\\', '/');
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(file))
                entries.Append(relative).Append(':').Append(BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "")).Append('\n');
        }
        if (!outputs)
        {
            entries.Append("sdk:").Append(sdk).Append('\n');
            Array.Sort(environment, StringComparer.Ordinal);
            foreach (var item in environment) entries.Append("env:").Append(item).Append('\n');
        }
        using (var algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(entries.ToString()))).Replace("-", "");
    }
}
