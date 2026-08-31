using System.Reflection;
using Core.Combat.Models;
using Core.Determinism;
using Core.Run;
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

    private static bool IsInitOnly(MethodInfo setter)
    {
        return setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
    }
}
