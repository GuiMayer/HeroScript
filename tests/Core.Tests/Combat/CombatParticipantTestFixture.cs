using System.Text.Json;
using Core.Combat;
using Core.Config;
using Core.Entity.Definitions;
using Core.Logging;
using Core.Resources;
using Moq;

namespace Core.Tests.Combat;

internal static class CombatParticipantTestFixture
{
    public static EntityDefinitionLoader CreateDefinitionLoader(ILogger logger)
    {
        var configs = new Mock<IConfigManager>();
        var resources = new Mock<IResourceLoader>();
        configs.Setup(manager => manager.ResolveInheritanceChain("test"))
            .Returns(["test"]);
        resources.Setup(loader => loader.LoadResource(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                true))
            .Returns((string path, IEnumerable<string> _, bool __) =>
            {
                var entityId = Path.GetFileNameWithoutExtension(path);
                var isHero = entityId.Contains("hero", StringComparison.OrdinalIgnoreCase) ||
                             entityId.Contains("player", StringComparison.OrdinalIgnoreCase);
                var pools = new Dictionary<string, ResourcePoolDefinition>(StringComparer.Ordinal)
                {
                    ["health"] = new()
                    {
                        Current = isHero ? 100 : 50,
                        Max = isHero ? 100 : 50
                    }
                };
                if (isHero)
                {
                    pools["energy"] = new() { Current = 3, Max = 10 };
                    pools["block"] = new() { Current = 0, Max = 999 };
                }

                var definition = new EntityDefinition
                {
                    DefinitionId = entityId,
                    DisplayName = entityId,
                    Components = isHero
                        ? [new ResourceEntityComponentDefinition { ComponentId = "resources", Pools = pools }]
                        :
                        [
                            new ResourceEntityComponentDefinition { ComponentId = "resources", Pools = pools },
                            new AbilityEntityComponentDefinition
                            {
                                ComponentId = "abilities",
                                AbilityIds = ["basic_attack"]
                            }
                        ]
                };
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(definition));
                return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [entityId] = document.RootElement.Clone()
                };
            });

        return new EntityDefinitionLoader(configs.Object, resources.Object, logger, "test");
    }

    public static CombatStartOptions WithResourceValues(
        string entityId,
        IReadOnlyDictionary<string, float> values,
        CombatStartOptions? options = null) =>
        (options ?? new CombatStartOptions()) with
        {
            InitialResourceValues = new Dictionary<string, IReadOnlyDictionary<string, float>>(
                StringComparer.Ordinal)
            {
                [entityId] = values
            }
        };

    public static CombatStartOptions WithEnergy(
        string entityId,
        float value,
        CombatStartOptions? options = null) =>
        WithResourceValues(
            entityId,
            new Dictionary<string, float>(StringComparer.Ordinal) { ["energy"] = value },
            options);
}
