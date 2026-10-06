using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class EffectiveCardResolverTests
{
    private readonly CardContentCompiler _compiler = new();
    private readonly EffectiveCardResolver _resolver = new();

    [Fact]
    public void Resolve_AppliesTypedUpgradeWithoutMutatingDefinitionBase()
    {
        var definition = CompileStrike();
        var instance = CreateInstance(
            new CardEffectNumericPatchDefinition
            {
                ComponentId = "effect.damage",
                Attribute = CardEffectNumericAttribute.FlatValue,
                Operation = CardNumericPatchOperation.Add,
                Value = 3
            });

        var first = _resolver.Resolve(definition, instance);
        var second = _resolver.Resolve(definition, instance);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(6, definition.All<CardEffectComponentDefinition>()[0].Effect.FlatValue);
        Assert.Equal(9, first.Value.All<CardEffectComponentDefinition>()[0].Effect.FlatValue);
        Assert.Equal(first.Value.Fingerprint, second.Value.Fingerprint);
        Assert.NotEqual(definition.Fingerprint, first.Value.Fingerprint);
        var trace = Assert.Single(first.Value.UpgradeTrace);
        Assert.Equal("sharpened", trace.UpgradeId);
        Assert.Equal(6, trace.PreviousValue);
        Assert.Equal(9, trace.CurrentValue);
    }

    [Fact]
    public void Resolve_UsesStoredUpgradeOrderDeterministically()
    {
        var definition = CompileStrike();
        var instance = new CardInstanceState
        {
            CardInstanceId = Guid.Parse("10000000-0000-8000-8000-000000000001"),
            DefinitionId = "strike",
            Upgrades =
            [
                Upgrade("add", CardNumericPatchOperation.Add, 2),
                Upgrade("multiply", CardNumericPatchOperation.Multiply, 2) with { TransformationId = 2 }
            ]
        };

        var result = _resolver.Resolve(definition, instance);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(16, result.Value.All<CardEffectComponentDefinition>()[0].Effect.FlatValue);
    }

    [Fact]
    public void ValidateUpgrade_RejectsPatchAddressedToWrongComponentType()
    {
        var definition = CompileStrike();
        var upgrade = new CardUpgradeDefinition
        {
            UpgradeId = "invalid",
            CardDefinitionIds = ["strike"],
            Patches =
            [
                new CardCostAmountPatchDefinition
                {
                    ComponentId = "effect.damage",
                    ResourceId = "energy",
                    Value = -1
                }
            ]
        };

        var result = _resolver.ValidateUpgrade(definition, upgrade);

        Assert.True(result.IsFailure);
        Assert.Contains("expected cost", result.Error);
    }

    [Fact]
    public void AuthoredUpgradeJson_DeserializesTypedPatch()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());

        var upgrade = JsonSerializer.Deserialize<CardUpgradeDefinition>(
            """
            {
              "upgradeId": "sharpened",
              "cardDefinitionIds": ["strike"],
              "patches": [
                {
                  "type": "effect_numeric",
                  "componentId": "effect.damage",
                  "attribute": "FlatValue",
                  "operation": "Add",
                  "value": 3
                }
              ]
            }
            """,
            options);

        var patch = Assert.IsType<CardEffectNumericPatchDefinition>(Assert.Single(upgrade!.Patches));
        Assert.Equal(CardEffectNumericAttribute.FlatValue, patch.Attribute);
        Assert.Equal(CardNumericPatchOperation.Add, patch.Operation);
    }

    [Theory]
    [InlineData(CardEffectNumericAttribute.Repeat, 257f)]
    [InlineData(CardEffectNumericAttribute.Repeat, float.MaxValue)]
    [InlineData(CardEffectNumericAttribute.Chance, float.NaN)]
    [InlineData(CardEffectNumericAttribute.FlatValue, -1f)]
    [InlineData((CardEffectNumericAttribute)999, 1f)]
    public void Resolve_RejectsUpgradeOutsideExecutableEffectContract(CardEffectNumericAttribute attribute, float value)
    {
        var result = _resolver.Resolve(CompileStrike(), CreateInstance(new CardEffectNumericPatchDefinition
        {
            ComponentId = "effect.damage", Attribute = attribute, Operation = CardNumericPatchOperation.Set, Value = value
        }));
        Assert.True(result.IsFailure);
    }

    private CompiledCardDefinition CompileStrike()
    {
        var result = _compiler.Compile(new CardContentDefinition
        {
            CardId = "strike",
            Components =
            [
                new CardCostComponentDefinition
                {
                    ComponentId = "cost.energy",
                    Costs = new ActionCosts
                    {
                        Costs = [new ResourceCost { ResourceId = "energy", Amount = 1 }]
                    }
                },
                new CardEffectComponentDefinition
                {
                    ComponentId = "effect.damage",
                    Effect = new EffectDefinition
                    {
                        Type = EffectType.DAMAGE,
                        FlatValue = 6,
                        TargetResource = "health"
                    }
                }
            ]
        });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private static CardInstanceState CreateInstance(CardUpgradePatchDefinition patch) => new()
    {
        CardInstanceId = Guid.Parse("10000000-0000-8000-8000-000000000001"),
        DefinitionId = "strike",
        Upgrades = [new CardUpgradeState { TransformationId = 1, ContentRevision = "content", UpgradeId = "sharpened", Patches = [patch] }]
    };

    private static CardUpgradeState Upgrade(
        string id,
        CardNumericPatchOperation operation,
        float value) => new()
    {
        UpgradeId = id,
        TransformationId = 1, ContentRevision = "content",
        Patches =
        [
            new CardEffectNumericPatchDefinition
            {
                ComponentId = "effect.damage",
                Attribute = CardEffectNumericAttribute.FlatValue,
                Operation = operation,
                Value = value
            }
        ]
    };
}
