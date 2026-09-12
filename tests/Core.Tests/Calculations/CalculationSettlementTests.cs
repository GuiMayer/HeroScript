using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat;
using Core.Combat.Models;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Xunit;

namespace Core.Tests.Calculations;

public sealed class CalculationSettlementTests
{
    [Fact]
    public void ArbitraryStatAndCapacityProducePureResultThenAtomicResourceSettlement()
    {
        var source = Actor("source", ("focus", 10)) with
        {
            Components = new Dictionary<string, EntityComponentState>
            {
                ["resources"] = new ResourceEntityComponentState
                {
                    ComponentId = "resources", State = Resources("source", ("focus", 10))
                },
                ["stats"] = new StatEntityComponentState
                {
                    ComponentId = "stats",
                    Values = new Dictionary<string, float> { ["power"] = 2 }
                }
            }
        };
        var target = Actor("target", ("resolve", 20), ("barrier", 5));
        var combat = CombatTransitions.Create([source, target],
            Core.Determinism.DeterministicContext.Create(7, "revision"));
        var pipeline = new CalculationPipelineDefinition
        {
            PipelineId = "arbitrary-damage",
            Channel = "damage",
            Buckets =
            [
                new() { BucketId = "flat", Order = 10, Operation = CalculationBucketOperation.Add },
                new() { BucketId = "mitigation", Order = 20, Operation = CalculationBucketOperation.ConsumeCapacity },
                new() { BucketId = "final", Order = 30, Operation = CalculationBucketOperation.Add, Minimum = 0 }
            ],
            StatInfluenceBindings = [new()
            {
                BindingId = "actor.power", Scope = CalculationEntityScope.Actor,
                ComponentId = "stats", ValueId = "power", Channel = "damage", Bucket = "flat",
                RequiredTags = ["effect.damage"]
            }],
            ResourceInfluenceBindings = [new()
            {
                BindingId = "target.barrier", Scope = CalculationEntityScope.Target,
                ResourceId = "barrier", Channel = "damage", Bucket = "mitigation",
                RequiredTags = ["effect.damage"], MissingResource = MissingResourcePolicy.Ignore,
                Settlement = new()
                {
                    Operation = ResourceEffectOperation.SUBTRACT,
                    Field = ResourceValueField.Current,
                    UseEffectiveValue = true
                }
            }]
        };
        var context = new CalculationSourceContext
        {
            Actor = source, Target = target, Combat = combat, Pipeline = pipeline,
            Tags = new HashSet<string> { "effect.damage" }
        };
        var collected = new CompositeCalculationInfluenceProvider(
            [new EntityStatInfluenceProvider(), new EntityResourceInfluenceProvider()]).Collect(context);
        var calculation = new CalculationEngine().Calculate(new()
        {
            CalculationId = "hit", ContentRevision = "revision", Channel = "damage",
            BaseValue = 6, Influences = collected.Value, Tags = context.Tags
        }, pipeline);

        Assert.True(calculation.IsSuccess, calculation.IsFailure ? calculation.Error : null);
        Assert.Equal(3, calculation.Value.Value);
        Assert.Equal(5, calculation.Value.Buckets[1].Contributions[0].EffectiveValue);
        Assert.Equal(5, target.GetResource("barrier")!.Current);

        var settlements = new CalculationSettlementPlanner().Plan(calculation.Value, pipeline, context);
        var applied = new ImmutableEffectProcessor().Apply(combat, [new ResolvedEffectCommand
        {
            EffectInstanceId = "damage", SourceEntityId = source.InstanceId,
            TargetEntityIds = [target.InstanceId], ResolvedValue = calculation.Value.Value,
            Calculation = calculation.Value, Settlements = settlements.Value,
            Definition = new()
            {
                EffectId = "damage", Type = EffectType.DAMAGE, TargetResource = "resolve"
            }
        }]);

        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
        Assert.Equal(0, applied.Value.State.GetActor("target")!.GetResource("barrier")!.Current);
        Assert.Equal(17, applied.Value.State.GetActor("target")!.GetResource("resolve")!.Current);
        Assert.Equal(5, combat.GetActor("target")!.GetResource("barrier")!.Current);
        Assert.Equal(2, applied.Value.Records.Count);
        Assert.All(applied.Value.Records,
            record => Assert.Equal(calculation.Value.Fingerprint, record.CalculationFingerprint));
    }

    [Fact]
    public void FormulaBucketConsumesOnlyRequestAndPreviousBucketResult()
    {
        var engine = new CalculationEngine(new FormulaStub());
        var pipeline = new CalculationPipelineDefinition
        {
            PipelineId = "armor", Channel = "damage", Buckets = [new()
            {
                BucketId = "armor", Operation = CalculationBucketOperation.Formula,
                Formula = "test_formula"
            }]
        };
        var result = engine.Calculate(new()
        {
            CalculationId = "formula", Channel = "damage", BaseValue = 10,
            Variables = new Dictionary<string, float> { ["armor"] = 3 }
        }, pipeline);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(7, result.Value.Value);
    }

    [Fact]
    public void SettlementUsesConfiguredUnitConversionAndExactResourceProvenance()
    {
        var target = Actor("target", ("armor", 8));
        var pipeline = new CalculationPipelineDefinition
        {
            PipelineId = "converted-capacity", Channel = "damage",
            Buckets = [new() { BucketId = "capacity", Operation = CalculationBucketOperation.ConsumeCapacity }],
            ResourceInfluenceBindings = [new()
            {
                BindingId = "armor-capacity", Scope = CalculationEntityScope.Target,
                ResourceId = "armor", Channel = "damage", Bucket = "capacity", Scale = .5f,
                Settlement = new() { Scale = 2 }
            }]
        };
        var context = new CalculationSourceContext { Target = target, Pipeline = pipeline };
        var collected = new EntityResourceInfluenceProvider().Collect(context);
        var calculation = new CalculationEngine().Calculate(new()
        {
            CalculationId = "converted-hit", Channel = "damage", BaseValue = 3,
            Influences = collected.Value.Concat([new CalculationInfluence
            {
                InfluenceId = "armor-capacity", SourceKind = CalculationSourceKind.Relic,
                SourceId = "collision", Channel = "damage", Bucket = "capacity", Value = 99,
                Priority = -1
            }]).ToArray()
        }, pipeline);

        Assert.True(calculation.IsSuccess, calculation.IsFailure ? calculation.Error : null);
        var settlements = new CalculationSettlementPlanner().Plan(calculation.Value, pipeline, context);

        Assert.True(settlements.IsSuccess, settlements.IsFailure ? settlements.Error : null);
        Assert.Equal(6, Assert.Single(settlements.Value).Value);
    }

    private static CombatActorState Actor(string id, params (string Id, float Value)[] resources) => new()
    {
        InstanceId = id,
        DefinitionId = id,
        ResourceState = Resources(id, resources)
    };

    private static ResourceSet Resources(string owner, params (string Id, float Value)[] resources) => new()
    {
        OwnerId = owner,
        Resources = resources.ToDictionary(item => item.Id, item => new ResourcePool
        {
            ResourceId = item.Id,
            Current = item.Value,
            Minimum = 0,
            Maximum = 100,
            Definition = new ResourceDefinition
            {
                ResourceId = item.Id,
                DisplayName = item.Id,
                DefaultMin = 0,
                DefaultMax = 100,
                CanExceedMax = false,
                CanBeNegative = false
            }
        }, StringComparer.Ordinal)
    };

    private sealed class FormulaStub : IRuntimeFormulaEvaluator
    {
        public Core.Common.Result<float> Evaluate(
            string expressionOrFormulaId,
            Dictionary<string, float>? variables = null,
            float initialValue = 0) =>
            Core.Common.Result<float>.Success(initialValue - variables!["armor"]);
    }
}
