using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Config.Providers;
using Core.Logging;

namespace Core.Config
{
    /// <summary>
    /// Resolve caminhos de recursos com fallback em cascata.
    /// Suporta múltiplos providers com prioridades configuráveis.
    /// </summary>
    public class ResourcePathResolver
    {
        private readonly List<IResourceProvider> _providers = new();
        private readonly object _lock = new();
        private readonly ILogger _logger;

        public ResourcePathResolver(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Registra um provider de recursos
        /// </summary>
        public void RegisterProvider(IResourceProvider provider)
        {
            lock (_lock)
            {
                _providers.Add(provider);
                // Ordenar por prioridade (maior primeiro)
                _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        /// <summary>
        /// Resolve um recurso através da cadeia de providers
        /// </summary>
        public ResourceResolutionResult Resolve(string relativePath)
        {
            var searchedLocations = new List<string>();

            _logger.LogDebug($"Resolving: {relativePath}");

            foreach (var provider in _providers)
            {
                var physicalPath = provider.GetPhysicalPath(relativePath);
                searchedLocations.Add($"{provider.Name}: {physicalPath ?? "N/A"}");

                if (provider.Exists(relativePath))
                {
                    _logger.LogDebug($"Found in '{provider.Name}': {physicalPath}");
                    return new ResourceResolutionResult
                    {
                        Found = true,
                        Provider = provider,
                        RelativePath = relativePath,
                        PhysicalPath = physicalPath,
                        SearchedLocations = searchedLocations
                    };
                }
                else
                {
                    _logger.LogDebug($"Not found in '{provider.Name}': {physicalPath}");
                }
            }

            _logger.LogWarning($"Resource not found: {relativePath}");
            return new ResourceResolutionResult
            {
                Found = false,
                RelativePath = relativePath,
                SearchedLocations = searchedLocations
            };
        }

        /// <summary>
        /// Abre um recurso e retorna o resultado da resolução
        /// </summary>
        public Stream? OpenResource(string relativePath, out ResourceResolutionResult result)
        {
            List<IResourceProvider> providers;
            lock (_lock)
            {
                providers = _providers.ToList();
            }

            var searchedLocations = new List<string>();
            foreach (var provider in providers)
            {
                var physicalPath = provider.GetPhysicalPath(relativePath);
                searchedLocations.Add($"{provider.Name}: {physicalPath ?? "N/A"}");

                try
                {
                    if (!provider.Exists(relativePath))
                        continue;

                    var stream = provider.OpenRead(relativePath);
                    if (stream == null)
                        continue;

                    result = new ResourceResolutionResult
                    {
                        Found = true,
                        Provider = provider,
                        RelativePath = relativePath,
                        PhysicalPath = physicalPath,
                        SearchedLocations = searchedLocations
                    };
                    return stream;
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(
                        $"Resource provider '{provider.Name}' denied access to '{physicalPath}': {ex.Message}");
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(
                        $"Resource provider '{provider.Name}' could not open '{physicalPath}': {ex.Message}");
                }
            }

            result = new ResourceResolutionResult
            {
                Found = false,
                RelativePath = relativePath,
                SearchedLocations = searchedLocations
            };
            return null;
        }

        /// <summary>
        /// Resolves every physical directory contributed by the configured
        /// providers. Directory discovery cannot use <see cref="Resolve"/>
        /// because that method intentionally accepts files only.
        /// </summary>
        public IReadOnlyList<string> GetExistingPhysicalDirectories(string relativeDirectory)
        {
            lock (_lock)
            {
                return _providers
                    .Select(provider => provider.GetPhysicalPath(relativeDirectory))
                    .Where(path => path != null && Directory.Exists(path))
                    .Select(path => Path.GetFullPath(path!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Resultado da resolução de um recurso
    /// </summary>
    public class ResourceResolutionResult
    {
        public bool Found { get; init; }
        public IResourceProvider? Provider { get; init; }
        public string RelativePath { get; init; } = "";
        public string? PhysicalPath { get; init; }
        public List<string> SearchedLocations { get; init; } = new();

        public static ResourceResolutionResult NotFound(string path) =>
            new() { Found = false, RelativePath = path };
    }
}
