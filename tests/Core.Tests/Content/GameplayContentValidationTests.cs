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
    [Fact]
    public void UnsupportedChildIsRejectedEvenWhenParentNeverExecutes()
    {
        var parent = EffectTransactionTests.Resource(EffectType.DAMAGE, 1) with
            { Chance = 0, ChainedEffects = [new() { Type = (EffectType)999 }] };
        var result = EffectTransactionTests.Executor().Execute(EffectTransactionTests.Request(parent));
        Assert.True(result.IsFailure);
        Assert.Contains("no executable runtime", result.Error);
        var published = Validate(("actions", "test", new ActionDefinition { ActionId = "test", Effects = [parent] }));
        Assert.Contains(published.Errors, error => error.Contains("chainedEffects[0]") && error.Contains("no executable runtime"));
    }

    [Fact]
    public void RecursiveReferencesAndNumericPoliciesAreCheckedWithoutExecuting()
    {
        var result = Validate(("actions", "test", new ActionDefinition { ActionId = "test", Effects = [new()
        {
            Type = EffectType.APPLY_MODIFIER, ModifierId = "missing", Chance = 0,
            ModifierOwner = new() { Kind = GameplayOwnerKind.Side }, ChainedEffects = [new()
            {
                Type = EffectType.CARD_ZONE_FLOW, CardZoneFlowId = "missing", CardCount = -1
            }]
        }] }));
        Assert.Contains(result.Errors, error => error.Contains("modifiers/missing"));
        Assert.Contains(result.Errors, error => error.Contains("invalid modifier owner"));
        Assert.Contains(result.Errors, error => error.Contains("invalid cardCount"));
    }

    [Theory]
    [InlineData("missing_formula")]
    [InlineData("actor.health + 1")]
    [InlineData("stacks +")]
    [InlineData("stacks > 1")]
    [InlineData("stacks + Infinity")]
    public void InvalidFormulaGrammarOrNamespaceIsRejected(string formula)
    {
        var result = Validate(("actions", "test", new ActionDefinition { ActionId = "test", Effects = [new()
            { Type = EffectType.CARD_ZONE_FLOW, CardZoneFlowId = "missing", Condition = formula }] }));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidNamedAndInlineFormulasDoNotRequireInventedGameplayValues()
    {
        var result = Validate(("formulas", "scaled", new FormulaDefinition
        {
            Params = new() { ["stacks"] = 1 }, Operations = [new() { Op = "MULTIPLY", Value = "params.stacks" }]
        }), ("modifiers", "formula-owner", new ScriptModifierDefinition { ModifierId = "formula-owner" }),
            ("actions", "test", new ActionDefinition { ActionId = "test", Effects = [new()
            { Type = EffectType.REMOVE_MODIFIER, ModifierId = "formula-owner", Condition = "scaled", ChainedEffects = [new()
                { Type = EffectType.REMOVE_MODIFIER, ModifierId = "formula-owner", Condition = "stacks / duration" }] }] }));
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
            ModifierId = "broken",
            MaxStacks = 0, DurationBoundary = (ModifierDurationBoundary)999,
            Influences = [new() { InfluenceId = "x", Channel = "damage", Bucket = "missing", Value = 1 }]
        }), ("status-effects", "broken", new StatusEffectDefinition
        {
            StatusId = "broken",
            Stacking = (StackReapplyPolicy)999,
            Triggers = [new() { TriggerId = "x", Boundary = "OnHit", Effects = [new()
                { Type = EffectType.CARD_ZONE_FLOW, CardZoneFlowId = "missing" }] }]
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

    [Theory]
    [InlineData("status-effects", "{\"legacy\":{\"statusId\":\"legacy\",\"type\":\"BURNING\"}}")]
    [InlineData("modifiers", "{\"legacy\":{\"modifierId\":\"legacy\",\"modifierKey\":\"damage\"}}")]
    [InlineData("actions", "{\"legacy\":{\"actionId\":\"legacy\",\"effects\":[{\"type\":\"DAMAGE\",\"timing\":\"IMMEDIATE\"}]}}")]
    [InlineData("actions", "{\"legacy\":{\"actionId\":\"legacy\",\"effects\":[{\"type\":\"DRAW_CARD\"}]}}")]
    [InlineData("actions", "{\"legacy\":{\"actionId\":\"legacy\",\"effects\":[{\"type\":\"CARD_ZONE_FLOW\",\"cardZoneFlowId\":\"x\",\"shuffleDiscardWhenEmpty\":true}]}}")]
    public void RemovedLegacyFieldsAreRejectedInsteadOfSilentlyIgnored(string kind, string json)
    {
        var result = ValidateRaw(kind, json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("could not", StringComparison.Ordinal));
    }

    [Fact]
    public void RelationshipValidationAllowsDirectedRulesButRejectsAmbiguousOnes()
    {
        var rule = new SideRelationshipRule { FromSideId = "a", ToSideId = "b", Relationship = SideRelationship.Neutral };
        var sides = new[]
        {
            new CombatSide { SideId = "a" },
            new CombatSide { SideId = "b" }
        };
        Assert.True(GameplayRelationshipValidator.Validate(["a", "b"], sides, new() { Rules = [rule] }).IsSuccess);
        Assert.True(GameplayRelationshipValidator.Validate(["a", "b"], sides, new() { Rules = [rule, rule] }).IsFailure);
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

    private static ContentGraphValidationResult ValidateRaw(string kind, string json)
    {
        using var document = JsonDocument.Parse(json);
        var path = $"{kind}/catalog.json";
        var artifact = document.RootElement.Clone();
        return new ContentGraphValidator().Validate(new()
        {
            Manifest = new()
            {
                Revision = "revision",
                ConfigName = "default",
                Artifacts =
                [
                    new ContentArtifactManifest
                    {
                        Path = path,
                        Kind = kind,
                        DefinitionCount = artifact.EnumerateObject().Count()
                    }
                ]
            },
            Artifacts = new Dictionary<string, JsonElement> { [path] = artifact }.ToImmutableDictionary()
        });
    }
}
