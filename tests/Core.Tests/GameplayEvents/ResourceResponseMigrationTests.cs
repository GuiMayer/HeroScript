using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Content.Migrations;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class ResourceResponseMigrationTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(30)]
    public void InlineAppliedLossMigratesToTypedEventWithoutChangingRecovery(float damage)
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(item => item.Evaluate(It.IsAny<string>(), It.IsAny<Dictionary<string, float>>(), It.IsAny<float>()))
            .Returns((string expression, Dictionary<string, float> variables, float initial) =>
            {
                var loss = variables.GetValueOrDefault(expression.StartsWith("parent.", StringComparison.Ordinal)
                    ? "parent.applied_change" : "event.change.applied_delta");
                return Result<float>.Success(expression.EndsWith("-0.5", StringComparison.Ordinal) ? loss * -.5f : -loss);
            });
        var fixture = EffectSequenceBudgetTests.Fixture(extraProfile: new() { PipelineId = "recovery", Channel = "recovery",
            UnitId = "points", Buckets = [new() { BucketId = "base" }] }, evaluator: formulas.Object);
        var definition = new RelicDefinition { RelicId = "test", Reactions = [new() {
            ReactionId = "recover", ResourceId = "focus", Operation = ResourceMutationOperation.Subtract,
            Channels = ["magnitude"], Effects = [new() { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus",
                Condition = "parent.applied_change * -1", Parameters = [new() { Parameter = EffectNumericParameter.Amount,
                    FormulaValue = "parent.applied_change * -0.5", PipelineId = "recovery", Channel = "recovery", UnitId = "points" }] }] }] };
        var catalog = GameplayCoreCatalog.Create(fixture.Run.SettingId, "revision").Value;
        var converted = GameplayBoundaryResponseMigration.Relic(definition, catalog);
        Assert.True(converted.IsSuccess, converted.IsFailure ? converted.Error : null);
        Assert.Null(converted.Value.Reactions); Assert.Single(converted.Value.Responses!.Value);
        Assert.Same(converted.Value, GameplayBoundaryResponseMigration.Relic(converted.Value, catalog).Value);
        var owner = new GameplayOwner { Kind = GameplayOwnerKind.Entity, Id = "hero" };
        var historical = RelicTransitions.Acquire(fixture.Run, definition, owner).Value.State;
        var migrated = RelicTransitions.Acquire(fixture.Run, converted.Value, owner).Value.State;
        var request = EffectTransactionTests.Request(EffectReactionTests.Damage(damage));
        var expected = fixture.Executor.Execute(request with { Run = historical });
        Assert.True(expected.IsSuccess, expected.IsFailure ? expected.Error : null);
        var input = CanonicalJson.ComputeHash(migrated); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var result = fixture.Executor.Execute(request with { Run = migrated });
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(expected.Value.State.GetActor("hero")!.GetResource("focus")!.Current,
                result.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
            Assert.Equal(expected.Value.Records.Select(item => item.CurrentValue), result.Value.Records.Select(item => item.CurrentValue));
            Assert.Equal(expected.Value.Calculations.Select(item => item.Value), result.Value.Calculations.Select(item => item.Value));
            hashes.Add(CanonicalJson.ComputeHash(result.Value));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(migrated));
    }

    [Fact]
    public void UnsupportedStochasticOrUnexpandedReferencesFailWithoutChangingDefinition()
    {
        var definition = new RelicDefinition { RelicId = "test", Reactions = [new() {
            ReactionId = "recover", ResourceId = "focus", Operation = ResourceMutationOperation.Subtract, Channels = ["magnitude"],
            Effects = [new() { Type = EffectType.HEAL, TargetResource = "focus", FormulaValue = "external_formula" }] }] };
        var before = CanonicalJson.ComputeHash(definition); var catalog = GameplayResponseCompilationTests.Catalog();
        Assert.True(GameplayBoundaryResponseMigration.Relic(definition, catalog).IsFailure);
        var stochastic = definition with { Reactions = [definition.Reactions!.Value[0] with {
            Effects = [definition.Reactions.Value[0].Effects[0] with { FormulaValue = null, FlatValue = 1, Chance = .5f }] }] };
        Assert.True(GameplayBoundaryResponseMigration.Relic(stochastic, catalog).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(definition));
    }

    [Fact]
    public void MigratedPriorityGroupsKeepCrossContainerOrder()
    {
        var context = GameplayResponseCompilationTests.Context();
        var fixture = EffectSequenceBudgetTests.Fixture(extraProfile: new() { PipelineId = "recovery", Channel = "recovery",
            UnitId = "points", Buckets = [new() { BucketId = "base" }] });
        var historical = fixture.Run; var migrated = fixture.Run;
        var catalog = GameplayCoreCatalog.Create(fixture.Run.SettingId, "revision").Value;
        foreach (var (id, priority, body) in new[] {
            ("set", -10, new EffectDefinition { Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF,
                TargetResource = "focus", Operation = ResourceEffectOperation.SET, Parameters = [new() { Parameter = EffectNumericParameter.Amount,
                    FlatValue = 0, PipelineId = "recovery", Channel = "recovery", UnitId = "points" }] }),
            ("heal", 10, new EffectDefinition { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus",
                Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = 1, PipelineId = "recovery", Channel = "recovery", UnitId = "points" }] }) })
        {
            var definition = new RelicDefinition { RelicId = id, Reactions = [new() { ReactionId = id, Priority = priority,
                ResourceId = "focus", Operation = ResourceMutationOperation.Subtract, Channels = ["magnitude"], Effects = [body] }] };
            var converted = GameplayBoundaryResponseMigration.Relic(definition, catalog);
            Assert.True(converted.IsSuccess, converted.IsFailure ? converted.Error : null);
            var owner = new GameplayOwner { Kind = GameplayOwnerKind.Entity, Id = "hero" };
            historical = RelicTransitions.Acquire(historical, definition, owner).Value.State;
            migrated = RelicTransitions.Acquire(migrated, converted.Value, owner).Value.State;
        }
        var effects = fixture.Executor;
        var request = EffectTransactionTests.Request(EffectReactionTests.Damage(1))
            with { Combat = context.Combat! };
        var expected = effects.Execute(request with { Run = historical });
        var actual = effects.Execute(request with { Run = migrated });
        Assert.True(expected.IsSuccess, expected.IsFailure ? expected.Error : null);
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(1, actual.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(expected.Value.Records.Select(item => item.CurrentValue), actual.Value.Records.Select(item => item.CurrentValue));
    }
}
