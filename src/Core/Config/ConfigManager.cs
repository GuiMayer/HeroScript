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
    public class ConfigManager : IConfigManager
    {
        private readonly object _configLock = new();
        private readonly ILogger _logger;
        private readonly ConfigValidator? _validator;
        private readonly Events.IEventBus? _eventBus;
        private string _currentConfig;

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
        public ConfigManager(ILogger logger, ConfigValidator? validator = null, Events.IEventBus? eventBus = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _validator = validator;
            _eventBus = eventBus;
            _currentConfig = DefaultConfig;
        }

        /// <summary>
        /// Caminho base de todas as configs (user://)
        /// Windows: %APPDATA%/HeroScript
        /// Linux: ~/.local/share/HeroScript
        /// Mac: ~/Library/Application Support/HeroScript
        /// </summary>
        public string UserConfigsPath => GetUserDataPath();

        /// <summary>
        /// Gets the user data path (public for backward compatibility)
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
        /// Checks if a configuration exists.
        /// </summary>
        public bool ConfigExists(string configName)
        {
            string configPath = GetConfigPath(configName);
            return Directory.Exists(configPath);
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
        public IEnumerable<string> ResolveInheritanceChain(string configName)
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

                // 1. Validar estrutura de pastas (se validator disponível)
                if (_validator != null)
                {
                    try
                    {
                        _validator.ValidateConfig(configName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Validation failed: {ex.Message}", ex);
                        throw;
                    }
                }

                // 2. Resolver cadeia de herança
                List<string> chain;
                try
                {
                    chain = ResolveInheritanceChain(configName).ToList();
                    _logger.LogInformation($"Inheritance chain: {string.Join(" -> ", chain)}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to resolve inheritance: {ex.Message}", ex);
                    throw;
                }

                // 3. Atualizar config atual
                string oldConfig = CurrentConfig;
                CurrentConfig = configName;

                // 4. Publicar evento se EventBus estiver configurado
                var metadata = GetConfigMetadata(configName);
                _eventBus?.Publish(new Events.Domain.ConfigLoadedEvent
                {
                    ConfigName = configName,
                    ParentConfig = metadata?.Parent,
                    ResourcesLoaded = chain.Count,
                    Target = configName
                });

                // Se houve mudança de config, publicar evento de mudança
                if (oldConfig != configName)
                {
                    _eventBus?.Publish(new Events.Domain.ConfigChangedEvent
                    {
                        OldConfig = oldConfig,
                        NewConfig = configName,
                        Reason = "LoadConfig called",
                        Target = configName
                    });
                }

                // 5. Invalidar caches de sistemas
                // Note: MathEngine cache invalidation is now handled via DI
                // Each MathEngine instance manages its own cache

                _logger.LogInformation($"Config '{configName}' loaded successfully");
            }
        }
    }
}
