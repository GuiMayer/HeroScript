using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Content;
using Core.Determinism;
using Core.Entity.Definitions;
using Core.Resources;
using Core.Run;
using Xunit;

namespace Core.Tests.Run;

public sealed class ActorResourceLifecycleTests
{
    [Fact]
    public void PersistentContainerMaterializesResourceComponentsWithoutWalletOrAliasing()
    {
        var player = Player();
        var resources = player.Component<ResourceEntityComponentState>()!;
        Assert.Equal("hero", resources.State.OwnerId);
        Assert.Equal(37, resources.State.Current("resolve"));
        Assert.Equal(60, resources.State.Get("resolve")!.Maximum);
        Assert.Null(resources.State.Get("gold"));
        var json = JsonSerializer.Serialize(player);
        Assert.Equal(CanonicalJson.ComputeHash(player), CanonicalJson.ComputeHash(JsonSerializer.Deserialize<EntityState>(json)!));
    }

    [Theory]
    [InlineData(ActorResourceLifecycleAction.PreserveCurrent, 37)]
    [InlineData(ActorResourceLifecycleAction.ResetToMaximum, 60)]
    [InlineData(ActorResourceLifecycleAction.ResetToConfiguredValue, 12)]
    [InlineData(ActorResourceLifecycleAction.EncounterOnly, 9)]
    public void EntryFollowsPolicyForArbitraryResources(ActorResourceLifecycleAction action, float expected)
    {
        var player = Player();
        var actor = Actor(9);
        var result = ActorResourceLifecycleTransitions.Enter(player, actor, Policy(action));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(expected, result.Value.ResourceState.Current("resolve"));
        Assert.Equal(9, actor.ResourceState.Current("resolve"));
        Assert.Equal(37, player.Component<ResourceEntityComponentState>()!.State.Current("resolve"));
    }

    [Theory]
    [InlineData(CombatStatus.VICTORY)]
    [InlineData(CombatStatus.DEFEAT)]
    [InlineData(CombatStatus.DRAW)]
    [InlineData(CombatStatus.ABANDONED)]
    public void ExitUsesTerminalOutcomeAndNeverPromotesTemporaryCapacity(CombatStatus outcome)
    {
        var actor = Actor(9);
        actor = actor.WithResourceState(actor.ResourceState with { Resources = new Dictionary<string, ResourcePool>
            { ["resolve"] = actor.GetResource("resolve")! with { Current = 88, Maximum = 120 } } });
        var result = ActorResourceLifecycleTransitions.Exit(Player(), actor, Policy(), outcome);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var pool = result.Value.Component<ResourceEntityComponentState>()!.State.Get("resolve")!;
        Assert.Equal(60, pool.Current);
        Assert.Equal(60, pool.Maximum);
        Assert.Equal(0, pool.Minimum);
    }

    [Fact]
    public void RetryHasIndependentActionAndMissingPoolIsExplicit()
    {
        var policy = Policy() with { Rules = [Rule() with { Retry = ActorResourceLifecycleAction.EncounterOnly }] };
        Assert.Equal(37, ActorResourceLifecycleTransitions.Exit(Player(), Actor(2), policy, CombatStatus.DEFEAT, true)
            .Value.Component<ResourceEntityComponentState>()!.State.Current("resolve"));
        var missing = Policy() with { Rules = [Rule() with { ResourceId = "not-present" }] };
        Assert.True(ActorResourceLifecycleTransitions.Enter(Player(), Actor(9), missing).IsFailure);
        missing = missing with { Rules = [missing.Rules[0] with { MissingResource = MissingActorResourceBehavior.Ignore }] };
        Assert.True(ActorResourceLifecycleTransitions.Enter(Player(), Actor(9), missing).IsSuccess);
        Assert.True(ActorResourceLifecycleTransitions.Exit(Player(), Actor(9), Policy(), CombatStatus.ACTIVE).IsFailure);
    }

    [Fact]
    public void SchemaRebindPreservesCurrentAndPersistentBoundsAndRejectsOwnerIdentity()
    {
        var player = Player();
        var next = PersistentPlayerTransitions.Rebind(player, Runtime("next"));
        Assert.True(next.IsSuccess, next.IsFailure ? next.Error : null);
        Assert.Equal(37, next.Value.Component<ResourceEntityComponentState>()!.State.Current("resolve"));
        Assert.Equal("next", next.Value.ContentRevision);
        Assert.True(ActorResourceLifecycleTransitions.Enter(player, Actor(3) with { InstanceId = "intruder" }, Policy()).IsFailure);
        Assert.True(ActorResourceLifecycleTransitions.Enter(player, Actor(3) with { ContentRevision = "other" }, Policy()).IsFailure);
        var component = player.Component<ResourceEntityComponentState>()!;
        var bad = player with { Components = new Dictionary<string, EntityComponentState>
            { [component.ComponentId] = component with { State = component.State with { OwnerId = "wallet" } } } };
        Assert.True(PersistentPlayerTransitions.Rebind(bad, Runtime()).IsFailure);
    }

    [Fact]
    public void PolicyRejectsAmbiguousActionsDuplicateIdsAndNonFiniteValues()
    {
        Assert.True(ActorResourceLifecyclePolicyValidator.Validate(Policy() with { Rules = [Rule() with { Retry = ActorResourceLifecycleAction.Unspecified }] }).IsFailure);
        Assert.True(ActorResourceLifecyclePolicyValidator.Validate(Policy() with { Rules = [Rule(), Rule() with { ResourceId = "RESOLVE" }] }).IsFailure);
        Assert.True(ActorResourceLifecyclePolicyValidator.Validate(Policy() with { Rules = [Rule() with { ConfiguredValue = float.NaN }] }).IsFailure);
        Assert.True(ActorResourceLifecyclePolicyValidator.Validate(Policy() with { Rules = [Rule() with { Entry = ActorResourceLifecycleAction.ResetToConfiguredValue, ConfiguredValue = null }] }).IsFailure);
    }

    internal static EntityState Player() => PersistentPlayerTransitions.Create("hero", "operator", Runtime()).Value;
    internal static CombatActorState Actor(float current) => new() { InstanceId = "hero", DefinitionId = "operator", ContentRevision = "resources",
        ResourceState = new() { OwnerId = "hero", Resources = new Dictionary<string, ResourcePool>
            { ["resolve"] = ResourcePool.Materialize(Definition(), current, 60) } } };
    internal static ActorResourceLifecycleRule Rule() => new() { ResourceId = "resolve", Entry = ActorResourceLifecycleAction.PreserveCurrent,
        Victory = ActorResourceLifecycleAction.PreserveCurrent, Defeat = ActorResourceLifecycleAction.PreserveCurrent,
        Draw = ActorResourceLifecycleAction.PreserveCurrent, Abandoned = ActorResourceLifecycleAction.PreserveCurrent,
        Retry = ActorResourceLifecycleAction.PreserveCurrent, ConfiguredValue = 12 };
    internal static ActorResourceLifecyclePolicyDefinition Policy(ActorResourceLifecycleAction entry = ActorResourceLifecycleAction.PreserveCurrent)
        => new() { ActorResourceLifecyclePolicyId = "carry", Rules = [Rule() with { Entry = entry }] };
    internal static ResourceDefinition Definition() => new() { ResourceId = "resolve", DisplayName = "Resolve", DefaultMin = 0, DefaultMax = 60, DefaultCurrent = 37 };
    internal static ContentRuntime Runtime(string revision = "resources")
    {
        var artifacts = new Dictionary<string, JsonElement>
        {
            ["entities/catalog.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, EntityDefinition> { ["operator"] = new()
            { DefinitionId = "operator", DisplayName = "Operator", Components = [new ResourceEntityComponentDefinition { ComponentId = "pools",
                Pools = new Dictionary<string, ResourcePoolDefinition> { ["resolve"] = new() { Current = 37, Max = 60 } } }] } }),
            ["resources/catalog.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, ResourceDefinition> { ["resolve"] = Definition() })
        };
        return ContentRuntime.Create(new() { Manifest = new() { ConfigName = "test", Revision = revision,
            Artifacts = artifacts.Select(pair => new ContentArtifactManifest { Kind = pair.Key.Split('/')[0], Path = pair.Key, DefinitionCount = 1 }).ToArray() },
            Artifacts = artifacts.ToImmutableDictionary() }).Value;
    }
}
