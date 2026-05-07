using System;
using System.IO;
using System.Linq;
using Core.Config;

namespace Core.CLI.Commands
{
    /// <summary>
    /// Comando para sincronizar recursos do projeto para user data
    /// </summary>
    public class SyncResourcesCommand
    {
        public void Execute(SyncResourcesOptions options)
        {
            Console.WriteLine("=== HeroScript Resource Sync ===\n");

            var sourceDir = options.SourcePath ?? FindProjectResourcesPath();
            var targetDir = options.TargetPath ?? ConfigManager.Instance.GetUserDataPath();

            if (sourceDir == null || !Directory.Exists(sourceDir))
            {
                Console.WriteLine($"Error: Source directory not found: {sourceDir}");
                return;
            }

            Console.WriteLine($"Source: {sourceDir}");
            Console.WriteLine($"Target: {targetDir}");
            Console.WriteLine();

            var configs = options.Configs ?? new[] { ConfigManager.Instance.DefaultConfig };

            foreach (var config in configs)
            {
                Console.WriteLine($"Syncing config: {config}");
                SyncConfig(sourceDir, targetDir, config, options);
            }

            Console.WriteLine("\n✓ Sync complete");
        }

        private void SyncConfig(
            string sourceDir,
            string targetDir,
            string configName,
            SyncResourcesOptions options)
        {
            var configTargetDir = Path.Combine(targetDir, configName, "Resources");
            Directory.CreateDirectory(configTargetDir);

            var resourceFiles = Directory.GetFiles(sourceDir, "*.json", SearchOption.AllDirectories);

            foreach (var sourceFile in resourceFiles)
            {
                var relativePath = Path.GetRelativePath(sourceDir, sourceFile);
                var targetFile = Path.Combine(configTargetDir, relativePath);

                var targetFileDir = Path.GetDirectoryName(targetFile)!;
                Directory.CreateDirectory(targetFileDir);

                if (options.Force || !File.Exists(targetFile) ||
                    File.GetLastWriteTime(sourceFile) > File.GetLastWriteTime(targetFile))
                {
                    File.Copy(sourceFile, targetFile, overwrite: true);
                    Console.WriteLine($"  ✓ {relativePath}");
                }
                else
                {
                    Console.WriteLine($"  - {relativePath} (up to date)");
                }
            }
        }

        private string? FindProjectResourcesPath()
        {
            var current = Directory.GetCurrentDirectory();
            while (current != null)
            {
                var resourcesPath = Path.Combine(current, "src", "Core", "Resources");
                if (Directory.Exists(resourcesPath))
                    return resourcesPath;
                current = Directory.GetParent(current)?.FullName;
            }
            return null;
        }
    }

    /// <summary>
    /// Opções para o comando sync-resources
    /// </summary>
    public class SyncResourcesOptions
    {
        public string? SourcePath { get; set; }
        public string? TargetPath { get; set; }
        public string[]? Configs { get; set; }
        public bool Force { get; set; }
    }
}
