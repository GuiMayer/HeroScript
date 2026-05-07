using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Config.Providers;

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

            Console.WriteLine($"[ResourcePathResolver] Resolving: {relativePath}");

            foreach (var provider in _providers)
            {
                var physicalPath = provider.GetPhysicalPath(relativePath);
                searchedLocations.Add($"{provider.Name}: {physicalPath ?? "N/A"}");

                if (provider.Exists(relativePath))
                {
                    Console.WriteLine($"[ResourcePathResolver] ✓ Found in '{provider.Name}': {physicalPath}");
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
                    Console.WriteLine($"[ResourcePathResolver] ✗ Not found in '{provider.Name}': {physicalPath}");
                }
            }

            Console.WriteLine($"[ResourcePathResolver] Resource not found: {relativePath}");
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
            result = Resolve(relativePath);
            return result.Found ? result.Provider!.OpenRead(relativePath) : null;
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
