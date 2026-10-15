using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;
using Xunit;

namespace Core.Tests.Effects;

public sealed class RelicResourceRestrictionTests
{
    [Fact]
    public void AcquisitionRestrictsOnlyOwnerAndRemovalDoesNotRestoreDiscardedBalance()
    {
        var run = Initial();
        var before = CanonicalJson.ComputeHash(run);
        var acquired = RelicTransitions.Acquire(run, Definition(), Owner());
        Assert.True(acquired.IsSuccess, acquired.IsFailure ? acquired.Error : null);
        var candidate = acquired.Value.State;
        Assert.Equal(0, PlayerPool(candidate).Current);
        Assert.Equal(20, PlayerPool(candidate).Maximum);
        Assert.Equal(0, PlayerPool(candidate).CurrentCeiling);
        Assert.Equal(0, candidate.Encounters[0].Combat.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(10, candidate.Encounters[0].Combat.GetActor("ally")!.GetResource("focus")!.Current);
        Assert.Equal(10, candidate.Encounters[1].Combat.GetActor("hero")!.GetResource("focus")!.Current);
        var removed = RelicTransitions.Remove(candidate, acquired.Value.Relic.RelicInstanceId).Value;
        Assert.Null(PlayerPool(removed).CurrentCeiling);
        Assert.Equal(0, PlayerPool(removed).Current);
        Assert.Equal(7, PlayerPool(removed).Gain(7).Current);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
    }

    [Fact]
    public void PoolReducerResetRefundOverflowAndLimitChangesCannotBypassCeiling()
    {
        var pool = PlayerPool(RelicTransitions.Acquire(Initial(), Definition(), Owner()).Value.State);
        pool = pool with { Definition = pool.Definition with { CanExceedMax = true } };
        Assert.Equal(0, pool.Gain(1000).Current);
        Assert.Equal(0, pool.Reset().Current);
        Assert.Equal(0, pool.Set(1000).Current);
        var resources = new ResourceSet { OwnerId = "hero", Resources = new Dictionary<string, ResourcePool> { ["focus"] = pool } };
        var changed = resources.Apply([
            new() { MutationId = "maximum", ResourceId = "focus", Field = ResourceValueField.Maximum, Operation = ResourceMutationOperation.Set, Value = 2000 },
            new() { MutationId = "refund", ResourceId = "focus", Operation = ResourceMutationOperation.Add, Value = 1000 },
            new() { MutationId = "set", ResourceId = "focus", Operation = ResourceMutationOperation.Set, Value = 2000 }
        ]);
        Assert.True(changed.IsSuccess, changed.IsFailure ? changed.Error : null);
        Assert.Equal(0, changed.Value.State.Current("focus"));
        Assert.Equal(2000, changed.Value.State.Get("focus")!.Maximum);
        var impossible = changed.Value.State.Apply([new() { MutationId = "minimum", ResourceId = "focus",
            Field = ResourceValueField.Minimum, Operation = ResourceMutationOperation.Set, Value = 2 }]);
        Assert.True(impossible.IsFailure);
        Assert.Equal(0, changed.Value.State.Get("focus")!.Minimum);
        Assert.True(ResourceCostTransitions.Spend(resources, [new() { ResourceId = "focus", Amount = 1 }], "cost").IsFailure);
    }

    [Fact]
    public void EffectsAndRegenerationObserveSameRestrictionsAndRecordZeroGain()
    {
        var run = RelicTransitions.Acquire(Initial(), Definition(), Owner()).Value.State;
        var combat = run.Encounters[0].Combat;
        var effect = EffectTransactionTests.Resource(EffectType.HEAL, 5) with { Target = EffectTarget.SELF };
        var result = EffectTransactionTests.Executor().Execute(EffectTransactionTests.Request(effect) with { Run = run, Combat = combat });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(0, result.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(0, Assert.Single(result.Value.Records).ResourceOutcome!.AppliedChange);
        var regenerated = new CombatResourceLifecycle(EffectTransactionTests.Executor())
            .Process(run, combat, "hero", RegenerationTiming.START_TURN);
        Assert.True(regenerated.IsSuccess, regenerated.IsFailure ? regenerated.Error : null);
        Assert.Equal(0, regenerated.Value.Combat.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Single(regenerated.Value.Records);
    }

    [Fact]
    public void NewEncounterAndMultipleRestrictionsMaterializeWithoutRefillOrRng()
    {
        var run = RelicTransitions.Acquire(Initial(), Definition() with { RelicId = "cap-five", ResourceRestrictions = [Rule(5)] }, Owner()).Value.State;
        var tighter = RelicTransitions.Acquire(run, Definition() with { RelicId = "cap-two", ResourceRestrictions = [Rule(2)] }, Owner()).Value;
        var combat = PersistentPlayerTransitions.Materialize(tighter.State with { PlayerEntity = null }, GameplayOwnershipTests.State());
        Assert.True(combat.IsSuccess, combat.IsFailure ? combat.Error : null);
        Assert.Equal(2, combat.Value.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(GameplayOwnershipTests.State().Determinism.RandomState, combat.Value.Determinism.RandomState);
        var loosened = RelicTransitions.Remove(tighter.State, tighter.Relic.RelicInstanceId).Value;
        Assert.Equal(2, PlayerPool(loosened).Current);
        Assert.Equal(5, PlayerPool(loosened).CurrentCeiling);
        Assert.Equal(5, PlayerPool(loosened).Reset().Current);
    }

    [Fact]
    public void RebindingAndRecoveryPreserveCeilingAndForeignWalletIsNotAnActorPool()
    {
        var run = RelicTransitions.Acquire(Initial(), Definition(), Owner()).Value.State;
        var recovered = JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(run))!;
        Assert.Equal(CanonicalJson.ComputeHash(run), CanonicalJson.ComputeHash(recovered));
        var state = recovered.PlayerEntity!.Component<ResourceEntityComponentState>()!.State;
        var rebound = state.RebindDefinitions(new Dictionary<string, ResourceDefinition> { ["focus"] = PlayerPool(run).Definition with { CanExceedMax = true } });
        Assert.True(rebound.IsSuccess);
        Assert.Equal(0, rebound.Value.Get("focus")!.Reset().Current);
        var wallet = GameplayOwnershipTests.State().GetActor("hero")!;
        wallet = wallet.WithResourceState(wallet.ResourceState with { OwnerId = "wallet" });
        var synthetic = new CombatState { Actors = new Dictionary<string, CombatActorState> { ["hero"] = wallet } };
        Assert.Equal(10, RelicResourceRules.Materialize(run, synthetic).Value.GetActor("hero")!.GetResource("focus")!.Current);
    }

    [Fact]
    public void InvalidRulesRejectWholeAcquisitionAndAbsentFieldsDoNotChangeOldSerialization()
    {
        var run = Initial();
        var before = CanonicalJson.ComputeHash(run);
        Assert.True(RelicTransitions.Acquire(run, Definition() with { ResourceRestrictions = [Rule(0) with { ExistingBalance = null }] }, Owner()).IsFailure);
        Assert.True(RelicTransitions.Acquire(run, Definition() with { ResourceRestrictions = [Rule(-1)] }, Owner()).IsFailure);
        Assert.True(RelicResourceRules.Validate([Rule(0), Rule(1)]).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.DoesNotContain("CurrentCeiling", JsonSerializer.Serialize(PlayerPool(run)));
        Assert.DoesNotContain("ResourceRestrictions", JsonSerializer.Serialize(new RelicDefinition()));
    }

    private static GameplayOwner Owner() => new() { Kind = GameplayOwnerKind.Entity, Id = "hero" };
    private static ResourceRestrictionDefinition Rule(float ceiling) => new()
    { RuleId = "no-focus", ResourceId = "focus", CurrentCeiling = ceiling, ExistingBalance = ResourceRestrictedBalancePolicy.DiscardExcess };
    private static RelicDefinition Definition() => new() { RelicId = "restriction", ResourceRestrictions = [Rule(0)] };
    private static ResourcePool PlayerPool(RunState run) => run.PlayerEntity!.Component<ResourceEntityComponentState>()!.State.Get("focus")!;
    private static RunState Initial()
    {
        var combat = GameplayOwnershipTests.State();
        var hero = combat.GetActor("hero")!;
        var pool = hero.GetResource("focus")!;
        hero = hero.WithResourceState(hero.ResourceState with { Resources = new Dictionary<string, ResourcePool>
        { ["focus"] = pool with { Definition = pool.Definition with { Regeneration = new()
            { Enabled = true, AmountPerTurn = 5, Timing = RegenerationTiming.START_TURN } } } } });
        combat = combat.ReplaceActor(hero);
        return new() { RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), PlayerEntityId = "hero",
            PlayerEntity = new() { InstanceId = "hero", Components = hero.Components },
            Determinism = DeterministicContext.Create(7, "revision"),
            Encounters = [new() { Combat = combat }, new() { Combat = combat, Resolved = true }] };
    }
}
