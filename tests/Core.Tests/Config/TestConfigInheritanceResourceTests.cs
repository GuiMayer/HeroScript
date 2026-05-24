using Core.Config;
using Core.Logging;
using Xunit;

namespace Core.Tests.Config;

public sealed class TestConfigInheritanceResourceTests
{
    [Fact]
    public void ResourceLoader_LoadsTestConfigInheritanceChain_WithDeltaDeletesAndMerges()
    {
        var root = Path.Combine(FindProjectRoot(), "tests", "configs");
        var configManager = new TestConfigManager(root);
        var loader = new ResourceLoader(NullLogger.Instance, new ResourceProviderFactory(configManager));
        loader.InitializePathResolver(new ResourceConfiguration
        {
            Mode = ResourceMode.Production,
            CoreResourcesPath = root
        });

        var chain = configManager.ResolveInheritanceChain("test-orc-mod");
        var skills = loader.LoadResource("Skills/Skills.json", chain, strictMode: true);

        Assert.Equal(new[] { "alisyum", "test-orc", "test-orc-mod" }, chain);
        Assert.Contains("FIREBALL", skills.Keys);
        Assert.Contains("HEALING_WAVE", skills.Keys);
        Assert.True(skills["HEALING_WAVE"].TryGetProperty("$delta", out var deletedDelta));
        Assert.Equal("DELETE", deletedDelta.GetProperty("$op").GetString());
        Assert.True(skills["FIREBALL"].TryGetProperty("$delta", out var fireballDelta));
        Assert.Equal("MERGE_DEEP", fireballDelta.GetProperty("$op").GetString());
        Assert.True(skills["SHIELD_BASH"].TryGetProperty("$delta", out var shieldDelta));
        Assert.Equal("MERGE_DEEP", shieldDelta.GetProperty("$op").GetString());
        Assert.Equal("Blood Rage", skills["BLOOD_RAGE"].GetProperty("$delta").GetProperty("$data").GetProperty("name").GetString());
    }

    [Fact]
    public void ResourceLoader_LoadsThreeLevelTestConfigInheritanceChain()
    {
        var root = Path.Combine(FindProjectRoot(), "tests", "configs");
        var configManager = new TestConfigManager(root);
        var loader = new ResourceLoader(NullLogger.Instance, new ResourceProviderFactory(configManager));
        loader.InitializePathResolver(new ResourceConfiguration
        {
            Mode = ResourceMode.Production,
            CoreResourcesPath = root
        });

        var chain = configManager.ResolveInheritanceChain("test-orc-mod-hardcore");
        var skills = loader.LoadResource("Skills/Skills.json", chain, strictMode: true);

        Assert.Equal(new[] { "alisyum", "test-orc", "test-orc-mod", "test-orc-mod-hardcore" }, chain);
        Assert.True(skills["BLOOD_RAGE"].TryGetProperty("$delta", out var bloodRageDelta));
        Assert.Equal("MERGE_DEEP", bloodRageDelta.GetProperty("$op").GetString());
        Assert.Equal("Warlord Command", skills["WARLORD_COMMAND"].GetProperty("$delta").GetProperty("$data").GetProperty("name").GetString());
        Assert.True(skills["SHIELD_BASH"].TryGetProperty("$delta", out var shieldDelta));
        Assert.Equal("MERGE_DEEP", shieldDelta.GetProperty("$op").GetString());
    }

    private static string FindProjectRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, "tests", "configs")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find HeroScript project root.");
    }

    private sealed class TestConfigManager : IConfigManager
    {
        private readonly string _root;

        public TestConfigManager(string root)
        {
            _root = root;
        }

        public string CurrentConfig => "test-orc";
        public string DefaultConfig { get; set; } = "test-orc";
        public string UserConfigsPath => _root;
        public string GetUserDataPath() => _root;
        public string GetCurrentConfigPath() => GetConfigPath(CurrentConfig);
        public string GetConfigPath(string configName) => Path.Combine(_root, configName);
        public IEnumerable<string> GetAvailableConfigs() => Directory.GetDirectories(_root).Select(Path.GetFileName)!;
        public bool ConfigExists(string configName) => Directory.Exists(GetConfigPath(configName));

        public ConfigMetadata? GetConfigMetadata(string configName)
        {
            var metadataPath = Path.Combine(GetConfigPath(configName), "config.json");
            if (!File.Exists(metadataPath))
                return null;

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = document.RootElement;
            string? parent = null;
            if (root.TryGetProperty("parent", out var parentProperty))
                parent = parentProperty.GetString();
            else if (root.TryGetProperty("inherits", out var inheritsProperty))
                parent = inheritsProperty.GetString();

            return new ConfigMetadata
            {
                Name = root.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? configName : configName,
                Parent = string.IsNullOrWhiteSpace(parent) ? null : parent
            };
        }

        public IEnumerable<string> ResolveInheritanceChain(string configName)
        {
            var chain = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? current = configName;

            while (!string.IsNullOrWhiteSpace(current))
            {
                if (!visited.Add(current))
                    throw new InvalidOperationException($"Circular inheritance detected: {current}");

                chain.Add(current);
                current = GetConfigMetadata(current)?.Parent;
            }

            chain.Reverse();
            return chain;
        }

        public void LoadConfig(string configName) { }
    }
}
