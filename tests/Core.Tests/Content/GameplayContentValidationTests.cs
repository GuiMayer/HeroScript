using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Content;
using Core.Effects;
using Core.Math;
using Core.StatusEffects;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.Content;

public sealed class GameplayContentValidationTests
{
    [Theory]
    [InlineData(EffectType.COPY_EFFECT)]
    [InlineData(EffectType.MODIFY_CRIT_CHANCE)]
    [InlineData(EffectType.SKIP_TURN)]
    [InlineData((EffectType)999)]
    public void UnsupportedChildIsRejectedEvenWhenParentNeverExecutes(EffectType type)
    {
        var parent = EffectTransactionTests.Resource(EffectType.DAMAGE, 1) with
            { Chance = 0, ChainedEffects = [new() { Type = type }] };
        var result = EffectTransactionTests.Executor().Execute(EffectTransactionTests.Request(parent));
        Assert.True(result.IsFailure);
        Assert.Contains("no executable runtime", result.Error);
        var published = Validate(("actions", "test", new ActionDefinition { Effects = [parent] }));
        Assert.Contains(published.Errors, error => error.Contains("chainedEffects[0]") && error.Contains("no executable runtime"));
    }

    [Fact]
    public void RecursiveReferencesAndNumericPoliciesAreCheckedWithoutExecuting()
    {
        var result = Validate(("actions", "test", new ActionDefinition { Effects = [new()
        {
            Type = EffectType.APPLY_MODIFIER, ModifierId = "missing", Chance = 0,
            ModifierOwner = new() { Kind = GameplayOwnerKind.Side }, ChainedEffects = [new()
            {
                Type = EffectType.DRAW_CARD, CardCount = -1, Timing = EffectTiming.DELAYED
            }]
        }] }));
        Assert.Contains(result.Errors, error => error.Contains("modifiers/missing"));
        Assert.Contains(result.Errors, error => error.Contains("invalid modifier owner"));
        Assert.Contains(result.Errors, error => error.Contains("invalid cardCount"));
        Assert.Contains(result.Errors, error => error.Contains("only IMMEDIATE"));
    }

    [Theory]
    [InlineData("missing_formula")]
    [InlineData("actor.health + 1")]
    [InlineData("stacks +")]
    [InlineData("stacks > 1")]
    [InlineData("stacks + Infinity")]
    public void InvalidFormulaGrammarOrNamespaceIsRejected(string formula)
    {
        var result = Validate(("actions", "test", new ActionDefinition { Effects = [new()
            { Type = EffectType.DRAW_CARD, Condition = formula }] }));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidNamedAndInlineFormulasDoNotRequireInventedGameplayValues()
    {
        var result = Validate(("formulas", "scaled", new FormulaDefinition
        {
            Params = new() { ["stacks"] = 1 }, Operations = [new() { Op = "MULTIPLY", Value = "params.stacks" }]
        }), ("actions", "test", new ActionDefinition { Effects = [new()
            { Type = EffectType.DRAW_CARD, Condition = "scaled", ChainedEffects = [new()
                { Type = EffectType.DRAW_CARD, Condition = "stacks / duration" }] }] }));
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void NamedFormulaRejectsUnsupportedOperationsAndMissingParameters()
    {
        var result = Validate(("formulas", "broken", new FormulaDefinition
        {
            Operations = [new() { Op = "RANDOM", Value = "1" }, new() { Op = "SET", Value = "params.missing" }]
        }));
        Assert.Contains(result.Errors, error => error.Contains("unsupported operation: RANDOM"));
        Assert.Contains(result.Errors, error => error.Contains("params.missing"));
    }

    [Fact]
    public void InstancePoliciesTriggersAndInfluenceDestinationsAreValidated()
    {
        var result = Validate(("modifiers", "broken", new ScriptModifierDefinition
        {
            MaxStacks = 0, DurationBoundary = (ModifierDurationBoundary)999,
            Influences = [new() { InfluenceId = "x", Channel = "damage", Bucket = "missing", Value = 1 }]
        }), ("status-effects", "broken", new StatusEffectDefinition
        {
            Stacking = (StackReapplyPolicy)999,
            Triggers = [new() { TriggerId = "x", Boundary = "OnHit", Effects = [new() { Type = EffectType.DRAW_CARD }] }]
        }));
        Assert.Contains(result.Errors, error => error.Contains("invalid default/max stacks"));
        Assert.Contains(result.Errors, error => error.Contains("invalid modifier durationBoundary"));
        Assert.Contains(result.Errors, error => error.Contains("no calculation pipeline"));
        Assert.Contains(result.Errors, error => error.Contains("invalid stacking"));
        Assert.Contains(result.Errors, error => error.Contains("OnHit") && error.Contains("no executable lifecycle"));
    }

    [Fact]
    public void MalformedCardComponentReturnsDiagnosticsRatherThanThrowing()
    {
        var result = Validate(("cards", "broken", new { components = new[] { new { type = "unknown" } } }));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("cards/broken"));
    }

    [Fact]
    public void RelationshipValidationAllowsDirectedRulesButRejectsAmbiguousOnes()
    {
        var rule = new SideRelationshipRule { FromSideId = "a", ToSideId = "b", Relationship = SideRelationship.Neutral };
        Assert.True(GameplayRelationshipValidator.Validate(["a", "b"], [], new() { Rules = [rule] }).IsSuccess);
        Assert.True(GameplayRelationshipValidator.Validate(["a", "b"], [], new() { Rules = [rule, rule] }).IsFailure);
        Assert.True(GameplayRelationshipValidator.Validate(["a", "b"], [new() { SideId = "a" }], new()).IsFailure);
        Assert.True(GameplayRelationshipValidator.Validate(["a"], [], new() { Rules = [rule] }).IsFailure);
    }

    private static ContentGraphValidationResult Validate(params (string Kind, string Id, object Definition)[] definitions)
    {
        var artifacts = definitions.GroupBy(item => item.Kind).ToDictionary(group => $"{group.Key}/catalog.json",
            group => JsonSerializer.SerializeToElement(group.ToDictionary(item => item.Id, item => item.Definition)));
        return new ContentGraphValidator().Validate(new()
        {
            Manifest = new() { Revision = "revision", ConfigName = "default", Artifacts = artifacts.Select(item => new ContentArtifactManifest
                { Path = item.Key, Kind = item.Key.Split('/')[0], DefinitionCount = item.Value.EnumerateObject().Count() }).ToArray() },
            Artifacts = artifacts.ToImmutableDictionary()
        });
    }
}
