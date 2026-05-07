using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Core.Config
{
    /// <summary>
    /// Gerenciador central de configurações.
    /// Cada configuração é uma pasta independente com estrutura completa de Resources.
    /// Suporta herança delta: configs podem herdar de outras usando o campo 'parent'.
    /// </summary>
    public static class ConfigManager
    {
        private static readonly object _configLock = new();

        /// <summary>
        /// Config padrão (configurável, não hardcoded)
        /// </summary>
        public static string DefaultConfig { get; set; } = "alisyum";

        /// <summary>
        /// Config atualmente ativa
        /// </summary>
        public static string CurrentConfig { get; private set; } = DefaultConfig;

        /// <summary>
        /// Caminho base de todas as configs (user://)
        /// Windows: %APPDATA%/HeroScript
        /// Linux: ~/.local/share/HeroScript
        /// Mac: ~/Library/Application Support/HeroScript
        /// </summary>
        public static string GetUserDataPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "HeroScript");
        }

        /// <summary>
        /// Retorna caminho da config ativa
        /// </summary>
        public static string GetCurrentConfigPath()
        {
            return GetConfigPath(CurrentConfig);
        }

        /// <summary>
        /// Retorna caminho de qualquer config
        /// </summary>
        public static string GetConfigPath(string configName)
        {
            return Path.Combine(GetUserDataPath(), configName);
        }

        /// <summary>
        /// Lista todas as configs disponíveis em user://
        /// </summary>
        public static IEnumerable<string> GetAvailableConfigs()
        {
            string userDataPath = GetUserDataPath();

            if (!Directory.Exists(userDataPath))
                return Enumerable.Empty<string>();

            return Directory.GetDirectories(userDataPath)
                            .Select(Path.GetFileName)
                            .Where(name => !string.IsNullOrEmpty(name))!;
        }

        /// <summary>
        /// Carrega metadados de uma config (config.json)
        /// </summary>
        public static ConfigMetadata? GetConfigMetadata(string configName)
        {
            string configPath = GetConfigPath(configName);
            string metadataPath = Path.Combine(configPath, "config.json");

            if (!File.Exists(metadataPath))
                return null;

            try
            {
                string jsonContent = File.ReadAllText(metadataPath);
                return JsonSerializer.Deserialize<ConfigMetadata>(jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigManager] Error loading metadata for '{configName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolve cadeia de herança de uma config.
        /// Retorna lista ordenada: [base, intermediário, atual]
        /// </summary>
        public static List<string> ResolveInheritanceChain(string configName)
        {
            var chain = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = configName;

            while (current != null)
            {
                // Detectar ciclo
                if (visited.Contains(current))
                {
                    string cyclePath = string.Join(" -> ", chain) + " -> " + current;
                    throw new InvalidOperationException($"Circular inheritance detected: {cyclePath}");
                }

                visited.Add(current);
                chain.Add(current);

                // Buscar pai
                var metadata = GetConfigMetadata(current);
                current = metadata?.Parent;
            }

            // Inverter: base primeiro, mod por último
            chain.Reverse();
            return chain;
        }

        /// <summary>
        /// Carrega uma config (substitui tudo).
        /// Valida estrutura antes de carregar.
        /// </summary>
        public static void LoadConfig(string configName)
        {
            lock (_configLock)
            {
                Console.WriteLine($"[ConfigManager] Loading config: {configName}");

                // 1. Validar estrutura de pastas
                try
                {
                    ConfigValidator.ValidateConfig(configName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConfigManager] Validation failed: {ex.Message}");
                    throw;
                }

                // 2. Resolver cadeia de herança
                List<string> chain;
                try
                {
                    chain = ResolveInheritanceChain(configName);
                    Console.WriteLine($"[ConfigManager] Inheritance chain: {string.Join(" -> ", chain)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConfigManager] Failed to resolve inheritance: {ex.Message}");
                    throw;
                }

                // 3. Atualizar config atual
                CurrentConfig = configName;

                // 4. Invalidar caches de sistemas
                Core.Math.MathEngine.ReloadFormulas();
                // Futuro: invalidar outros sistemas (CardInterpreter, etc.)

                Console.WriteLine($"[ConfigManager] Config '{configName}' loaded successfully");
            }
        }
    }
}
