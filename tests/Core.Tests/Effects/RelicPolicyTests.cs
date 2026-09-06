using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Xunit;

namespace Core.Tests.Effects;

public sealed class RelicPolicyTests
{
    [Fact]
    public void AcquisitionPinsOwnerRevisionAndDefinition()
    {
        var run = new RunState { RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), PlayerEntityId = "hero",
            Determinism = DeterministicContext.Create(123, "revision") };
        var owner = new GameplayOwner { Kind = GameplayOwnerKind.Entity, Id = "enemy" };
        var definition = Definition();
        var first = RelicTransitions.Acquire(run, definition, owner);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(owner, first.Value.Relic.Owner);
        Assert.Equal("revision", first.Value.Relic.ContentRevision);
        Assert.Equal(definition, first.Value.Relic.Definition);
        Assert.Empty(run.Relics);
    }

    [Fact]
    public void CombatEndExecutesForConfiguredOwnerExactlyOnce()
    {
        var run = new RunState { Determinism = DeterministicContext.Create(123, "revision") };
        run = RelicTransitions.Acquire(run, Definition(), new() { Kind = GameplayOwnerKind.Entity, Id = "enemy" }).Value.State;
        var state = GameplayOwnershipTests.State() with { Status = CombatStatus.VICTORY };
        var lifecycle = new CombatRelicLifecycle(EffectTransactionTests.Executor());
        var first = lifecycle.Process(run, state, CombatTriggerBoundaries.CombatEnd);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(7, first.Value.Combat.GetEntity("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(10, first.Value.Combat.Hero.GetResource("focus")!.Current);
        Assert.Single(first.Value.Events);
        var repeated = lifecycle.Process(run, first.Value.Combat, CombatTriggerBoundaries.CombatEnd);
        Assert.Empty(repeated.Value.Events);
        Assert.Equal(CanonicalJson.ComputeHash(first.Value.Combat), CanonicalJson.ComputeHash(repeated.Value.Combat));
    }

    [Fact]
    public void SideRelicBindsEachOwnerInDeterministicOrder()
    {
        var run = RelicTransitions.Acquire(new RunState { Determinism = DeterministicContext.Create(123, "revision") },
            Definition(), new() { Kind = GameplayOwnerKind.Side, Id = "blue" }).Value.State;
        var result = new CombatRelicLifecycle(EffectTransactionTests.Executor()).Process(run, GameplayOwnershipTests.State(), "CombatEnd");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["ally", "hero"], result.Value.Events.Select(item => item.Applications[0].TargetEntityId));
    }

    private static RelicDefinition Definition() => new()
    {
        RelicId = "test", Triggers = [new() { TriggerId = "end", Boundary = "CombatEnd",
            Effects = [new() { Type = EffectType.DAMAGE, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 3 }] }]
    };
}
