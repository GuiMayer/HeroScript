using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class EffectReactionTests
{
    [Theory]
    [InlineData(0, 4, 2)]
    [InlineData(3, 4, .5f)]
    [InlineData(9, 4, 0)]
    [InlineData(0, 30, 5)]
    public void RecoveryUsesActualLossAfterAbsorptionAndOverkill(float shield, float damage, float recovered)
    {
        var request = Request(Damage(damage));
        var enemy = request.Combat.GetActor("enemy")!;
        request = request with { Combat = request.Combat.ReplaceActor(enemy with { ResourceState = enemy.ResourceState with
        { Resources = enemy.ResourceState.Resources.ToImmutableDictionary().Add("shield", ResourcePool.Materialize(new()
            { ResourceId = "shield", DisplayName = "Shield", DefaultMax = shield }, shield)) } }) };
        var result = Success(Executor().Execute(request));
        Assert.Equal(10 + recovered, result.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(10, request.Combat.GetActor("hero")!.GetResource("focus")!.Current);
        var recovery = result.Records.Where(record => record.Provenance.Kind == EffectProvenanceKind.Relic).ToArray();
        if (recovered == 0) Assert.Empty(recovery);
        else
        {
            var application = Assert.Single(recovery);
            Assert.Equal(recovered, application.ResourceOutcome!.AppliedChange);
            Assert.Equal(result.Records.First(record => record.CalculationInfluenceId == null).Identity!.ImpactId,
                application.Identity!.ParentImpactId);
        }
    }

    [Fact]
    public void EachHitAndStatusSourceUseSameReactionsAndAreDeterministic()
    {
        var request = Request(Damage(2) with { Repeat = 3 }) with
        { Provenance = new() { Kind = EffectProvenanceKind.Status, SourceId = "poison" } };
        var before = CanonicalJson.ComputeHash(request);
        var results = Enumerable.Range(0, 5).Select(_ => Success(Executor().Execute(request))).ToArray();
        Assert.All(results, result =>
        {
            Assert.Equal(13, result.State.GetActor("hero")!.GetResource("focus")!.Current);
            Assert.Equal(3, result.Records.Count(record => record.Provenance.Kind == EffectProvenanceKind.Relic));
            Assert.Equal(request.Combat.Determinism.RandomState, result.State.Determinism.RandomState);
            Assert.Equal(Enumerable.Range(0, result.Steps.Length), result.Steps.Select(step => step.Index));
            for (var index = 1; index < result.Steps.Length; index++)
                Assert.Equal(result.Steps[index - 1].StateAfterHash, result.Steps[index].StateBeforeHash);
        });
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result)).Distinct());
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void OpponentCannotBorrowOwnersRecoveryAndFiltersAreExplicit()
    {
        var request = Request(Damage(4));
        var otherSource = Success(Executor().Execute(request with { SourceEntityId = "ally" }));
        Assert.Equal(10, otherSource.State.GetActor("hero")!.GetResource("focus")!.Current);
        var reaction = Reaction() with { RequiredTags = ["fire"], ExcludedTags = ["dot"] };
        var run = WithReaction(request.Run!, reaction);
        Assert.Equal(10, Success(Executor().Execute(request with { Run = run })).State.GetActor("hero")!.GetResource("focus")!.Current);
        var fire = request with { Run = run, Tags = ImmutableHashSet.Create("fire") };
        Assert.Equal(12, Success(Executor().Execute(fire)).State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(10, Success(Executor().Execute(fire with { Tags = fire.Tags.Add("dot") })).State.GetActor("hero")!.GetResource("focus")!.Current);
    }

    [Fact]
    public void RecoveryDoesNotInheritCardBonusesAndReflectionsCannotReenterReactions()
    {
        var request = Request(Damage(2)) with { Card = new() { Components = [new Core.Run.Content.CardInfluenceComponentDefinition
            { ComponentId = "foreign-card-recovery", Channel = "recovery", Bucket = "base", Value = 7 }] } };
        var reflect = Reaction() with { ReactionId = "reflect", OwnerScope = EffectReactionOwnerScope.Target,
            Selection = EffectReactionSelection.Source, Effects = [Damage(1)] };
        var run = RelicTransitions.Acquire(request.Run!, new() { RelicId = "reflect", Reactions = [reflect] },
            new() { Kind = GameplayOwnerKind.Entity, Id = "enemy" }).Value.State;
        var result = Success(Executor().Execute(request with { Run = run }));
        // Parent damage heals the hero by one; reflection subtracts one without generating more reactions.
        Assert.Equal(8, result.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(10, result.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(3, result.Records.Count);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void DefeatedOwnerEligibilityIsConfigured(bool requireAlive, int reactions)
    {
        var request = Request(Damage(30));
        request = request with { Combat = EffectSequenceBudgetTests.DefeatAtZero(request.Combat, "enemy", 10),
            Run = WithReaction(request.Run!, Reaction() with { OwnerScope = EffectReactionOwnerScope.Target,
                RequireLivingOwner = requireAlive, Effects = [Damage(1) with { Target = EffectTarget.RANDOM_ENEMY }] }, "enemy") };
        var result = Success(Executor().Execute(request));
        Assert.Equal(reactions, result.Steps.Count(step => step.Provenance.Kind == EffectProvenanceKind.Relic));
    }

    [Fact]
    public void ReactionFailureDoesNotMutateParentOrRngAndAcquisitionPinsRules()
    {
        var request = Request(Damage(2));
        var invalidTarget = Recovery() with { TargetResource = "missing" };
        request = request with { Run = WithReaction(request.Run!, Reaction() with { Effects = [invalidTarget] }) };
        var before = CanonicalJson.ComputeHash(request);
        Assert.True(Executor().Execute(request).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
        var recovered = JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(request.Run))!;
        Assert.Equal("recover", Assert.Single(Assert.Single(recovered.Relics).Reactions!.Value).ReactionId);
        Assert.Equal(request.Run!.Determinism.ContentRevision, Assert.Single(recovered.Relics).ContentRevision);
    }

    [Fact]
    public void InvalidAndDuplicateReactionContractsAreRejectedBeforeAcquisition()
    {
        Assert.True(EffectReactionPolicies.Validate([Reaction() with { Operation = null }]).IsFailure);
        Assert.True(EffectReactionPolicies.Validate([Reaction(), Reaction()]).IsFailure);
        Assert.True(EffectReactionPolicies.Validate([Reaction() with { Channels = [] }]).IsFailure);
        var run = new RunState { Determinism = DeterministicContext.Create(1, "revision") };
        Assert.True(RelicTransitions.Acquire(run, new() { RelicId = "invalid",
            Reactions = [Reaction() with { Effects = [] }] }).IsFailure);
    }

    [Fact]
    public void CombinedActionAndReactionWorkShareOneHardBudget()
    {
        var request = Request(Damage(1) with { Repeat = 32 });
        request = request with { Run = WithReaction(request.Run!, Reaction() with
            { Effects = [Recovery() with { Repeat = 256 }] }) };
        var before = CanonicalJson.ComputeHash(request);
        var result = Executor().Execute(request);
        Assert.True(result.IsFailure);
        Assert.Contains("limit", result.Error);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void ReactionFormulaAndPipelineUseAcquisitionRevision()
    {
        var request = Request(Damage(4));
        var profiles = EffectSequenceBudgetTests.Profiles();
        profiles.Add("recovery", RecoveryProfile());
        var bundle = EffectSequenceBudgetTests.Bundle(profiles);
        var current = ContentRuntime.Create(bundle).Value;
        var archived = ContentRuntime.Create(bundle with { Manifest = bundle.Manifest with { Revision = "archived" } }).Value;
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(item => item.Resolve("revision", It.IsAny<string?>())).Returns(Result<ContentRuntime>.Success(current));
        runtimes.Setup(item => item.Resolve("archived", It.IsAny<string?>())).Returns(Result<ContentRuntime>.Success(archived));
        var formulas = new Mock<IRevisionedRuntimeFormulaEvaluator>(MockBehavior.Strict);
        formulas.Setup(item => item.EvaluateAtRevision("loss_positive", "archived", It.IsAny<Dictionary<string, float>>(), 0))
            .Returns(Result<float>.Success(1));
        formulas.Setup(item => item.EvaluateAtRevision("loss_half", "archived", It.IsAny<Dictionary<string, float>>(), 0))
            .Returns((string _, string revision, Dictionary<string, float> values, float initial) =>
                Result<float>.Success(-values["parent.applied_change"] * .5f));
        request = request with { Run = request.Run! with { Relics = [request.Run.Relics[0] with { ContentRevision = "archived" }] } };
        var executor = new EffectTriggerExecutor(formulas.Object, new ImmutableEffectProcessor(), runtimes.Object,
            new CalculationEngine(formulas.Object), new EntityResourceInfluenceProvider());
        var result = Success(executor.Execute(request));
        Assert.Equal(12, result.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal("archived", result.Steps.Single(step => step.Provenance.Kind == EffectProvenanceKind.Relic).Calculation!.ContentRevision);
        formulas.VerifyAll();
    }

    [Fact]
    public void PublicationRejectsMissingReactionResourcesAndOldRelicsRemainOptOut()
    {
        var profiles = EffectSequenceBudgetTests.Profiles();
        profiles.Add("recovery", RecoveryProfile());
        var bundle = EffectSequenceBudgetTests.Bundle(profiles);
        var artifacts = bundle.Artifacts.Add("relics/catalog.json", JsonSerializer.SerializeToElement(new Dictionary<string, RelicDefinition>
        { ["recovery"] = new() { RelicId = "recovery", Reactions = [Reaction() with { ResourceId = "missing" }] } }));
        bundle = bundle with { Artifacts = artifacts, Manifest = bundle.Manifest with { Artifacts = artifacts.Select(pair =>
            new ContentArtifactManifest { Path = pair.Key, Kind = pair.Key.Split('/')[0], DefinitionCount = pair.Value.EnumerateObject().Count() }).ToArray() } };
        var published = new ContentGraphValidator().Validate(bundle);
        Assert.False(published.IsValid);
        Assert.Contains(published.Errors, error => error.Contains("missing", StringComparison.Ordinal));
        Assert.DoesNotContain("Reactions", JsonSerializer.Serialize(new RelicDefinition { RelicId = "old" }));
        Assert.DoesNotContain("Reactions", JsonSerializer.Serialize(new RunRelicState { DefinitionId = "old" }));
    }

    private static EffectDefinition Damage(float value) => new()
    { EffectId = "damage", Type = EffectType.DAMAGE, TargetResource = "focus", Parameters = [new()
        { Parameter = EffectNumericParameter.Amount, FlatValue = value, PipelineId = "scoped", Channel = "magnitude", UnitId = "points" }] };

    private static EffectDefinition Recovery() => new()
    { EffectId = "recover", Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus",
        Condition = "loss_positive", Parameters = [new() { Parameter = EffectNumericParameter.Amount,
            FormulaValue = "loss_half", PipelineId = "recovery", Channel = "recovery", UnitId = "points" }] };

    private static EffectReactionDefinition Reaction() => new()
    { ReactionId = "recover", ResourceId = "focus", Operation = ResourceMutationOperation.Subtract,
        Channels = ["magnitude"], Effects = [Recovery()] };

    private static RunState WithReaction(RunState run, EffectReactionDefinition reaction, string owner = "hero")
        => RelicTransitions.Acquire(run with { Relics = [] }, new() { RelicId = "recovery", Reactions = [reaction] },
            new() { Kind = GameplayOwnerKind.Entity, Id = owner }).Value.State;

    private static EffectTriggerExecutionRequest Request(params EffectDefinition[] effects)
        => EffectTransactionTests.Request(effects) with { Run = WithReaction(Fixture().Run, Reaction()) };

    private static EffectTriggerExecutor Executor() => Fixture().Executor;

    private static (EffectTriggerExecutor Executor, RunState Run) Fixture()
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(item => item.Evaluate(It.IsAny<string>(), It.IsAny<Dictionary<string, float>>(), It.IsAny<float>()))
            .Returns((string expression, Dictionary<string, float> variables, float initial) => expression switch
            {
                "loss_positive" => Result<float>.Success(variables["parent.applied_change"] < 0 ? 1 : 0),
                "loss_half" => Result<float>.Success(-variables["parent.applied_change"] * .5f),
                _ => Result<float>.Failure("unknown formula")
            });
        return EffectSequenceBudgetTests.Fixture(extraProfile: RecoveryProfile(), evaluator: formulas.Object);
    }

    private static CalculationPipelineDefinition RecoveryProfile() => new()
    { PipelineId = "recovery", Channel = "recovery", UnitId = "points", Buckets = [new() { BucketId = "base" }] };

    private static EffectBatchResult Success(Result<EffectBatchResult> result)
    { Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null); return result.Value; }
}
