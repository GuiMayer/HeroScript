using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Entity;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Tests.Effects;
using Core.Combat;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class NormalGameplayResponseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalAndObservedPathsShareActorBuffAndCostResponsesWithoutInventingDamage(bool attribute)
    {
        var context = GameplayResponseCompilationTests.Context();
        var definition = GameplayResponseCompilationTests.Definition() with
        {
            Observe = attribute ? new() { Kinds = new() { Include = ["attribute.change.resolved"] }, Subject = new() { RelationshipToOwner = SideRelationship.Ally } }
                : new() { Kinds = new() { Include = ["resource.change.resolved"] }, Reasons = new() { Include = ["cost.payment"] },
                    Resource = new() { ResourceIds = ["focus"], Direction = ResourceChangeDirection.Decrease } },
            Effects = [new() { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 2 }]
        };
        var observer = context.Combat!.GetActor("ally")!;
        observer = observer with { Components = observer.Components.ToImmutableDictionary(StringComparer.Ordinal).Add("reactions",
            new EntityResponseComponentState { ComponentId = "reactions", Responses = [definition] }) };
        var hero = context.Combat.GetActor("hero")!;
        hero = hero with { Components = hero.Components.ToImmutableDictionary(StringComparer.Ordinal).Add("stats", new StatEntityComponentState
            { ComponentId = "stats", Values = new Dictionary<string, float> { ["power"] = 3 },
                ValueRules = ImmutableSortedDictionary<string, AttributeValueRule>.Empty.Add("power", new() { AllowedOperations = [AttributeOperation.Add] }) }) };
        var combat = CombatTransitions.Create(context.Combat.GetAllActors().Select(actor => (actor.InstanceId == "ally" ? observer
            : actor.InstanceId == "hero" ? hero : actor) with { ContentRevision = "revision" }), context.Combat.Determinism);
        var request = EffectTransactionTests.Request() with { Run = context.Run, Combat = combat, SelectedTargetEntityIds = ["hero"] };
        if (attribute) request = request with { Trigger = request.Trigger with { Effects = [new() { Type = EffectType.MODIFY_ATTRIBUTE,
            Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = 2, Channel = "attributes", UnitId = "scalar" }],
            AttributeMutation = new() { ComponentId = "stats", ValueId = "power", Operation = AttributeOperation.Add, Lifetime = AttributeLifetime.Encounter } }] } };
        else request = request with { PrefixCommands = [new() { EffectInstanceId = "payment", SourceEntityId = "hero", TargetEntityIds = ["hero"],
            ContentRevision = "revision", SemanticReasonId = "cost.payment", ResolvedValue = 2, Definition = new() { Type = EffectType.MODIFY_RESOURCE,
                TargetResource = "focus", Operation = ResourceEffectOperation.SUBTRACT, FlatValue = 2 } }] };
        var original = CanonicalJson.ComputeHash(request);
        var executor = EffectTransactionTests.Executor();
        var normal = executor.Execute(request);
        Assert.True(normal.IsSuccess, normal.IsFailure ? normal.Error : null);
        var observed = executor.ExecuteObserved(request, GameplayObservedExecutorTests.ObservationFor(context.Run, combat));
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(normal.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
        Assert.Equal(12, normal.Value.State.GetActor("ally")!.GetResource("focus")!.Current);
        Assert.Equal(original, CanonicalJson.ComputeHash(request));
        var facts = observed.Value.Observation.Events.Occurrences;
        Assert.Equal(attribute ? "attribute.change.resolved" : "resource.change.resolved", facts[0].Kind);
        Assert.Equal(attribute ? "effect.application" : "cost.payment", facts[0].ReasonId);
        Assert.DoesNotContain(facts, fact => fact.Kind.Contains("damage", StringComparison.Ordinal));
        Assert.Equal(facts[0].EventId, facts[1].ParentEventId);
    }
}
