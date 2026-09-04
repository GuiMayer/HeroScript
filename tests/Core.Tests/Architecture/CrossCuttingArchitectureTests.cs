using System.Reflection;
using Core.Combat.Models;
using Core.Combat;
using Core.Combat.Gambits;
using Core.Caching;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Core.StatusEffects;
using System.Collections.Immutable;
using Xunit;

namespace Core.Tests.Architecture;

/// <summary>
/// Guards the architectural boundaries shared by every gameplay module. More
/// focused tests exercise the behavior behind each boundary; these checks make
/// accidental bypasses visible when new modules are added.
/// </summary>
public sealed class CrossCuttingArchitectureTests
{
    [Theory]
    [InlineData(typeof(RunState))]
    [InlineData(typeof(CombatState))]
    [InlineData(typeof(DeterministicContext))]
    public void AuthoritativeGameplayState_DoesNotExposeMutableSetters(Type stateType)
    {
        var mutableProperties = stateType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod is { IsPublic: true } setter &&
                               !IsInitOnly(setter))
            .Select(property => property.Name)
            .ToArray();

        Assert.True(
            mutableProperties.Length == 0,
            $"{stateType.Name} exposes mutable properties: {string.Join(", ", mutableProperties)}");
    }

    [Fact]
    public void AuthoritativeGameplayState_OwnsAContentRevision()
    {
        var revision = new string('a', 64);
        var context = DeterministicContext.Create(17UL, revision);
        var run = new RunState { Determinism = context };
        var combat = new CombatState { Determinism = context };

        Assert.Equal(revision, run.Determinism.ContentRevision);
        Assert.Equal(revision, combat.Determinism.ContentRevision);
    }

    [Fact]
    public void CrossCuttingServices_HaveDedicatedAbstractions()
    {
        var coreAssembly = typeof(RunState).Assembly;
        var requiredContracts = new[]
        {
            "Core.Caching.ICacheService",
            "Core.Config.IResourceLoader",
            "Core.Content.IContentRuntimeResolver",
            "Core.Run.IGameplayCommandGateway",
            "Core.Events.IEventBus",
            "Core.Logging.ILogger",
            "Core.Math.IRuntimeFormulaEvaluator",
            "Core.Abstractions.Persistence.IRunStateRepository"
        };

        var missing = requiredContracts
            .Where(name => coreAssembly.GetType(name) == null)
            .ToArray();

        Assert.True(missing.Length == 0, $"Missing cross-cutting contracts: {string.Join(", ", missing)}");
    }

    [Fact]
    public void RuntimeInfrastructure_DoesNotExposeGlobalMutableServiceLocators()
    {
        var coreAssembly = typeof(RunState).Assembly;

        Assert.Null(coreAssembly.GetType("Core.Logging.LoggerFactory"));
        Assert.Null(typeof(CacheRegistry).GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void ResourceManager_DoesNotExposeUnversionedHotReloadControls()
    {
        var forbiddenMethods = new[]
        {
            "EnableHotReload",
            "DisableHotReload",
            "ReloadResource"
        };
        var exposed = typeof(IResourceManager)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .Intersect(forbiddenMethods, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            exposed.Length == 0,
            $"Unversioned resource reload controls are exposed: {string.Join(", ", exposed)}");
    }

    [Theory]
    [InlineData(typeof(ActionManager), "_definitions")]
    [InlineData(typeof(GambitEngine), "_definitions")]
    [InlineData(typeof(ResourceManager), "_definitions")]
    [InlineData(typeof(StatusEffectManager), "_definitions")]
    public void ReloadableSingletonCatalogs_PublishImmutableSnapshots(
        Type serviceType,
        string fieldName)
    {
        var field = serviceType.GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.True(field.FieldType.IsGenericType);
        Assert.Equal(
            typeof(ImmutableDictionary<,>),
            field.FieldType.GetGenericTypeDefinition());
    }

    [Fact]
    public void CommandExecutionContext_IsIsolatedPerAsyncFlow()
    {
        var field = typeof(RunManager).GetField(
            "_executingCommand",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.True(field.FieldType.IsGenericType);
        Assert.Equal(typeof(AsyncLocal<>), field.FieldType.GetGenericTypeDefinition());
    }

    private static bool IsInitOnly(MethodInfo setter)
    {
        return setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
    }
}
