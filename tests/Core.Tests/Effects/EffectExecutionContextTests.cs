using System.Text.Json;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.StatusEffects;
using Xunit;

namespace Core.Tests.Effects;

public sealed class EffectExecutionContextTests
{
    [Fact]
    public void ClampedResourceMutationHasSignedRequestedAppliedAndLimitedFacts()
    {
        var result = Execute(Hit(15));
        var application = Assert.Single(result.Records);
        var outcome = Assert.IsType<EffectResourceOutcome>(application.ResourceOutcome);
        Assert.Equal(-5, outcome.RequestedValue);
        Assert.Equal(-15, outcome.RequestedChange);
        Assert.Equal(-10, outcome.AppliedChange);
        Assert.Equal(-5, outcome.LimitedChange);
        Assert.False(outcome.CausedDefeat); // No resource name alone implies defeat.
        Assert.Equal(result.ExecutionId, application.Identity!.ExecutionId);
        Assert.Equal(result.Steps[0].Identity, application.Identity);
    }

    [Fact]
    public void HealingAtCapacityProducesResourceFactsWithoutDamageSemantics()
    {
        var result = Execute(Hit(15) with { Type = EffectType.HEAL });
        var outcome = result.Records[0].ResourceOutcome!;
        Assert.Equal(15, outcome.RequestedChange);
        Assert.Equal(10, outcome.AppliedChange);
        Assert.Equal(5, outcome.LimitedChange);
        Assert.False(outcome.CausedDefeat);
    }

    [Fact]
    public void ExplicitOutputReferenceDistinguishesLastAndTotalAcrossRepeats()
    {
        var result = Execute(Hit(3) with { OutputId = "impact", Repeat = 2 }, Hit(0) with
        {
            Type = EffectType.MODIFY_RESOURCE, FlatValue = null,
            FormulaValue = "results.impact.target.total.applied_change"
        });
        Assert.Equal(-6, result.Calculations[2].Value);
        Assert.Equal(0, result.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.NotEqual(result.Steps[0].Identity!.ProcId, result.Steps[1].Identity!.ProcId);
        Assert.Single(result.Steps.Select(step => step.Identity!.ExecutionId).Distinct());
    }

    [Fact]
    public void OutputReferenceUsesCurrentTargetInsteadOfGlobalLastTarget()
    {
        var request = EffectTransactionTests.Request(Hit(3) with { Target = EffectTarget.ALL_ENEMIES, OutputId = "impact" },
            Hit(0) with { Type = EffectType.MODIFY_RESOURCE, FlatValue = null, Target = EffectTarget.ALL_ENEMIES,
                FormulaValue = "results.impact.target.last.applied_change" });
        var neutral = request.Combat.GetActor("neutral")!;
        request = request with { Combat = request.Combat.ReplaceActor(neutral.WithResourceState(new()
        {
            OwnerId = "neutral", Resources = new Dictionary<string, ResourcePool>
            { ["focus"] = ResourcePool.Materialize(neutral.GetResource("focus")!.Definition!, 2, 20) }
        })) };
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(4, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(new float[] { -3, -2 }, result.Value.Calculations.Skip(2).Select(calculation => calculation.Value).ToArray());
        Assert.Equal(result.Value.Steps[0].Identity!.ProcId, result.Value.Steps[1].Identity!.ProcId);
        Assert.NotEqual(result.Value.Steps[0].Identity!.ImpactId, result.Value.Steps[1].Identity!.ImpactId);
    }

    [Fact]
    public void ChildGetsItsExactParentDefeatFactEvenWhenItTargetsAnotherActor()
    {
        var request = EffectTransactionTests.Request(Hit(15) with { ChainedEffects = [new()
        {
            EffectId = "heal-on-defeat", Type = EffectType.HEAL, Target = EffectTarget.SELF,
            TargetResource = "focus", FlatValue = 2, Condition = "parent.caused_defeat"
        }] });
        var enemy = request.Combat.GetActor("enemy")!;
        var pool = enemy.GetResource("focus")!;
        var definition = pool.Definition! with { ThresholdPolicies = [new()
        {
            PolicyId = "defeat", Comparison = ResourceThresholdComparison.LessThanOrEqual,
            ThresholdSource = ResourceThresholdSource.Minimum, Consequence = ResourceThresholdConsequence.DefeatOwner
        }] };
        request = request with { Combat = request.Combat.ReplaceActor(enemy.WithResourceState(new()
        { OwnerId = "enemy", Resources = new Dictionary<string, ResourcePool> { ["focus"] = pool with { Definition = definition } } })) };
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value.Records[0].ResourceOutcome!.CausedDefeat);
        Assert.Contains(result.Value.Records[0].ResourceOutcome!.ThresholdFacts, fact => fact.PolicyId == "defeat");
        Assert.Equal(12, result.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(result.Value.Steps[0].Identity!.ProcId, result.Value.Steps[1].Identity!.ParentProcId);
        Assert.Equal(result.Value.Steps[0].Identity!.ImpactId, result.Value.Steps[1].Identity!.ParentImpactId);
        Assert.NotEqual(result.Value.Steps[0].Identity!.ProcId, result.Value.Steps[1].Identity!.ProcId);
    }

    [Fact]
    public void MissingOrForgedOutputIsARealTransactionalFailure()
    {
        var request = EffectTransactionTests.Request(Hit(3), Hit(0) with
        { FlatValue = null, FormulaValue = "results.missing.target.last.applied_change" });
        var hash = CanonicalJson.ComputeHash(request.Combat);
        Assert.True(EffectTransactionTests.Executor().Execute(request).IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(request.Combat));
        var forged = request with { Variables = new Dictionary<string, float>
        { ["results.missing.target.last.applied_change"] = 10 } };
        Assert.True(EffectTransactionTests.Executor().Execute(forged).IsFailure);
        Assert.True(EffectTransactionTests.Executor().Execute(request with
        { Variables = new Dictionary<string, float> { ["invalid"] = float.NaN } }).IsFailure);
    }

    [Fact]
    public void AliasValidationIncludesInactiveChildrenAndWholeCardComposition()
    {
        Assert.NotEmpty(EffectDefinitionValidator.Validate([Hit(1) with
        { Chance = 0, OutputId = "impact", ChainedEffects = [Hit(1) with { OutputId = "Impact" }] }]));
        Assert.NotEmpty(EffectDefinitionValidator.Validate([Hit(1) with { OutputId = "bad-id" }]));
        var compiled = new CardContentCompiler().Compile(new()
        {
            CardId = "duplicate", Components = [
                new CardEffectComponentDefinition { ComponentId = "a", Effect = Hit(1) with { OutputId = "same" } },
                new CardEffectComponentDefinition { ComponentId = "b", Effect = Hit(1) with { OutputId = "same" } }
            ]
        });
        Assert.True(compiled.IsFailure);
    }

    [Fact]
    public void IdentityFactsAndFramesRoundTripAndRemainDeterministic()
    {
        var outputs = Enumerable.Range(0, 10).Select(_ => Execute(Hit(1) with
        { OutputId = "impact", Repeat = 3, Chance = .8f, ChainedEffects = [Hit(1)] })).ToArray();
        Assert.Single(outputs.Select(output => CanonicalJson.ComputeHash(output)).Distinct());
        var frame = new CombatAnimationFrame
        { Payload = JsonSerializer.SerializeToElement(new { }), EffectSteps = outputs[0].Steps, Applications = outputs[0].Records };
        var restored = JsonSerializer.Deserialize<CombatAnimationFrame>(JsonSerializer.Serialize(frame))!;
        Assert.Equal(CanonicalJson.ComputeHash(frame), CanonicalJson.ComputeHash(restored));
        Assert.NotNull(restored.EffectSteps[0].Identity);
        Assert.NotNull(restored.Applications[0].ResourceOutcome);
    }

    [Fact]
    public void StatusStackChangesRecordApplicationReapplicationAndRemoval()
    {
        var combat = GameplayOwnershipTests.State();
        var processor = new ImmutableEffectProcessor();
        var command = new ResolvedEffectCommand
        {
            EffectInstanceId = "status", Definition = new() { Type = EffectType.APPLY_STATUS, StatusId = "charge", StatusStacks = 2 },
            StatusDefinition = new() { StatusId = "charge", Stacking = StackReapplyPolicy.Add },
            TargetEntityIds = ["enemy"], SourceEntityId = "hero"
        };
        var applied = processor.Apply(combat, [command]).Value;
        var reapplied = processor.Apply(applied.State, [command]).Value;
        var removed = processor.Apply(reapplied.State, [command with { Definition = new()
        { Type = EffectType.REMOVE_STATUS, StatusId = "charge" } }]).Value;
        Assert.Equal(2, Assert.Single(applied.Records[0].StackChanges).CurrentStacks);
        var reapply = Assert.Single(reapplied.Records[0].StackChanges);
        Assert.Equal(2, reapply.PreviousStacks);
        Assert.Equal(4, reapply.CurrentStacks);
        Assert.Equal(EffectStackChangeReason.Reapply, reapply.Reason);
        var removal = Assert.Single(removed.Records[0].StackChanges);
        Assert.Equal(4, removal.PreviousStacks);
        Assert.True(removal.Removed);
        Assert.Equal("enemy", removal.Owner!.Id);
    }

    private static EffectDefinition Hit(float value) => EffectTransactionTests.Resource(EffectType.DAMAGE, value);

    private static EffectBatchResult Execute(params EffectDefinition[] effects)
    {
        var result = EffectTransactionTests.Executor().Execute(EffectTransactionTests.Request(effects));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }
}
