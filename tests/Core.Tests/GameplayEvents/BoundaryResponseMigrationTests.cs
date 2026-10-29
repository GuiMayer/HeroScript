using System.Collections.Immutable;
using Core.Combat.Flow;
using Core.Content.Migrations;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class BoundaryResponseMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MigratedStatusRetainsPayloadTimingSourceAndNumbersWithoutDoubleDispatch(bool directStacks)
    {
        var fixture = StackPayloadTests.Fixture(withTrigger: true);
        var applied = fixture.Executor.Execute(EffectTransactionTests.Request(StackPayloadTests.Apply(2)) with { Run = fixture.Run });
        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
        var state = applied.Value.State;
        var run = applied.Value.Run! with { Deck = new() };
        var status = Assert.Single(state.StatusEffects["enemy"]);
        if (directStacks)
        {
            var trigger = status.Definition.Triggers[0];
            var effect = trigger.Effects[0];
            status = status with { Definition = status.Definition with { Triggers = [trigger with { Effects = [effect with {
                Parameters = [effect.Parameters[0] with { InputQuantityId = null, FormulaValue = "stacks" }] }] }] } };
            state = state with { StatusEffects = state.StatusEffects.SetItem("enemy", [status]) };
        }
        var catalog = GameplayCoreCatalog.Create(run.SettingId, "revision").Value;
        var converted = GameplayBoundaryResponseMigration.Status(status.Definition, catalog);
        Assert.True(converted.IsSuccess, converted.IsFailure ? converted.Error : null);
        Assert.Empty(converted.Value.Triggers); Assert.Single(converted.Value.Responses!.Value);
        Assert.Same(converted.Value, GameplayBoundaryResponseMigration.Status(converted.Value, catalog).Value);
        var migrated = state with { StatusEffects = state.StatusEffects.SetItem("enemy", [status with { Definition = converted.Value }]) };
        var lifecycle = new CombatStatusLifecycle(fixture.Executor);
        var historical = lifecycle.Process(run, state, StatusTriggerBoundary.EndActivation, "enemy");
        Assert.True(historical.IsSuccess, historical.IsFailure ? historical.Error : null);
        var input = CanonicalJson.ComputeHash(new { run, migrated }); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var result = lifecycle.ProcessObserved(run, migrated, StatusTriggerBoundary.EndActivation,
                GameplayObservedExecutorTests.ObservationFor(run, migrated), "enemy");
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(historical.Value.Combat.GetActor("enemy")!.GetResource("focus")!.Current,
                result.Value.Execution.Combat.GetActor("enemy")!.GetResource("focus")!.Current);
            Assert.Equal(CanonicalJson.ComputeHash(historical.Value.Combat.Determinism), CanonicalJson.ComputeHash(result.Value.Execution.Combat.Determinism));
            Assert.Single(result.Value.Execution.Events, item => item.Kind == CombatStatusLifecycleEventKind.Triggered);
            Assert.Equal(historical.Value.Events.Select(item => item.TriggerId), result.Value.Execution.Events.Select(item => item.TriggerId));
            Assert.Equal(historical.Value.Combat.StatusEffects["enemy"].Select(item => item.Duration),
                result.Value.Execution.Combat.StatusEffects["enemy"].Select(item => item.Duration));
            hashes.Add(CanonicalJson.ComputeHash(result.Value.Execution));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(new { run, migrated }));
    }

    [Fact]
    public void MigrationRejectsMixedOrUnsupportedAuthoringWithoutChangingItsInput()
    {
        var catalog = GameplayCoreCatalog.Create("setting", "revision").Value;
        var legacy = new Core.StatusEffects.StatusEffectDefinition { StatusId = "test", Triggers = [new() {
            TriggerId = "tick", Boundary = "EndActivation", Effects = [new() { Type = EffectType.REMOVE_STATUS, StatusId = "absent" }] }] };
        var input = CanonicalJson.ComputeHash(legacy);
        Assert.True(GameplayBoundaryResponseMigration.Status(legacy with { Responses = [GameplayResponseCompilationTests.Definition()] }, catalog).IsFailure);
        Assert.True(GameplayBoundaryResponseMigration.Status(legacy with { Triggers = [legacy.Triggers[0] with { Boundary = "invented" }] }, catalog).IsFailure);
        Assert.Equal(input, CanonicalJson.ComputeHash(legacy));
        var response = GameplayBoundaryResponseMigration.Status(legacy, catalog).Value.Responses!.Value[0];
        Assert.True(GameplayResponseCompiler.Compile(response, catalog).IsFailure); // only a lifecycle host can authorize this schedule
    }

    [Fact]
    public void MigratedRelicRetainsInheritedStackVariablesAndRunsOnlyAtItsBoundary()
    {
        var context = GameplayResponseCompilationTests.Context();
        var catalog = GameplayCoreCatalog.Create(context.Run.SettingId, "revision").Value;
        var definition = new Core.Run.RelicDefinition { RelicId = "test", Triggers = [new() {
            TriggerId = "tick", Boundary = "EndRound", Effects = [new() { Type = EffectType.HEAL,
                Target = EffectTarget.SELF, TargetResource = "focus", FormulaValue = "stacks" }] }] };
        var converted = GameplayBoundaryResponseMigration.Relic(definition, catalog);
        Assert.True(converted.IsSuccess, converted.IsFailure ? converted.Error : null);
        var owner = new Core.Combat.Models.GameplayOwner { Kind = Core.Combat.Models.GameplayOwnerKind.Entity, Id = "hero" };
        var historical = Core.Run.RelicTransitions.Acquire(context.Run, definition, owner).Value.State;
        var migrated = Core.Run.RelicTransitions.Acquire(context.Run, converted.Value, owner).Value.State;
        var lifecycle = new CombatRelicLifecycle(EffectTransactionTests.Executor());
        var expected = lifecycle.Process(historical, context.Combat!, "EndRound");
        Assert.True(expected.IsSuccess, expected.IsFailure ? expected.Error : null);
        var actual = lifecycle.Process(migrated, context.Combat!, "EndRound");
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Single(actual.Value.Events);
        Assert.Equal(11, actual.Value.Combat.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(expected.Value.Combat.GetActor("hero")!.GetResource("focus")!.Current,
            actual.Value.Combat.GetActor("hero")!.GetResource("focus")!.Current);
        var wrongBoundary = lifecycle.Process(migrated, context.Combat!, "StartRound");
        Assert.True(wrongBoundary.IsSuccess, wrongBoundary.IsFailure ? wrongBoundary.Error : null);
        Assert.Empty(wrongBoundary.Value.Events);
        Assert.Equal(10, wrongBoundary.Value.Combat.GetActor("hero")!.GetResource("focus")!.Current);
    }
}
