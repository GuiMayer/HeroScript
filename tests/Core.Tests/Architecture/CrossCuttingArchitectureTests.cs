using System.Reflection;
using Core.Combat.Models;
using Core.Combat;
using Core.Combat.Gambits;
using Core.Combat.TurnOrder;
using Core.Caching;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Core.StatusEffects;
using Core.Events.Domain;
using Core.Math;
using System.Collections.Immutable;
using System.Collections.Concurrent;
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
    [InlineData(typeof(TurnOrderState))]
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
            "Core.Events.IOperationalEventBus",
            "Core.Logging.ILogger",
            "Core.Math.IRuntimeFormulaEvaluator",
            "Core.Abstractions.Persistence.IRunCommitStore"
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
    public void CanonicalGameplayFlow_DoesNotReintroduceRemovedAuthorities()
    {
        var coreAssembly = typeof(RunState).Assembly;
        var removedTypes = new[]
        {
            "Core.Combat.CombatSystem",
            "Core.Combat.ICombatSystem",
            "Core.Damage.DamageCalculator",
            "Core.Damage.IDamageCalculator",
            "Core.Effects.EffectResolver",
            "Core.Effects.IEffectResolver",
            "Core.StatusEffects.StatusEffectManager",
            "Core.StatusEffects.IStatusEffectManager",
            "Core.StatusEffects.StatusEffectProcessor",
            "Core.Combat.Modifiers.ScriptModifierManager",
            "Core.Combat.Modifiers.IScriptModifierManager",
            "Core.Resources.ResourceRegenerationProcessor",
            "Core.Resources.IResourceRegenerationProcessor",
            "Core.Entity.Components.StatusEffectComponent",
            "Core.Combat.TurnPhase.PhaseManager",
            "Core.Combat.TurnPhase.PrioritySystem",
            "Core.Combat.TurnPhase.ActionStackManager",
            "Core.Combat.TurnPhase.PhaseSystemFactory",
            "Core.Combat.TurnPhase.PhaseSequenceLoader",
            "Core.Entity.Entity",
            "Core.Entity.IComponent",
            "Core.Entity.ComponentBase",
            "Core.Entity.Definitions.EntityFactory",
            "Core.Entity.Integration.EntityCombatAdapter",
            "Core.Combat.Gambits.GambitEngine",
            "Core.Combat.Gambits.IGambitEngine",
            "Core.Config.CombatOptions",
            "Core.Combat.TurnOrder.ITurnOrderCalculator",
            "Core.Combat.TurnOrder.TurnOrderCalculatorFactory",
            "Core.Combat.TurnOrder.ConditionalTurnOrderCalculator"
        };
        var present = removedTypes
            .Where(name => coreAssembly.GetType(name) != null)
            .ToArray();

        Assert.True(
            present.Length == 0,
            $"Removed gameplay authorities were reintroduced: {string.Join(", ", present)}");
    }

    [Theory]
    [InlineData(typeof(MathEngine))]
    [InlineData(typeof(GambitDecisionReducer))]
    [InlineData(typeof(Core.Combat.LegalActions.LegalActionResolver))]
    [InlineData(typeof(TurnOrderResolver))]
    public void PureEvaluationServices_DoNotPublishEvents(Type serviceType)
    {
        var eventBusType = typeof(Core.Events.IOperationalEventBus);
        var eventFields = serviceType
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => eventBusType.IsAssignableFrom(field.FieldType))
            .Select(field => field.Name)
            .ToArray();
        var eventParameters = serviceType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => eventBusType.IsAssignableFrom(parameter.ParameterType))
            .Select(parameter => parameter.Name)
            .ToArray();

        Assert.True(
            eventFields.Length == 0 && eventParameters.Length == 0,
            $"{serviceType.Name} can publish events during evaluation");
    }

    [Fact]
    public void TurnOrderUsesPinnedPolicyAndOwnsNoMutableRuntimeState()
    {
        var plannerFields = typeof(Core.Combat.Flow.CombatFlowPlanner)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        var resolverFields = typeof(TurnOrderResolver)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        var policyProperties = typeof(TurnOrderPolicyDefinition)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.Contains(plannerFields,
            field => typeof(ITurnOrderResolver).IsAssignableFrom(field.FieldType));
        Assert.DoesNotContain(resolverFields,
            field => field.FieldType == typeof(CombatState) || field.FieldType == typeof(TurnOrderState));
        Assert.DoesNotContain(policyProperties,
            property => typeof(Delegate).IsAssignableFrom(property.PropertyType));
    }

    [Fact]
    public void PlayerPreviewIntentAndCoordinatorShareOneLegalActionBoundary()
    {
        var boundary = typeof(Core.Combat.LegalActions.ILegalActionResolver);
        var consumers = new[]
        {
            typeof(Core.Run.Content.CardInspectionService),
            typeof(Core.Combat.Intents.IntentResolver),
            typeof(Core.Combat.CombatRunCoordinator)
        };

        Assert.All(consumers, consumer => Assert.Contains(
            consumer.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => boundary.IsAssignableFrom(field.FieldType)));
    }

    [Fact]
    public void DecisionPoliciesDoNotOwnMutableAuthoringOrRuntimeCaches()
    {
        var forbidden = new[]
        {
            typeof(Core.Config.IConfigManager),
            typeof(Core.Config.IResourceLoader),
            typeof(Core.Resources.IDefinitionPersister),
            typeof(Core.Caching.ICacheService)
        };
        var services = new[]
        {
            typeof(GambitDecisionPolicy),
            typeof(GambitDecisionReducer),
            typeof(Core.Combat.Intents.IntentResolver)
        };

        Assert.All(services, service => Assert.DoesNotContain(
            service.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => forbidden.Any(type => type.IsAssignableFrom(field.FieldType))));
    }

    [Fact]
    public void CombatCoordinatorCannotBypassCanonicalActionExecutors()
    {
        var fields = typeof(Core.Combat.CombatRunCoordinator)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.DoesNotContain(fields,
            field => typeof(Core.Run.Content.IAbilityExecutor).IsAssignableFrom(field.FieldType));
        Assert.DoesNotContain(fields,
            field => typeof(Core.Run.Content.ICardPlayExecutor).IsAssignableFrom(field.FieldType));
        Assert.Contains(fields,
            field => typeof(Core.Combat.LegalActions.ILegalActionResolver).IsAssignableFrom(field.FieldType));
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

    [Fact]
    public void ResourceOwners_DoNotExposeMutationReducerBypasses()
    {
        var forbidden = new[]
        {
            "UpdateResource",
            "UpdateResources",
            "WithResource",
            "WithResources",
            "ReduceResource",
            "IncreaseResource"
        };
        var ownerTypes = new[]
        {
            typeof(ResourceSet),
            typeof(CombatActorState)
        };
        var exposed = ownerTypes
            .SelectMany(type => type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => forbidden.Contains(method.Name, StringComparer.Ordinal))
                .Select(method => $"{type.Name}.{method.Name}"))
            .ToArray();

        Assert.True(
            exposed.Length == 0,
            $"Resource mutation reducer bypasses are exposed: {string.Join(", ", exposed)}");
        Assert.NotNull(typeof(ResourceSet).GetMethod(
            nameof(ResourceSet.Apply),
            BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(typeof(CombatActorState).GetMethod(
            nameof(CombatActorState.ApplyResourceMutation),
            BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void CombatHistory_DoesNotExposePrivilegedResourceSummaries()
    {
        var forbiddenProperties = new[]
        {
            "DamageDealt",
            "DamageTaken",
            "EnergyChange",
            "InitialEnergy"
        };
        var contractTypes = new[]
        {
            typeof(CombatAction),
            typeof(CombatResult),
            typeof(ActionExecutedEvent),
            typeof(CombatStartedEvent)
        };
        var exposed = contractTypes
            .SelectMany(type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => forbiddenProperties.Contains(property.Name, StringComparer.Ordinal))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        Assert.True(
            exposed.Length == 0,
            $"Privileged resource summaries are exposed: {string.Join(", ", exposed)}");
        Assert.Null(typeof(CombatAction).Assembly.GetType("Core.Events.Domain.EnergyChangedEvent"));
        Assert.NotNull(typeof(CombatAction).GetProperty(nameof(CombatAction.Applications)));
    }

    [Fact]
    public void ResourceSystem_DoesNotExposeSpecializedPoolsOrMockEntityFactories()
    {
        var coreAssembly = typeof(ResourcePool).Assembly;

        Assert.Null(coreAssembly.GetType("Core.Combat.Models.EnergyPool"));
        Assert.Null(coreAssembly.GetType("Core.Combat.IEntityFactory"));
        Assert.Null(coreAssembly.GetType("Core.Combat.EntityFactory"));
        Assert.Null(typeof(ResourceSet).GetMethod("FirstInCategory"));
        Assert.Null(coreAssembly.GetType("Core.Entity.Components.ResourceComponent"));
    }

    [Theory]
    [InlineData(typeof(ActionManager), "_definitions")]
    [InlineData(typeof(ResourceManager), "_definitions")]
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
    public void RunCommands_UsePerAggregateCoordinatorWithoutAmbientContext()
    {
        var ambient = typeof(RunManager).GetField(
            "_executingCommand",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var gates = typeof(RunSessionGateProvider).GetField(
            "_gates",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.Null(ambient);
        Assert.NotNull(gates);
        Assert.True(gates.FieldType.IsGenericType);
        Assert.Equal(typeof(ConcurrentDictionary<,>), gates.FieldType.GetGenericTypeDefinition());
        Assert.Equal(typeof(SemaphoreSlim), gates.FieldType.GetGenericArguments()[1]);
    }

    [Fact]
    public void PublicRunManagerSurface_DoesNotExposeLateralMutations()
    {
        var methods = typeof(IRunManager)
            .GetInterfaces()
            .Append(typeof(IRunManager))
            .SelectMany(type => type.GetMethods())
            .Select(method => method.Name)
            .ToHashSet();

        Assert.DoesNotContain("DrawCards", methods);
        Assert.DoesNotContain("AdvanceNode", methods);
        Assert.DoesNotContain("ApplyRunResource", methods);
        Assert.DoesNotContain("CreateShop", methods);
        Assert.DoesNotContain("AttachEncounter", methods);
        Assert.Contains("StartRun", methods);
        Assert.Contains("GetRun", methods);
    }

    private static bool IsInitOnly(MethodInfo setter)
    {
        return setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
    }
}
