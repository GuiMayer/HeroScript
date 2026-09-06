using Core.Common;
using Core.Config;
using Core.Logging;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Resources;

/// <summary>
/// Testes de integração para carregamento dinâmico de recursos.
/// </summary>
public class ResourceManagerDynamicLoadingTests : IDisposable
{
    private readonly string _testConfigPath;
    private readonly string _testResourcesPath;
    private readonly TestConfigManager _configManager;
    private readonly TestResourceLoader _resourceLoader;
    private readonly Mock<ILogger> _mockLogger;
    private readonly ResourceManager _resourceManager;

    public ResourceManagerDynamicLoadingTests()
    {
        // Setup test environment
        _testConfigPath = Path.Combine(Path.GetTempPath(), $"HeroScript_Test_{Guid.NewGuid()}");
        _testResourcesPath = Path.Combine(_testConfigPath, "default", "Resources", "resources");
        Directory.CreateDirectory(_testResourcesPath);

        // Create test dependencies
        _configManager = new TestConfigManager(_testConfigPath);
        _resourceLoader = new TestResourceLoader(_testResourcesPath);
        _mockLogger = new Mock<ILogger>();
        
        // Create resource manager
        _resourceManager = new ResourceManager(
            _configManager,
            _resourceLoader,
            _mockLogger.Object);
    }

    [Fact]
    public void LoadResourceDefinitions_ShouldDiscoverAllResourceFiles()
    {
        // Arrange: Create multiple resource files
        CreateTestResource("health", "Health", ResourceCategory.VITAL);
        CreateTestResource("mana", "Mana", ResourceCategory.VITAL);
        CreateTestResource("stamina", "Stamina", ResourceCategory.TACTICAL);

        // Act
        _resourceManager.LoadResourceDefinitions("default");

        // Assert
        var definitions = _resourceManager.GetAllDefinitions();
        Assert.Equal(3, definitions.Count);
        Assert.Contains(definitions, d => d.ResourceId == "health");
        Assert.Contains(definitions, d => d.ResourceId == "mana");
        Assert.Contains(definitions, d => d.ResourceId == "stamina");
    }

    [Fact]
    public void LoadResourceDefinitions_ShouldHandleEmptyDirectory()
    {
        // Act
        _resourceManager.LoadResourceDefinitions("default");

        // Assert
        var definitions = _resourceManager.GetAllDefinitions();
        Assert.Empty(definitions);
    }

    [Fact]
    public void LoadResourceDefinitions_ShouldSkipInvalidFiles()
    {
        // Arrange: Create valid and invalid resource files
        CreateTestResource("health", "Health", ResourceCategory.VITAL);
        CreateInvalidResource("invalid");

        // Act
        _resourceManager.LoadResourceDefinitions("default");

        // Assert
        var definitions = _resourceManager.GetAllDefinitions();
        Assert.Single(definitions);
        Assert.Equal("health", definitions[0].ResourceId);
    }

    private void CreateTestResource(string resourceId, string displayName, ResourceCategory category)
    {
        var shortName = displayName.Length > 3 ? displayName.Substring(0, 3) : displayName;
        var resourceJson = $$"""
        {
            "{{resourceId}}": {
                "ResourceId": "{{resourceId}}",
                "DisplayName": "{{displayName}}",
                "ShortName": "{{shortName}}",
                "Category": "{{category}}",
                "DefaultMin": 0,
                "DefaultMax": 100,
                "DefaultCurrent": 100,
                "CanBeNegative": false,
                "CanExceedMax": false,
                "CostMultiplier": 1.0,
                "Tags": ["test"]
            }
        }
        """;

        File.WriteAllText(Path.Combine(_testResourcesPath, $"{resourceId}.json"), resourceJson);
    }

    private void CreateInvalidResource(string resourceId)
    {
        var invalidJson = "{ invalid json }";
        File.WriteAllText(Path.Combine(_testResourcesPath, $"{resourceId}.json"), invalidJson);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testConfigPath))
        {
            Directory.Delete(_testConfigPath, true);
        }
    }

    /// <summary>
    /// Simple test implementation of IConfigManager
    /// </summary>
    private class TestConfigManager : IConfigManager
    {
        private readonly string _basePath;

        public TestConfigManager(string basePath)
        {
            _basePath = basePath;
        }

        public string CurrentConfig => "default";
        public string DefaultConfig => "default";
        public string UserConfigsPath => _basePath;

        public void LoadConfig(string configName) { }

        public string GetConfigPath(string configName)
        {
            return Path.Combine(_basePath, configName);
        }

        public string GetUserDataPath() => _basePath;

        public IEnumerable<string> ResolveInheritanceChain(string configName)
        {
            return new[] { configName };
        }

        public ConfigMetadata? GetConfigMetadata(string configName)
        {
            return new ConfigMetadata
            {
                Name = configName,
                Description = "Test config",
                Version = "1.0.0"
            };
        }

        public IEnumerable<string> GetAvailableConfigs()
        {
            return new[] { "default" };
        }

        public bool ConfigExists(string configName)
        {
            return configName == "default";
        }
    }

    /// <summary>
    /// Simple test implementation of IResourceLoader
    /// </summary>
    private class TestResourceLoader : IResourceLoader
    {
        private readonly string _resourcesPath;
        private readonly Dictionary<string, string> _cache = new();

        public TestResourceLoader(string resourcesPath)
        {
            _resourcesPath = resourcesPath;
        }

        public Dictionary<string, System.Text.Json.JsonElement> LoadResource(
            string relativePath,
            IEnumerable<string> configChain,
            bool strictMode = true)
        {
            var result = new Dictionary<string, System.Text.Json.JsonElement>();
            
            // Extract just the filename from the relative path
            // e.g., "resources/health.json" -> "health.json"
            var fileName = Path.GetFileName(relativePath);
            
            // Build the full path to the resource file
            // _resourcesPath already points to the "resources" directory
            var fullPath = Path.Combine(_resourcesPath, fileName);
            
            if (!File.Exists(fullPath))
            {
                return result;
            }
            
            try
            {
                var json = File.ReadAllText(fullPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                
                // Clone each property to avoid disposal issues
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    result[property.Name] = property.Value.Clone();
                }
            }
            catch
            {
                // Invalid JSON, return empty
            }
            
            return result;
        }

        public Task<Dictionary<string, System.Text.Json.JsonElement>> LoadResourceAsync(
            string relativePath,
            IEnumerable<string> configChain,
            bool strictMode = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LoadResource(relativePath, configChain, strictMode));
        }

        public void InvalidateCache()
        {
            _cache.Clear();
        }

        public void InvalidateCache(string relativePath)
        {
            _cache.Remove(relativePath);
        }

        public Dictionary<string, string> GetResourceOrigins(string relativePath)
        {
            return new Dictionary<string, string>();
        }

        public Dictionary<string, object> GetCacheStats()
        {
            return new Dictionary<string, object>
            {
                { "size", _cache.Count },
                { "hits", 0 },
                { "misses", 0 }
            };
        }

        public IEnumerable<string> DiscoverResources(
            string relativeDirectory,
            IEnumerable<string> configChain,
            string filePattern = "*.json")
        {
            // The relativeDirectory is "resources", but _resourcesPath already points to the resources directory
            // So we just need to list files in _resourcesPath
            if (!Directory.Exists(_resourcesPath))
            {
                return Enumerable.Empty<string>();
            }

            var files = Directory.GetFiles(_resourcesPath, filePattern);
            var resourceNames = files
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();

            return resourceNames!;
        }
    }
}
