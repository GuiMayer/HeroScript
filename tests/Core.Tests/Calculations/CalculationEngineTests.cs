using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Calculations;

public sealed class CalculationEngineTests
{
    private readonly CalculationEngine _engine = new();

    [Fact]
    public void Calculate_ReducesConfiguredBucketsWithCompleteTrace()
    {
        var pipeline = Pipeline();
        var request = new CalculationRequest
        {
            CalculationId = "card.effect.damage",
            Channel = "resource_reduction",
            BaseValue = 10,
            Influences =
            [
                Influence("relic.more", "more", 1.5f, CalculationSourceKind.Relic),
                Influence("status.weak", "increased", -0.25f, CalculationSourceKind.Status),
                Influence("actor.power", "flat", 2, CalculationSourceKind.Actor)
            ]
        };

        var result = _engine.Calculate(request, pipeline);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(13.5f, result.Value.Value);
        Assert.Equal([10f, 12f, 9f], result.Value.Buckets.Select(bucket => bucket.Input));
        Assert.Equal([12f, 9f, 13.5f], result.Value.Buckets.Select(bucket => bucket.Output));
        Assert.Equal(64, result.Value.Fingerprint.Length);
    }

    [Fact]
    public void Calculate_InputOrderDoesNotChangeTraceOrFingerprint()
    {
        var influences = new[]
        {
            Influence("b", "flat", 2, CalculationSourceKind.Status, priority: 5),
            Influence("a", "flat", 3, CalculationSourceKind.Relic, priority: 5)
        };
        var first = _engine.Calculate(new CalculationRequest
        {
            CalculationId = "same",
            Channel = "resource_reduction",
            BaseValue = 1,
            Influences = influences
        }, Pipeline());
        var second = _engine.Calculate(new CalculationRequest
        {
            CalculationId = "same",
            Channel = "resource_reduction",
            BaseValue = 1,
            Influences = influences.Reverse().ToArray()
        }, Pipeline());

        Assert.Equal(first.Value.Value, second.Value.Value);
        Assert.Equal(first.Value.Fingerprint, second.Value.Fingerprint);
        Assert.Equal(
            first.Value.Buckets[0].Contributions.Select(item => item.InfluenceId),
            second.Value.Buckets[0].Contributions.Select(item => item.InfluenceId));
    }

    [Fact]
    public void Calculate_RejectsInfluenceForUnknownBucket()
    {
        var result = _engine.Calculate(new CalculationRequest
        {
            CalculationId = "invalid",
            Channel = "resource_reduction",
            BaseValue = 1,
            Influences = [Influence("bad", "implicit_health_rule", 5, CalculationSourceKind.Status)]
        }, Pipeline());

        Assert.True(result.IsFailure);
        Assert.Contains("unknown bucket", result.Error);
    }

    [Fact]
    public void EntityResourceProvider_UsesExplicitBindingsForArbitraryResources()
    {
        var actor = Entity("mage", "mana", 7);
        var pipeline = Pipeline() with
        {
            ResourceInfluenceBindings =
            [
                new ResourceInfluenceBindingDefinition
                {
                    BindingId = "actor.mana.scaling",
                    Scope = CalculationEntityScope.Actor,
                    ResourceId = "mana",
                    Channel = "resource_reduction",
                    Bucket = "flat",
                    Scale = 2,
                    Offset = 1
                }
            ]
        };
        var provider = new EntityResourceInfluenceProvider();

        var result = provider.Collect(new CalculationSourceContext
        {
            Actor = actor,
            Pipeline = pipeline
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var influence = Assert.Single(result.Value);
        Assert.Equal(15, influence.Value);
        Assert.Equal("resource_reduction", influence.Channel);
        Assert.Equal("flat", influence.Bucket);
    }

    [Fact]
    public void EntityResourceProvider_CanReadConfiguredResourceField()
    {
        var actor = Entity("mage", "mana", 7);
        var pipeline = Pipeline() with
        {
            ResourceInfluenceBindings =
            [
                new ResourceInfluenceBindingDefinition
                {
                    BindingId = "actor.mana.capacity",
                    Scope = CalculationEntityScope.Actor,
                    ResourceId = "mana",
                    Field = ResourceValueField.Maximum,
                    Channel = "resource_reduction",
                    Bucket = "flat",
                    Scale = .1f
                }
            ]
        };

        var result = new EntityResourceInfluenceProvider().Collect(new CalculationSourceContext
        {
            Actor = actor,
            Pipeline = pipeline
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(10, Assert.Single(result.Value).Value);
    }

    [Fact]
    public void Calculate_RejectsInvalidPinnedResourceBinding()
    {
        var pipeline = Pipeline() with
        {
            ResourceInfluenceBindings =
            [
                new ResourceInfluenceBindingDefinition
                {
                    BindingId = "actor.mana.scaling",
                    Scope = CalculationEntityScope.Actor,
                    ResourceId = "mana",
                    Channel = "resource_reduction",
                    Bucket = "missing"
                }
            ]
        };

        var result = _engine.Calculate(new CalculationRequest
        {
            CalculationId = "invalid-resource-binding",
            Channel = "resource_reduction",
            BaseValue = 1
        }, pipeline);

        Assert.True(result.IsFailure);
        Assert.Contains("unknown bucket", result.Error);
    }

    [Fact]
    public void CardInfluenceProvider_ProjectsComponentsWithoutKnowingTheirMeaning()
    {
        var card = new EffectiveCardDefinition
        {
            CardInstanceId = Guid.Parse("10000000-0000-8000-8000-000000000001"),
            DefinitionId = "strike",
            Components =
            [
                new CardInfluenceComponentDefinition
                {
                    ComponentId = "scaling.strength",
                    Channel = "effect_amount",
                    Bucket = "increased",
                    Value = 0.25f,
                    Priority = 20
                }
            ]
        };

        var result = new CardComponentInfluenceProvider().Collect(
            new CalculationSourceContext { Card = card });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var influence = Assert.Single(result.Value);
        Assert.Equal("effect_amount", influence.Channel);
        Assert.Equal("increased", influence.Bucket);
        Assert.Equal(CalculationSourceKind.Card, influence.SourceKind);
        Assert.Equal(0.25f, influence.Value);
    }

    [Fact]
    public void RunModifierProvider_ReadsPinnedRunStateAndFiltersEffectTags()
    {
        var runId = Guid.Parse("20000000-0000-8000-8000-000000000001");
        var actor = Entity("player", "focus", 10);
        var combat = new CombatState { Hero = actor };
        var run = new RunState
        {
            RunId = runId,
            Modifiers =
            [
                new ScriptModifierInstance
                {
                    InstanceId = Guid.Parse("20000000-0000-8000-8000-000000000002"),
                    ModifierId = "glass_cannon",
                    OwnerId = $"run:{runId}",
                    Owner = new() { Kind = GameplayOwnerKind.Run, Id = runId.ToString() },
                    Stacks = 2,
                    Definition = new ScriptModifierDefinition
                    {
                        ModifierId = "glass_cannon",
                        Influences =
                        [
                            new ContextualInfluenceDefinition
                            {
                                InfluenceId = "attack-increased",
                                Channel = "effect_amount",
                                Bucket = "increased",
                                Value = 0.25f,
                                RequiredTags = ["attack"]
                            }
                        ]
                    }
                }
            ]
        };
        var provider = new RunModifierInfluenceProvider(
            Mock.Of<IRuntimeFormulaEvaluator>());

        var attack = provider.Collect(new CalculationSourceContext
        {
            Run = run,
            Combat = combat, Actor = actor,
            Tags = new HashSet<string> { "attack" }
        });
        var skill = provider.Collect(new CalculationSourceContext
        {
            Run = run,
            Combat = combat, Actor = actor,
            Tags = new HashSet<string> { "skill" }
        });

        Assert.True(attack.IsSuccess, attack.IsFailure ? attack.Error : null);
        Assert.Equal(0.5f, Assert.Single(attack.Value).Value);
        Assert.Empty(skill.Value);
    }

    [Fact]
    public void StatusProvider_UsesOwnerScopeAndPinnedStacks()
    {
        var actor = Entity("hero", "energy", 3);
        var target = Entity("enemy", "health", 20);
        var status = new StatusEffectInstance
        {
            InstanceId = Guid.Parse("30000000-0000-8000-8000-000000000001"),
            StatusId = "strength",
            TargetId = actor.EntityId,
            Stacks = 2,
            IsActive = true,
            Definition = new StatusEffectDefinition
            {
                StatusId = "strength",
                Influences =
                [
                    new ContextualInfluenceDefinition
                    {
                        InfluenceId = "strength.damage",
                        Scope = CalculationEntityScope.Actor,
                        Channel = "effect_amount",
                        Bucket = "increased",
                        Value = 0.25f,
                        RequiredTags = ["damage"]
                    }
                ]
            }
        };
        var combat = CombatTransitions.Create(
            actor,
            [target],
            Core.Determinism.DeterministicContext.Create(1, "revision")) with
        {
            StatusEffects = new Dictionary<string, System.Collections.Immutable.ImmutableArray<StatusEffectInstance>>
            {
                [actor.EntityId] = [status]
            }.ToImmutableDictionary(StringComparer.Ordinal)
        };
        var provider = new StatusCalculationInfluenceProvider(Mock.Of<IRuntimeFormulaEvaluator>());

        var result = provider.Collect(new CalculationSourceContext
        {
            Combat = combat,
            Actor = actor,
            Target = target,
            Tags = new HashSet<string> { "damage" }
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var influence = Assert.Single(result.Value);
        Assert.Equal(CalculationSourceKind.Status, influence.SourceKind);
        Assert.Equal(0.5f, influence.Value);
    }

    [Fact]
    public void RelicProvider_AppliesOnlyToConfiguredOwnerScope()
    {
        var hero = Entity("hero", "energy", 3) with { IsHero = true };
        var enemy = Entity("enemy", "health", 20) with { IsHero = false };
        var run = new RunState
        {
            Relics =
            [
                new RunRelicState
                {
                    RelicInstanceId = Guid.Parse("40000000-0000-8000-8000-000000000001"),
                    DefinitionId = "ember",
                    Owner = new() { Kind = GameplayOwnerKind.Entity, Id = "hero" },
                    Stacks = 2,
                    Influences =
                    [
                        new ContextualInfluenceDefinition
                        {
                            InfluenceId = "ember.damage",
                            Scope = CalculationEntityScope.Actor,
                            Channel = "effect_amount",
                            Bucket = "flat",
                            Value = 1,
                            RequiredTags = ["fire"]
                        }
                    ]
                }
            ]
        };
        var provider = new RelicCalculationInfluenceProvider(Mock.Of<IRuntimeFormulaEvaluator>());

        var heroResult = provider.Collect(new CalculationSourceContext
        {
            Run = run,
            Combat = new() { Hero = hero, Enemies = [enemy] },
            Actor = hero,
            Target = enemy,
            Tags = new HashSet<string> { "fire" }
        });
        var enemyResult = provider.Collect(new CalculationSourceContext
        {
            Run = run,
            Combat = new() { Hero = hero, Enemies = [enemy] },
            Actor = enemy,
            Target = hero,
            Tags = new HashSet<string> { "fire" }
        });

        Assert.Equal(2, Assert.Single(heroResult.Value).Value);
        Assert.Empty(enemyResult.Value);
    }

    [Fact]
    public void ModeAndEncounterProvidersComposeWithExplicitProvenance()
    {
        var run = new RunState
        {
            ResolvedMode = new() { Definition = new()
            {
                ModeId = "sandbox", Influences = [new()
                {
                    InfluenceId = "mode-flat", Channel = "effect_amount", Bucket = "flat", Value = 2,
                    RequiredTags = ["attack"]
                }]
            } },
            ScenarioHash = "scenario-hash",
            Scenario = new() { Influences = [new()
            {
                InfluenceId = "encounter-more", Channel = "effect_amount", Bucket = "increased", Value = .5f,
                RequiredTags = ["attack"]
            }] }
        };
        var context = new CalculationSourceContext
        {
            Run = run, Tags = new HashSet<string> { "attack" }, ContentRevision = "revision"
        };
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>();
        var collected = new CompositeCalculationInfluenceProvider([
            new GameModeCalculationInfluenceProvider(formulas), new EncounterCalculationInfluenceProvider(formulas)
        ]).Collect(context);
        Assert.True(collected.IsSuccess, collected.IsFailure ? collected.Error : null);
        Assert.Equal(2, collected.Value.Count);
        Assert.Contains(collected.Value, item => item.SourceKind == CalculationSourceKind.GameMode);
        Assert.Contains(collected.Value, item => item.SourceKind == CalculationSourceKind.Encounter);
        Assert.Contains(collected.Value, item => item.SourceId == "sandbox");
        Assert.Contains(collected.Value, item => item.SourceId == "scenario-hash");
        var calculated = new CalculationEngine().Calculate(new()
        {
            CalculationId = "combined", Channel = "effect_amount", BaseValue = 10, Influences = collected.Value
        }, new()
        {
            PipelineId = "test", Channel = "effect_amount", Buckets =
            [
                new() { BucketId = "flat", Order = 1, Operation = CalculationBucketOperation.Add },
                new() { BucketId = "increased", Order = 2, Operation = CalculationBucketOperation.AddPercent }
            ]
        });
        Assert.Equal(18, calculated.Value.Value);
    }

    private static CalculationPipelineDefinition Pipeline() => new()
    {
        PipelineId = "generic-resource-change",
        Channel = "resource_reduction",
        Buckets =
        [
            new CalculationBucketDefinition
            {
                BucketId = "flat",
                Order = 10,
                Operation = CalculationBucketOperation.Add
            },
            new CalculationBucketDefinition
            {
                BucketId = "increased",
                Order = 20,
                Operation = CalculationBucketOperation.AddPercent
            },
            new CalculationBucketDefinition
            {
                BucketId = "more",
                Order = 30,
                Operation = CalculationBucketOperation.Multiply
            }
        ]
    };

    private static CalculationInfluence Influence(
        string id,
        string bucket,
        float value,
        CalculationSourceKind kind,
        int priority = 0) => new()
    {
        InfluenceId = id,
        SourceKind = kind,
        SourceId = id,
        Channel = "resource_reduction",
        Bucket = bucket,
        Value = value,
        Priority = priority
    };

    private static CombatEntity Entity(string id, string resourceId, float value) => new()
    {
        EntityId = id,
        ResourceState = new ResourceSet
        {
            Resources = new Dictionary<string, ResourcePool>
            {
                [resourceId] = new ResourcePool
                {
                    ResourceId = resourceId,
                    Current = value,
                    Maximum = 100,
                    Minimum = 0,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = resourceId,
                        DisplayName = resourceId
                    }
                }
            }
        }
    };
}
