using Core.Combat.Flow;
using Core.Combat.TurnPhase;
using Core.Content.Migrations;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Math;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class PhaseResponseMigrationTests
{
    [Fact]
    public void PhaseMigrationKeepsEntryExitNumbersAndCursorWithoutDuplicatingEffects()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var combat = GameplayResponseCompilationTests.Context().Combat! with { ActivationState = new() { ActiveActorId = "hero" } };
        var heal = EffectSequenceBudgetTests.Damage(2, 1) with { Type = EffectType.HEAL, Target = EffectTarget.SELF };
        var damage = EffectSequenceBudgetTests.Damage(1, 1) with { Target = EffectTarget.SELF };
        var sequence = new PhaseSequenceDefinition { SequenceId = "test", EntryPhaseId = "start", Phases = [
            new() { PhaseId = "start", Role = PhaseRole.Start, EntryEffects = [heal], Edges = [new() {
                EdgeId = "to-main", TargetPhaseId = "main", Trigger = PhaseEdgeTrigger.Automatic }] },
            new() { PhaseId = "main", Role = PhaseRole.Middle, Order = 1, ExitEffects = [damage], Edges = [new() {
                EdgeId = "to-end", TargetPhaseId = "end", Trigger = PhaseEdgeTrigger.ActivationExit }] },
            new() { PhaseId = "end", Role = PhaseRole.End, Order = 2 }] };
        var catalog = GameplayCoreCatalog.Create(fixture.Run.SettingId, "revision").Value;
        var converted = GameplayBoundaryResponseMigration.Phases(sequence, catalog);
        Assert.True(converted.IsSuccess, converted.IsFailure ? converted.Error : null);
        Assert.Same(converted.Value, GameplayBoundaryResponseMigration.Phases(converted.Value, catalog).Value);
        Assert.All(converted.Value.Phases, phase => { Assert.Empty(phase.EntryEffects); Assert.Empty(phase.ExitEffects); });
        var reducer = new PhaseGraphReducer(Mock.Of<IRuntimeFormulaEvaluator>(), fixture.Executor);
        var expectedEnter = reducer.Enter(fixture.Run, combat, sequence);
        Assert.True(expectedEnter.IsSuccess, expectedEnter.IsFailure ? expectedEnter.Error : null);
        var expectedExit = reducer.Exit(expectedEnter.Value.Run, expectedEnter.Value.Combat, sequence);
        Assert.True(expectedExit.IsSuccess, expectedExit.IsFailure ? expectedExit.Error : null);
        var input = CanonicalJson.ComputeHash(new { fixture.Run, combat, sequence }); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var entered = reducer.Enter(fixture.Run, combat, converted.Value);
            Assert.True(entered.IsSuccess, entered.IsFailure ? entered.Error : null);
            var exited = reducer.Exit(entered.Value.Run, entered.Value.Combat, converted.Value);
            Assert.True(exited.IsSuccess, exited.IsFailure ? exited.Error : null);
            Assert.Equal("main", entered.Value.Combat.PhaseState!.Cursor);
            Assert.Equal("end", exited.Value.Combat.PhaseState!.Cursor);
            Assert.Single(entered.Value.Applications); Assert.Single(exited.Value.Applications);
            Assert.Equal(expectedEnter.Value.Combat.GetActor("hero")!.GetResource("focus")!.Current,
                entered.Value.Combat.GetActor("hero")!.GetResource("focus")!.Current);
            Assert.Equal(expectedExit.Value.Combat.GetActor("hero")!.GetResource("focus")!.Current,
                exited.Value.Combat.GetActor("hero")!.GetResource("focus")!.Current);
            Assert.Equal(CanonicalJson.ComputeHash(expectedExit.Value.Combat.Determinism), CanonicalJson.ComputeHash(exited.Value.Combat.Determinism));
            hashes.Add(CanonicalJson.ComputeHash(new { entered.Value, exit = exited.Value }));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(new { fixture.Run, combat, sequence }));
    }

    [Fact]
    public void AmbiguousPhaseMigrationIsRejected()
    {
        var definition = new PhaseSequenceDefinition { SequenceId = "test", Phases = [new() { PhaseId = "main",
            EntryEffects = [new() { Type = EffectType.REMOVE_STATUS, StatusId = "test" }],
            Responses = [GameplayResponseCompilationTests.Definition()] }] };
        Assert.True(GameplayBoundaryResponseMigration.Phases(definition, GameplayResponseCompilationTests.Catalog()).IsFailure);
    }
}
