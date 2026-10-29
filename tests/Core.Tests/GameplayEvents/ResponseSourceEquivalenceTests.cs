using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class ResponseSourceEquivalenceTests
{
    [Fact]
    public void EquivalentResponseEffectsFromActorStatusRelicAndModeHaveTheSameNumericAndRandomSemantics()
    {
        var outputs = new HashSet<string>();
        foreach (var source in new[] { "entity", "status", "relic", "mode" })
        {
            var fixture = EffectSequenceBudgetTests.Fixture(); var run = fixture.Run;
            var response = GameplayResponseCompilationTests.Definition() with { Bindings = new() {
                Owner = new() { Kind = ResponseActorBindingKind.ExplicitActor, ActorId = "hero" }, Target = new() { Kind = ResponseActorBindingKind.Owner } },
                Effects = [EffectSequenceBudgetTests.Damage(2, 1) with { Type = EffectType.HEAL, Target = EffectTarget.SELF, Chance = .85f }] };
            var combat = EffectTransactionTests.Request().Combat;
            combat = CombatTransitions.Create(combat.GetAllActors().Select(actor => actor with { ContentRevision = "revision" }), combat.Determinism);
            if (source == "entity")
            {
                var hero = combat.GetActor("hero")!;
                combat = combat.ReplaceActor(hero with { Components = hero.Components.ToImmutableDictionary(StringComparer.Ordinal).Add("watch",
                    new EntityResponseComponentState { ComponentId = "watch", Responses = [response] }) });
            }
            else if (source == "status") combat = combat with { StatusEffects = combat.StatusEffects.Add("hero", [new() {
                InstanceId = Guid.Parse("ca458e61-e985-4fa4-a6dc-862ac917af14"), StatusId = "watch", TargetId = "hero", SourceId = "hero",
                ContentRevision = "revision", Stacks = 1, Duration = -1, Definition = new() { StatusId = "watch", Responses = [response] } }]) };
            else if (source == "relic")
            {
                var acquired = RelicTransitions.Acquire(run, new() { RelicId = "watch", Responses = [response] },
                    new() { Kind = GameplayOwnerKind.Entity, Id = "hero" });
                Assert.True(acquired.IsSuccess, acquired.IsFailure ? acquired.Error : null); run = acquired.Value.State;
            }
            else run = run with { ResolvedMode = run.ResolvedMode! with { Definition = run.ResolvedMode.Definition with { ModeId = "watch", Responses = [response] } } };
            var request = EffectTransactionTests.Request(EffectSequenceBudgetTests.Damage(1, 1)) with { Run = run, Combat = combat };
            var input = CanonicalJson.ComputeHash(request);
            var repeated = new HashSet<string>();
            for (var i = 0; i < 5; i++)
            {
                var result = fixture.Executor.Execute(request);
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
                Assert.Equal(2, result.Value.Steps.Length);
                Assert.Equal(9, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
                repeated.Add(CanonicalJson.ComputeHash(result.Value));
                outputs.Add(CanonicalJson.ComputeHash(new { resources = result.Value.State.GetAllActors().OrderBy(actor => actor.InstanceId)
                    .Select(actor => new { actor.InstanceId, actor.ResourceState }), result.Value.State.Determinism,
                    values = result.Value.Calculations.Select(calculation => calculation.Value) }));
            }
            Assert.Single(repeated); Assert.Equal(input, CanonicalJson.ComputeHash(request));
        }
        Assert.Single(outputs);
    }

    [Fact]
    public void UniversalEffectProcessorDoesNotSelectNumericRulesFromTheContainerProvenance()
    {
        var fixture = EffectSequenceBudgetTests.Fixture(); var signatures = new HashSet<string>();
        var effect = EffectSequenceBudgetTests.Damage(6, 3) with { Chance = .75f };
        foreach (var kind in new[] { EffectProvenanceKind.Card, EffectProvenanceKind.Status, EffectProvenanceKind.Relic, EffectProvenanceKind.Rule })
        {
            var request = EffectTransactionTests.Request(effect) with { Run = fixture.Run,
                Provenance = new() { Kind = kind, SourceId = "same-captured-definition" } };
            var result = fixture.Executor.Execute(request);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            signatures.Add(CanonicalJson.ComputeHash(new { result.Value.State, result.Value.State.Determinism,
                numbers = result.Value.Calculations.Select(calculation => calculation.Value),
                rolls = result.Value.Steps.Select(step => step.ChanceRoll) }));
        }
        Assert.Single(signatures);
    }
}
