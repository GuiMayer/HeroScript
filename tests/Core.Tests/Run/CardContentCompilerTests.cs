using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardContentCompilerTests
{
    private readonly CardContentCompiler _compiler = new();

    [Fact]
    public void Compile_ExpandsBundlesAndOrdersComponentsDeterministically()
    {
        var card = new CardContentDefinition
        {
            CardId = "strike",
            Tags = ["physical", "attack"],
            ComponentBundleIds = ["standard_play"],
            Components =
            [
                new CardEffectComponentDefinition
                {
                    ComponentId = "effect.damage",
                    Order = 20,
                    Effect = new EffectDefinition
                    {
                        EffectId = "strike.damage",
                        Type = EffectType.DAMAGE,
                        FlatValue = 6,
                        TargetResource = "health"
                    }
                }
            ]
        };
        var bundle = new CardComponentBundleDefinition
        {
            BundleId = "standard_play",
            Components =
            [
                new CardDispositionComponentDefinition
                {
                    ComponentId = "disposition.default",
                    Order = 90,
                    Destination = CardConsumeDestination.Discard
                },
                new CardCostComponentDefinition
                {
                    ComponentId = "cost.energy",
                    Order = 10,
                    Costs = new ActionCosts
                    {
                        Costs = [new ResourceCost { ResourceId = "energy", Amount = 1 }]
                    }
                }
            ]
        };

        var first = _compiler.Compile(card, new Dictionary<string, CardComponentBundleDefinition>
        {
            ["standard_play"] = bundle
        });
        var second = _compiler.Compile(card, new Dictionary<string, CardComponentBundleDefinition>
        {
            ["standard_play"] = bundle
        });

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(
            ["cost.energy", "effect.damage", "disposition.default"],
            first.Value.Components.Select(component => component.ComponentId));
        Assert.Equal(
            first.Value.Components.Select(component => component.ComponentId),
            second.Value.Components.Select(component => component.ComponentId));
        Assert.Equal(first.Value.Fingerprint, second.Value.Fingerprint);
        Assert.Equal(64, first.Value.Fingerprint.Length);
    }

    [Fact]
    public void Compile_RejectsDuplicateStableComponentAddresses()
    {
        var card = new CardContentDefinition
        {
            CardId = "invalid",
            Components =
            [
                new CardConditionComponentDefinition
                {
                    ComponentId = "rule.same",
                    Expression = "1"
                },
                new CardDispositionComponentDefinition
                {
                    ComponentId = "rule.same"
                }
            ]
        };

        var result = _compiler.Compile(card);

        Assert.True(result.IsFailure);
        Assert.Contains("duplicate component id rule.same", result.Error);
    }

    [Fact]
    public void Compile_RejectsResourceEffectWithoutExplicitTargetResource()
    {
        var card = new CardContentDefinition
        {
            CardId = "invalid",
            Components =
            [
                new CardEffectComponentDefinition
                {
                    ComponentId = "effect.damage",
                    Effect = new EffectDefinition { Type = EffectType.DAMAGE, FlatValue = 5 }
                }
            ]
        };

        var result = _compiler.Compile(card);

        Assert.True(result.IsFailure);
        Assert.Contains("requires targetResource", result.Error);
    }

    [Fact]
    public void AuthoredJson_DeserializesIntoTypedComponents()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var card = JsonSerializer.Deserialize<CardContentDefinition>(
            """
            {
              "cardId": "mana_burn",
              "components": [
                {
                  "type": "effect",
                  "componentId": "effect.mana_burn",
                  "order": 20,
                  "effect": {
                    "effectId": "mana_burn.reduce",
                    "type": "DAMAGE",
                    "targetResource": "mana",
                    "flatValue": 4
                  }
                },
                {
                  "type": "disposition",
                  "componentId": "disposition.default",
                  "order": 90,
                  "destination": "Discard"
                }
              ]
            }
            """,
            options);

        var result = _compiler.Compile(card!);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var effect = Assert.IsType<CardEffectComponentDefinition>(result.Value.Components[0]);
        Assert.Equal("mana", effect.Effect.TargetResource);
        Assert.IsType<CardDispositionComponentDefinition>(result.Value.Components[1]);
    }
}
