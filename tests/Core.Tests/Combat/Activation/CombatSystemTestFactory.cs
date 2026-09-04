using Core.Combat;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Logging;
using Core.Resources;

namespace Core.Tests.Combat.Activation;

internal static class CombatSystemTestFactory
{
    public static CombatSystem Create()
    {
        return new CombatSystem(new NullLogger(), new StubResourceManager(), new FixedTurnOrderCalculator(new NullLogger()));
    }

    private sealed class NullLogger : ILogger
    {
        public void LogDebug(string message) { }
        public void LogInformation(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
        public void LogError(string message, Exception? exception = null) { }
    }

    private sealed class StubResourceManager : IResourceManager
    {
        public void LoadResourceDefinitions(string configName) { }
        public Core.Common.Result<ResourceDefinition> GetDefinition(string resourceId) => Core.Common.Result<ResourceDefinition>.Failure($"Resource not found: {resourceId}");
        public IReadOnlyList<ResourceDefinition> GetAllDefinitions() => Array.Empty<ResourceDefinition>();
        public IReadOnlyList<ResourceDefinition> GetDefinitionsByCategory(ResourceCategory category) => Array.Empty<ResourceDefinition>();
        public IReadOnlyList<ResourceDefinition> GetDefinitionsByTag(string tag) => Array.Empty<ResourceDefinition>();
        public ResourcePool CreatePool(string resourceId, float? initialCurrent = null) => new() { ResourceId = resourceId, Current = initialCurrent ?? 0, Maximum = initialCurrent ?? 0 };
        public ResourcePool CreatePoolFromDefinition(ResourceDefinition definition, float? initialCurrent = null) => new() { ResourceId = definition.ResourceId, Current = initialCurrent ?? definition.DefaultCurrent, Maximum = definition.DefaultMax, Minimum = definition.DefaultMin, Definition = definition };
        public Dictionary<string, ResourcePool> CreateDefaultPools() => new();
        public bool ValidateResourceExists(string resourceId) => false;
        public Core.Common.Result ValidateCost(ResourcePool pool, float cost) => Core.Common.Result.Success();
        public Core.Common.Result ValidateResourceDefinition(ResourceDefinition definition) => Core.Common.Result.Success();
        public Core.Common.Result<ResourceSet> ProcessRegeneration(ResourceSet entityResourceState, RegenerationTiming timing, Dictionary<string, float>? context = null) => Core.Common.Result<ResourceSet>.Success(entityResourceState);
        public void EnableHotReload(string configName) { }
        public void DisableHotReload() { }
        public Core.Common.Result ReloadResource(string resourceId) => Core.Common.Result.Success();
    }
}
