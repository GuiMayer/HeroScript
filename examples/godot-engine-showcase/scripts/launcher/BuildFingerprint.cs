using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

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
                        files.Add(path);
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
