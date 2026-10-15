using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Effects;
using Core.Math;
using Core.Run;
using Core.Run.Loadouts;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class InscriptionCalculationTests
{
    [Fact]
    public void TargetAttributeUsesEquippedOwnersTagsAndRevisionedFormulaRatherThanAttackersOwnerVariables()
    {
        var (initial, _, _) = InscriptionLoadoutTests.Setup();
        var item = LoadoutTestContent.Inscription();
        item = item with { AttributeBonuses = [item.AttributeBonuses[0] with { Influence = item.AttributeBonuses[0].Influence with
        { Scope = CalculationEntityScope.Target, Value = null, Formula = "0 + 0", RequiredTags = ["fire"] } }] };
        var runtime = ContentRuntime.Create(LoadoutTestContent.Bundle(item)).Value;
        var run = InscriptionAcquisition.Grant(initial, runtime, "alice", "focus", "one").Value;
        var resolvers = new Mock<IContentRuntimeResolver>();
        resolvers.Setup(value => value.Resolve(runtime.Manifest.Revision, "default")).Returns(Result<ContentRuntime>.Success(runtime));
        run = new LoadoutChangePlanner(resolvers.Object).Plan(run, "alice", new Dictionary<string, Guid> { ["one"] = InscriptionLoadoutTests.Item(run) }).Value.Candidate;
        var formulas = new Mock<IRevisionedRuntimeFormulaEvaluator>(MockBehavior.Strict);
        formulas.Setup(value => value.EvaluateAtRevision("0 + 0", runtime.Manifest.Revision,
            It.Is<Dictionary<string, float>>(values => !values.ContainsKey("owner.resources.fake.current")), 0)).Returns(Result<float>.Success(.4f));
        var context = new CalculationSourceContext { Run = run, Actor = new() { InstanceId = "bob" }, Target = new() { InstanceId = "alice" },
            Tags = new HashSet<string> { "fire" }, Variables = new Dictionary<string, float> { ["owner.resources.fake.current"] = 999 } };
        var provider = new InscriptionCalculationInfluenceProvider(resolvers.Object, formulas.Object);
        Assert.Equal(.4f, Assert.Single(provider.Collect(context).Value).Value);
        Assert.Empty(provider.Collect(context with { Tags = new HashSet<string>() }).Value);
        Assert.Equal(999, context.Variables["owner.resources.fake.current"]);
        formulas.VerifyAll();
    }

    [Fact]
    public void SameEquippedCharacterContributionReachesProjectionAndGameplayWithoutCardMutation()
    {
        var (initial, runtime, planner) = InscriptionLoadoutTests.Setup();
        var run = InscriptionAcquisition.Grant(initial, runtime, "alice", "focus", "one").Value;
        run = planner.Plan(run, "alice", new Dictionary<string, Guid> { ["one"] = InscriptionLoadoutTests.Item(run) }).Value.Candidate;
        var alice = new CombatActorState { InstanceId = "alice" };
        var bob = new CombatActorState { InstanceId = "bob" };
        var resolver = new Mock<IContentRuntimeResolver>();
        resolver.Setup(value => value.Resolve(runtime.Manifest.Revision, "default")).Returns(Result<ContentRuntime>.Success(runtime));
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>();
        var provider = new InscriptionCalculationInfluenceProvider(resolver.Object, formulas);
        var engine = new CalculationEngine();
        var calculations = new CalculationResolver(formulas, resolver.Object, engine, provider);
        var combat = new CombatState { Actors = new Dictionary<string, CombatActorState> { ["alice"] = alice, ["bob"] = bob } };
        var context = new CalculationSourceContext { Run = run, Actor = alice, Target = bob, ContentRevision = runtime.Manifest.Revision };
        var parameter = new EffectNumericParameterDefinition { Parameter = EffectNumericParameter.Amount, FlatValue = 0, PipelineId = "critical", Channel = "critical", UnitId = "scalar" };
        var projected = new ActorAttributeProjectionService(resolver.Object, formulas, engine, provider).Project(run, combat, "alice");
        Assert.True(projected.IsSuccess, projected.IsFailure ? projected.Error : null);
        var stat = Assert.Single(projected.Value.Attributes);
        Assert.Equal(0, stat.BaseValue);
        Assert.Equal(25, stat.EffectiveValue);
        var resolved = calculations.ResolveParameter(new() { Type = EffectType.DAMAGE }, parameter, "gameplay", context with { Combat = combat });
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : null);
        Assert.Equal(stat.EffectiveValue, resolved.Value.Value * 100);
        Assert.Equal(CalculationSourceKind.Inscription, Assert.Single(provider.Collect(context).Value).SourceKind);
        Assert.Empty(provider.Collect(context with { Actor = bob, Target = alice }).Value);
        var unequipped = planner.Plan(run, "alice", ImmutableDictionary<string, Guid>.Empty).Value.Candidate;
        Assert.Empty(provider.Collect(context with { Run = unequipped }).Value);
        Assert.Equal(.25f, Assert.Single(provider.Collect(context).Value).Value);
    }

    [Fact]
    public void PersistentAttributeProjectionEqualsCombatAndPreviewUsesCanonicalValues()
    {
        var (initial, content, planner) = InscriptionLoadoutTests.Setup();
        var run = InscriptionAcquisition.Grant(initial, content, "alice", "focus", "one").Value with
        { PlayerEntityId = "alice", PlayerEntity = new() { InstanceId = "alice" } };
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(value => value.Resolve(content.Manifest.Revision, "default")).Returns(Result<ContentRuntime>.Success(content));
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>();
        var provider = new InscriptionCalculationInfluenceProvider(runtimes.Object, formulas);
        var attributes = new ActorAttributeProjectionService(runtimes.Object, formulas, new CalculationEngine(), provider);
        var runs = new Mock<IRunQueryService>();
        runs.Setup(value => value.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));
        var queries = new LoadoutQueryService(runs.Object, runtimes.Object, planner, attributes);
        var preview = queries.Preview(run.RunId, new("alice", ImmutableSortedDictionary<string, Guid>.Empty.Add("one", InscriptionLoadoutTests.Item(run)), run.Sequence, run.Determinism.Step));
        Assert.True(preview.IsSuccess, preview.IsFailure ? preview.Error : null);
        Assert.Equal(0, Assert.Single(preview.Value.Before.Attributes!.Attributes).EffectiveValue);
        Assert.Equal(25, Assert.Single(preview.Value.After.Attributes!.Attributes).EffectiveValue);
        Assert.Equal(0, Assert.Single(queries.Read(run, "alice").Value.Attributes!.Attributes).EffectiveValue);
        var changed = planner.Plan(run, "alice", preview.Value.After.Assignments).Value.Candidate;
        var combat = new CombatState { Actors = new Dictionary<string, CombatActorState> { ["alice"] = new() { InstanceId = "alice" } } };
        Assert.Equal(Assert.Single(attributes.Project(changed, null, "alice").Value.Attributes).EffectiveValue,
            Assert.Single(attributes.Project(changed, combat, "alice").Value.Attributes).EffectiveValue);
    }

    [Fact]
    public void RelicOwnershipDoesNotNeedAFakeCombatAndSideOwnershipIsNotInvented()
    {
        var actor = new CombatActorState { InstanceId = "alice" };
        var run = new RunState { PlayerEntityId = "alice", Relics = [new() { RelicInstanceId = Guid.NewGuid(), Owner = new() { Kind = GameplayOwnerKind.Entity, Id = "alice" },
            Influences = [new() { InfluenceId = "bonus", Channel = "critical", Bucket = "flat", Value = .5f }] }] };
        var provider = new RelicCalculationInfluenceProvider(Mock.Of<IRuntimeFormulaEvaluator>());
        var context = new CalculationSourceContext { Run = run, Actor = actor };
        Assert.Equal(.5f, Assert.Single(provider.Collect(context).Value).Value);
        Assert.Empty(provider.Collect(context with { Actor = new() { InstanceId = "bob" } }).Value);
        Assert.False(new GameplayOwner { Kind = GameplayOwnerKind.Side, Id = "party" }.Includes(actor, null, run));
        Assert.True(new GameplayOwner { Kind = GameplayOwnerKind.Run, Id = run.RunId.ToString() }.Includes(actor, null, run));
    }
}
