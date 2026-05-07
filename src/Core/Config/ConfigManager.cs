using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Logging;

namespace Core.Config
{
    /// <summary>
    /// Gerenciador central de configurações.
    /// Cada configuração é uma pasta independente com estrutura completa de Resources.
    /// Suporta herança delta: configs podem herdar de outras usando o campo 'parent'.
    /// </summary>
    public class ConfigManager
    {
        private static ConfigManager? _instance;
        private static readonly object _instanceLock = new();
        
        private readonly object _configLock = new();
        private readonly ILogger _logger;
        private string _currentConfig;

        /// <summary>
        /// Singleton instance (for backward compatibility)
        /// </summary>
        public static ConfigManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new ConfigManager();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Config padrão (configurável, não hardcoded)
        /// </summary>
        public string DefaultConfig { get; set; } = "alisyum";

        /// <summary>
        /// Config atualmente ativa
        /// </summary>
        public string CurrentConfig
        {
            get => _currentConfig;
            private set => _currentConfig = value;
        }

        /// <summary>
        /// Constructor for dependency injection
        /// </summary>
        public ConfigManager(ILogger? logger = null)
        {
            _logger = logger ?? CoreLogger.Current;
            _currentConfig = DefaultConfig;
        }

        /// <summary>
        /// Caminho base de todas as configs (user://)
        /// Windows: %APPDATA%/HeroScript
        /// Linux: ~/.local/share/HeroScript
        /// Mac: ~/Library/Application Support/HeroScript
        /// </summary>
        public string GetUserDataPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "HeroScript");
        }

        /// <summary>
        /// Retorna caminho da config ativa
        /// </summary>
        public string GetCurrentConfigPath()
        {
            return GetConfigPath(CurrentConfig);
        }

        /// <summary>
        /// Retorna caminho de qualquer config
        /// </summary>
        public string GetConfigPath(string configName)
        {
            return Path.Combine(GetUserDataPath(), configName);
        }

        /// <summary>
        /// Lista todas as configs disponíveis em user://
        /// </summary>
        public IEnumerable<string> GetAvailableConfigs()
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
        public ConfigMetadata? GetConfigMetadata(string configName)
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
                _logger.LogError($"Error loading metadata for '{configName}': {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Resolve cadeia de herança de uma config.
        /// Retorna lista ordenada: [base, intermediário, atual]
        /// </summary>
        public List<string> ResolveInheritanceChain(string configName)
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
        public void LoadConfig(string configName)
        {
            lock (_configLock)
            {
                _logger.LogInformation($"Loading config: {configName}");

                // 1. Validar estrutura de pastas
                try
                {
                    ConfigValidator.ValidateConfig(configName);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Validation failed: {ex.Message}", ex);
                    throw;
                }

                // 2. Resolver cadeia de herança
                List<string> chain;
                try
                {
                    chain = ResolveInheritanceChain(configName);
                    _logger.LogInformation($"Inheritance chain: {string.Join(" -> ", chain)}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to resolve inheritance: {ex.Message}", ex);
                    throw;
                }

                // 3. Atualizar config atual
                CurrentConfig = configName;

                // 4. Invalidar caches de sistemas
                Core.Math.MathEngine.ReloadFormulas();
                // Futuro: invalidar outros sistemas (CardInterpreter, etc.)

                _logger.LogInformation($"Config '{configName}' loaded successfully");
            }
        }
    }
}
