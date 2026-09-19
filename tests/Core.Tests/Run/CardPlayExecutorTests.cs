using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardPlayExecutorTests
{
    private const string Revision = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void FormulaSeesPaidCostAndTraceIncludesCostBeforeEffect()
    {
        var instanceId = Guid.Parse("10000000-0000-8000-8000-000000000009");
        var runtime = Runtime(Card([
            new CardCostComponentDefinition { ComponentId = "cost", Costs = new()
                { Costs = [new() { ResourceId = "energy", Amount = 2 }] } },
            new CardEffectComponentDefinition { ComponentId = "effect", Effect = new()
                { Type = EffectType.DAMAGE, TargetResource = "mana", FormulaValue = "source.resources.energy.current" } },
            Targeting(), Disposition()
        ]), Pipeline());
        var result = Executor(runtime).Execute(new()
        {
            Run = Run(instanceId), Combat = Combat(), CardInstanceId = instanceId,
            ActorId = "hero", SelectedTargetIds = ["enemy"]
        });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(19, result.Value.Combat.GetActor("enemy")!.GetResource("mana")!.Current);
        Assert.Equal(2, result.Value.Steps.Length);
        Assert.Equal("cost", result.Value.Steps[0].Provenance.ComponentId);
        Assert.Equal("effect", result.Value.Steps[1].Provenance.ComponentId);
    }

    [Fact]
    public void Execute_UsesUpgradedBaseContextPipelineAndExplicitResourceAtomically()
    {
        var instanceId = Guid.Parse("10000000-0000-8000-8000-000000000001");
        var card = Card(
        [
            new CardCostComponentDefinition
            {
                ComponentId = "cost.energy",
                Order = 10,
                Costs = new ActionCosts
                {
                    Costs = [new ResourceCost { ResourceId = "energy", Amount = 2 }]
                }
            },
            new CardEffectComponentDefinition
            {
                ComponentId = "effect.drain",
                Order = 20,
                Effect = new EffectDefinition
                {
                    EffectId = "arcane_drain.mana",
                    Type = EffectType.DAMAGE,
                    Target = EffectTarget.TARGET,
                    TargetResource = "mana",
                    FlatValue = 5
                }
            },
            new CardInfluenceComponentDefinition
            {
                ComponentId = "influence.flat",
                Order = 30,
                Channel = "effect_amount",
                Bucket = "flat",
                Value = 3
            },
            Targeting(),
            Disposition()
        ]);
        var pipeline = Pipeline();
        var runtime = Runtime(card, pipeline);
        var run = Run(instanceId, new CardUpgradeState
        {
            UpgradeId = "empowered",
            Patches =
            [
                new CardEffectNumericPatchDefinition
                {
                    ComponentId = "effect.drain",
                    Attribute = CardEffectNumericAttribute.FlatValue,
                    Operation = CardNumericPatchOperation.Add,
                    Value = 2
                }
            ]
        });
        var combat = Combat();
        var executor = Executor(runtime);

        var result = executor.Execute(new CardPlayExecutionRequest
        {
            Run = run,
            Combat = combat,
            CardInstanceId = instanceId,
            ActorId = "hero",
            SelectedTargetIds = ["enemy"]
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(1, result.Value.Combat.GetActor("hero")!.GetResource("energy")!.Current);
        Assert.Equal(10, result.Value.Combat.GetActor("enemy")!.GetResource("mana")!.Current);
        Assert.Equal(3, combat.GetActor("hero")!.GetResource("energy")!.Current);
        Assert.Equal(20, combat.GetActor("enemy")!.GetResource("mana")!.Current);
        Assert.Equal(7, Assert.Single(result.Value.Calculations).BaseValue);
        Assert.Equal(10, result.Value.Calculations[0].Value);
        Assert.Equal(2, result.Value.Calculations[0].BaseTrace.Count);
        Assert.Equal(CalculationSourceKind.Card, result.Value.Calculations[0].BaseTrace[0].SourceKind);
        Assert.Equal(CalculationSourceKind.Upgrade, result.Value.Calculations[0].BaseTrace[1].SourceKind);
        Assert.Equal("empowered", result.Value.Calculations[0].BaseTrace[1].SourceId);
        Assert.Equal(5, result.Value.Calculations[0].BaseTrace[1].Input);
        Assert.Equal(7, result.Value.Calculations[0].BaseTrace[1].Output);
        Assert.Equal("card.played", result.Value.CardZoneResolutionFlowId);
        var action = Assert.Single(result.Value.Combat.ActionHistory);
        Assert.Equal(ActionType.PLAY_CARD, action.ActionType);
        Assert.Equal(instanceId, action.CardInstanceId);
        Assert.Equal("arcane_drain", action.CardDefinitionId);
        Assert.Equal(result.Value.Applications, action.Applications);
    }

    [Fact]
    public void Execute_SameSnapshotProducesSameFingerprintAndState()
    {
        var instanceId = Guid.Parse("10000000-0000-8000-8000-000000000002");
        var runtime = Runtime(Card(
        [
            new CardEffectComponentDefinition
            {
                ComponentId = "effect.drain",
                Effect = new EffectDefinition
                {
                    EffectId = "arcane_drain.mana",
                    Type = EffectType.DAMAGE,
                    Target = EffectTarget.TARGET,
                    TargetResource = "mana",
                    FlatValue = 4,
                    Chance = .75f,
                    Repeat = 2
                }
            },
            Targeting(),
            Disposition()
        ]), Pipeline());
        var run = Run(instanceId);
        var combat = Combat();
        var executor = Executor(runtime);
        var request = new CardPlayExecutionRequest
        {
            Run = run,
            Combat = combat,
            CardInstanceId = instanceId,
            ActorId = "hero",
            SelectedTargetIds = ["enemy"]
        };

        var first = executor.Execute(request);
        var second = executor.Execute(request);

        Assert.True(first.IsSuccess && second.IsSuccess);
        Assert.Equal(first.Value.ResolutionFingerprint, second.Value.ResolutionFingerprint);
        Assert.Equal(
            CanonicalJson.ComputeHash(first.Value.Combat),
            CanonicalJson.ComputeHash(second.Value.Combat));
    }

    [Fact]
    public void Execute_AppliesResourceInfluenceFromPinnedPipeline()
    {
        var instanceId = Guid.Parse("10000000-0000-8000-8000-000000000004");
        var card = Card(
        [
            new CardEffectComponentDefinition
            {
                ComponentId = "effect.drain",
                Effect = new EffectDefinition
                {
                    EffectId = "arcane_drain.mana",
                    Type = EffectType.DAMAGE,
                    Target = EffectTarget.TARGET,
                    TargetResource = "mana",
                    FlatValue = 5
                }
            },
            Targeting(),
            Disposition()
        ]);
        var pipeline = Pipeline() with
        {
            ResourceInfluenceBindings =
            [
                new ResourceInfluenceBindingDefinition
                {
                    BindingId = "actor.mana.flat",
                    Scope = CalculationEntityScope.Actor,
                    ResourceId = "mana",
                    Channel = "effect_amount",
                    Bucket = "flat"
                }
            ]
        };
        var result = Executor(Runtime(card, pipeline)).Execute(new CardPlayExecutionRequest
        {
            Run = Run(instanceId),
            Combat = Combat(),
            CardInstanceId = instanceId,
            ActorId = "hero",
            SelectedTargetIds = ["enemy"]
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(5, result.Value.Combat.GetActor("enemy")!.GetResource("mana")!.Current);
        var calculation = Assert.Single(result.Value.Calculations);
        var contribution = Assert.Single(Assert.Single(calculation.Buckets).Contributions);
        Assert.Equal("actor.mana.flat", contribution.InfluenceId);
        Assert.Equal(CalculationSourceKind.Actor, contribution.SourceKind);
    }

    [Fact]
    public void Execute_CalculatesAndConsumesConfiguredTargetCapacityBeforeArbitraryResourceDamage()
    {
        var instanceId = Guid.Parse("10000000-0000-8000-8000-000000000008");
        var card = Card(
        [
            new CardEffectComponentDefinition
            {
                ComponentId = "effect.drain",
                Effect = new EffectDefinition
                {
                    EffectId = "arcane_drain.resolve",
                    Type = EffectType.DAMAGE,
                    Target = EffectTarget.TARGET,
                    TargetResource = "mana",
                    FlatValue = 5
                }
            },
            Targeting(),
            Disposition()
        ]);
        var pipeline = Pipeline() with
        {
            Buckets =
            [
                new() { BucketId = "flat", Order = 10, Operation = CalculationBucketOperation.Add },
                new() { BucketId = "mitigation", Order = 20, Operation = CalculationBucketOperation.ConsumeCapacity },
                new() { BucketId = "final", Order = 30, Operation = CalculationBucketOperation.Add, Minimum = 0 }
            ],
            ResourceInfluenceBindings = [new()
            {
                BindingId = "target.guard", Scope = CalculationEntityScope.Target,
                ResourceId = "guard", Channel = "effect_amount", Bucket = "mitigation",
                RequiredTags = ["effect.damage"], MissingResource = MissingResourcePolicy.Ignore,
                Settlement = new()
                {
                    Operation = ResourceEffectOperation.SUBTRACT,
                    Field = ResourceValueField.Current,
                    UseEffectiveValue = true
                }
            }]
        };
        var combat = Combat().ReplaceActor(Entity("enemy", false, ("mana", 20), ("guard", 3)));

        var result = Executor(Runtime(card, pipeline)).Execute(new CardPlayExecutionRequest
        {
            Run = Run(instanceId), Combat = combat, CardInstanceId = instanceId,
            ActorId = "hero", SelectedTargetIds = ["enemy"]
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(0, result.Value.Combat.GetActor("enemy")!.GetResource("guard")!.Current);
        Assert.Equal(18, result.Value.Combat.GetActor("enemy")!.GetResource("mana")!.Current);
        Assert.Equal(2, result.Value.Applications.Count);
        var calculation = Assert.Single(result.Value.Calculations);
        Assert.Equal(2, calculation.Value);
        Assert.All(result.Value.Applications,
            application => Assert.Equal(calculation.Fingerprint, application.CalculationFingerprint));
    }

    [Fact]
    public void Execute_InvalidLaterEffectDoesNotMutateInputOrSpendCost()
    {
        var instanceId = Guid.Parse("10000000-0000-8000-8000-000000000003");
        var runtime = Runtime(Card(
        [
            new CardCostComponentDefinition
            {
                ComponentId = "cost.energy",
                Costs = new ActionCosts
                {
                    Costs = [new ResourceCost { ResourceId = "energy", Amount = 2 }]
                }
            },
            new CardEffectComponentDefinition
            {
                ComponentId = "effect.invalid",
                Effect = new EffectDefinition
                {
                    EffectId = "invalid",
                    Type = EffectType.DAMAGE,
                    Target = EffectTarget.TARGET,
                    TargetResource = "missing",
                    FlatValue = 4
                }
            },
            Targeting(),
            Disposition()
        ]), Pipeline());
        var run = Run(instanceId);
        var combat = Combat();

        var result = Executor(runtime).Execute(new CardPlayExecutionRequest
        {
            Run = run,
            Combat = combat,
            CardInstanceId = instanceId,
            ActorId = "hero",
            SelectedTargetIds = ["enemy"]
        });

        Assert.True(result.IsFailure);
        Assert.Contains("missing", result.Error);
        Assert.Equal(3, combat.GetActor("hero")!.GetResource("energy")!.Current);
        Assert.Empty(combat.ActionHistory);
    }

    private static CardPlayExecutor Executor(ContentRuntime runtime)
    {
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(item => item.Resolve(Revision, "default"))
            .Returns(Result<ContentRuntime>.Success(runtime));
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(item => item.Evaluate(It.IsAny<string>(), It.IsAny<Dictionary<string, float>>()))
            .Returns((string expression, Dictionary<string, float> variables, float initialValue) =>
                variables.TryGetValue(expression, out var value) ? Result<float>.Success(value) : Result<float>.Failure("Unknown variable"));
        var influences = new CompositeCalculationInfluenceProvider(
        [
            new CardComponentInfluenceProvider(formulas.Object),
            new EntityResourceInfluenceProvider(),
            new EntityStatInfluenceProvider()
        ]);
        return new CardPlayExecutor(
            runtimes.Object,
            new CardContentCompiler(),
            new EffectiveCardResolver(),
            new CardPlayEvaluator(new ActionCostEvaluator(formulas.Object), formulas.Object),
            new EffectTriggerExecutor(formulas.Object, new ImmutableEffectProcessor(),
                runtimes.Object, new CalculationEngine(), influences));
    }

    private static ContentRuntime Runtime(
        CardContentDefinition card,
        CalculationPipelineDefinition pipeline)
    {
        var definitions = new (string Kind, string Path, object Value)[]
        {
            ("cards", "cards/catalog.json", new Dictionary<string, CardContentDefinition>
            {
                [card.CardId] = card
            }),
            ("calculation-pipelines", "calculation-pipelines/default.json",
                new Dictionary<string, CalculationPipelineDefinition>
                {
                    [pipeline.PipelineId] = pipeline
                })
        };
        var artifacts = definitions.ToImmutableDictionary(
            item => item.Path,
            item => JsonSerializer.SerializeToElement(item.Value),
            StringComparer.Ordinal);
        var manifest = new ContentManifest
        {
            ConfigName = "default",
            Revision = Revision,
            Artifacts = definitions.Select(item => new ContentArtifactManifest
            {
                Kind = item.Kind,
                Path = item.Path,
                DefinitionCount = 1
            }).ToArray()
        };
        var result = ContentRuntime.Create(new ContentBundle
        {
            Manifest = manifest,
            Artifacts = artifacts
        });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private static CardContentDefinition Card(IReadOnlyList<CardComponentDefinition> components) => new()
    {
        CardId = "arcane_drain",
        Components = components
    };

    private static CardTargetingComponentDefinition Targeting() => new()
    {
        ComponentId = "targeting.primary",
        Order = 80,
        Target = EffectTarget.TARGET,
        MinimumTargets = 1,
        MaximumTargets = 1
    };

    private static CardDispositionComponentDefinition Disposition() => new()
    {
        ComponentId = "disposition.default",
        Order = 90,
        CardZoneResolutionFlowId = "card.played"
    };

    private static CalculationPipelineDefinition Pipeline() => new()
    {
        PipelineId = "default_effect_amount",
        Channel = "effect_amount",
        Buckets =
        [
            new CalculationBucketDefinition
            {
                BucketId = "flat",
                Order = 10,
                Operation = CalculationBucketOperation.Add
            }
        ]
    };

    private static RunState Run(Guid cardInstanceId, params CardUpgradeState[] upgrades)
    {
        var instance = new CardInstanceState
        {
            CardInstanceId = cardInstanceId,
            DefinitionId = "arcane_drain",
            Upgrades = upgrades
        };
        return new RunState
        {
            RunId = Guid.Parse("20000000-0000-8000-8000-000000000001"),
            ConfigName = "default",
            Deck = new DeckState
            {
                CardInstances = new Dictionary<Guid, CardInstanceState> { [cardInstanceId] = instance },
                HandInstanceIds = [cardInstanceId]
            },
            ResolvedMode = new ResolvedGameMode
            {
                Definition = new GameModeDefinition
                {
                    ModeId = "test",
                    CalculationPipelineIds = ["default_effect_amount"]
                }
            },
            Determinism = DeterministicContext.Create(42, Revision)
        };
    }

    private static CombatState Combat() => new()
    {
        CombatId = Guid.Parse("30000000-0000-8000-8000-000000000001"),
        Actors = new[] { Entity("hero", true, ("energy", 3), ("mana", 10)),
                Entity("enemy", false, ("mana", 20)) }
            .ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
        Determinism = DeterministicContext.Create(77, Revision)
    };

    private static CombatActorState Entity(
        string id,
        bool hero,
        params (string Id, float Current)[] resources) => new()
    {
        InstanceId = id,
        SideId = hero ? "player" : "opposition", ControllerBinding = new ControllerBinding { Kind = hero ? ControllerKind.Player : ControllerKind.AI },
        ResourceState = new ResourceSet
        {
            OwnerId = id,
            Resources = resources.ToDictionary(
                item => item.Id,
                item => new ResourcePool
                {
                    ResourceId = item.Id,
                    Current = item.Current,
                    Minimum = 0,
                    Maximum = 100,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = item.Id,
                        DisplayName = item.Id
                    }
                },
                StringComparer.Ordinal)
        }
    };
}
