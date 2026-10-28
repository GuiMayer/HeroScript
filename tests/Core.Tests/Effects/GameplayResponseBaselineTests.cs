using Core.Common;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;
using Xunit;

namespace Core.Tests.Effects;

/// <summary>Migration contract: externally observable order and immutable successors, not a new dispatcher.</summary>
public sealed class GameplayResponseBaselineTests
{
    [Fact]
    public void ResourceResponsesKeepAscendingPriorityAndRunBeforeEachChainedChild()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var run = fixture.Run;
        foreach (var (id, priority, gain) in new[] { ("high", 100, 2f), ("low", -100, 1f) })
        {
            var acquired = RelicTransitions.Acquire(run, new()
            {
                RelicId = id,
                Reactions = [new()
                {
                    ReactionId = id, Priority = priority,
                    OwnerScope = EffectReactionOwnerScope.Source,
                    ResourceId = "focus", ResourceField = ResourceValueField.Current,
                    Operation = ResourceMutationOperation.Subtract, Channels = ["magnitude"],
                    Effects = [EffectSequenceBudgetTests.Damage(gain, 1) with { Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF, Operation = ResourceEffectOperation.ADD }]
                }]
            }, new() { Kind = GameplayOwnerKind.Entity, Id = "hero" });
            Assert.True(acquired.IsSuccess, acquired.IsFailure ? acquired.Error : null);
            run = acquired.Value.State;
        }
        var damage = EffectSequenceBudgetTests.Damage(1, 1);
        var request = EffectTransactionTests.Request(damage with { ChainedEffects = [damage] }) with { Run = run };
        var before = CanonicalJson.ComputeHash(request);
        var results = Enumerable.Range(0, 10).Select(_ => fixture.Executor.Execute(request)).ToArray();
        foreach (var result in results)
        {
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(8, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
            Assert.Equal(16, result.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
            Assert.Equal(new[] { "low", "high", "low", "high" }, result.Value.Steps
                .Where(step => step.Provenance.Kind == EffectProvenanceKind.Relic).Select(step => step.Provenance.ComponentId));
            Assert.Equal(new[] { EffectProvenanceKind.Ability, EffectProvenanceKind.Relic, EffectProvenanceKind.Relic,
                EffectProvenanceKind.Ability, EffectProvenanceKind.Relic, EffectProvenanceKind.Relic },
                result.Value.Steps.Select(step => step.Provenance.Kind));
            Assert.Equal(request.Combat.Determinism.RandomState, result.Value.State.Determinism.RandomState);
        }
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct(StringComparer.Ordinal));
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void NoObserverPreservesKnownResourcesRngAndInputSnapshots()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var request = EffectTransactionTests.Request(EffectSequenceBudgetTests.Damage(6, 3)) with { Run = fixture.Run };
        var before = CanonicalJson.ComputeHash(request);
        var results = Enumerable.Range(0, 10).Select(_ => fixture.Executor.Execute(request)).ToArray();
        foreach (var result in results)
        {
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(4, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
            Assert.Equal(10, result.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
            Assert.Equal(3, result.Value.Steps.Length);
            Assert.Equal(request.Combat.Determinism.RandomState, result.Value.State.Determinism.RandomState);
        }
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct(StringComparer.Ordinal));
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }
}
