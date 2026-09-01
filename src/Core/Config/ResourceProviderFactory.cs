using System;
using System.Diagnostics;
using System.IO;
using Core.Config.Providers;
using Core.Logging;

namespace Core.Config
{
    /// <summary>
    /// Factory para criar ResourcePathResolver configurado para dev ou production
    /// </summary>
    public class ResourceProviderFactory
    {
        private readonly IConfigManager _configManager;
        private readonly ILogger _logger;

        public ResourceProviderFactory(IConfigManager configManager, ILogger logger)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Cria um resolver configurado baseado no modo
        /// </summary>
        public ResourcePathResolver CreateResolver(ResourceConfiguration config)
        {
            var resolver = new ResourcePathResolver(_logger);
            var mode = config.Mode == ResourceMode.Auto ? DetectMode() : config.Mode;

            _logger.LogDebug($"Initializing in {mode} mode");

            if (mode == ResourceMode.Development)
            {
                RegisterDevelopmentProviders(resolver, config);
            }
            else
            {
                RegisterProductionProviders(resolver, config);
            }

            return resolver;
        }

        /// <summary>
        /// Detecta se está em ambiente de desenvolvimento
        /// </summary>
        private static ResourceMode DetectMode()
        {
            // Detecta se está em ambiente de desenvolvimento
            var isDevelopment =
                Environment.GetEnvironmentVariable("HEROSCRIPT_DEV_MODE") == "1" ||
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development" ||
                Debugger.IsAttached;

            return isDevelopment || FindProjectRoot() != null
                ? ResourceMode.Development
                : ResourceMode.Production;
        }

        /// <summary>
        /// Registra providers para modo desenvolvimento
        /// </summary>
        private void RegisterDevelopmentProviders(
            ResourcePathResolver resolver,
            ResourceConfiguration config)
        {
            // 1. User data (highest priority - permite override local)
            var userDataPath = _configManager.GetUserDataPath();
            if (Directory.Exists(userDataPath))
            {
                resolver.RegisterProvider(new PhysicalFileResourceProvider(
                    userDataPath, "UserData", priority: 100));
            }

            // 2. Project source (dev only - carrega direto do src/)
            var projectRoot = FindProjectRoot();
            if (projectRoot != null)
            {
                var dataConfigsPath = Path.Combine(projectRoot, "data", "configs");
                if (Directory.Exists(dataConfigsPath))
                {
                    resolver.RegisterProvider(new PhysicalFileResourceProvider(
                        dataConfigsPath, "DataConfigs (Dev)", priority: 75));
                    _logger.LogDebug($"Dev mode: Loading data configs from {dataConfigsPath}");
                }

                var coreResourcesPath = Path.Combine(projectRoot, "src", "Core", "Resources");
                if (Directory.Exists(coreResourcesPath))
                {
                    resolver.RegisterProvider(new PhysicalFileResourceProvider(
                        coreResourcesPath, "CoreResources (Dev)", priority: 50));
                    _logger.LogDebug($"Dev mode: Loading from {coreResourcesPath}");
                }
            }

            // 3. Build output (fallback)
            var buildOutputPath = Path.Combine(AppContext.BaseDirectory, "Resources");
            if (Directory.Exists(buildOutputPath))
            {
                resolver.RegisterProvider(new PhysicalFileResourceProvider(
                    buildOutputPath, "BuildOutput", priority: 10));
            }
        }

        /// <summary>
        /// Registra providers para modo produção
        /// </summary>
        private void RegisterProductionProviders(
            ResourcePathResolver resolver,
            ResourceConfiguration config)
        {
            // 1. User data (highest priority)
            var userDataPath = _configManager.GetUserDataPath();
            if (Directory.Exists(userDataPath))
            {
                resolver.RegisterProvider(new PhysicalFileResourceProvider(
                    userDataPath, "UserData", priority: 100));
            }

            // 2. Build output (core resources)
            var buildOutputPath = Path.Combine(AppContext.BaseDirectory, "Resources");
            if (Directory.Exists(buildOutputPath))
            {
                resolver.RegisterProvider(new PhysicalFileResourceProvider(
                    buildOutputPath, "CoreResources", priority: 50));
            }

            // 3. Custom path (se configurado)
            if (!string.IsNullOrEmpty(config.CoreResourcesPath) &&
                Directory.Exists(config.CoreResourcesPath))
            {
                resolver.RegisterProvider(new PhysicalFileResourceProvider(
                    config.CoreResourcesPath, "CustomResources", priority: 25));
            }
        }

        /// <summary>
        /// Encontra a raiz do projeto (onde está o .sln)
        /// </summary>
        public static string? FindProjectRoot()
        {
            var current = AppContext.BaseDirectory;
            while (current != null)
            {
                if (File.Exists(Path.Combine(current, "HeroScript.sln")) ||
                    File.Exists(Path.Combine(current, "HeroScript.slnx")) ||
                    Directory.Exists(Path.Combine(current, "src", "API")))
                    return current;
                current = Directory.GetParent(current)?.FullName;
            }
            return null;
        }
    }
}
