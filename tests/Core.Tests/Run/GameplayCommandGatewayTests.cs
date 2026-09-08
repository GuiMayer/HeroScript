using System.Text.Json;
using Core.Combat;
using Core.Combat.Models;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Core.Events;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class GameplayCommandGatewayTests
{
    [Fact]
    public void Execute_NormalizesOrdinaryRunCommands_AndUsesCanonicalProcessor()
    {
        var runId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToElement(new { count = 2 });
        var expected = Receipt(runId, commandId, RunCommandTypes.DrawCards);
        var processor = new Mock<IRunCommandProcessor>();
        processor.Setup(service => service.Execute(
                runId,
                It.Is<GameplayCommandEnvelope>(command =>
                    command.Identity.Type == RunCommandTypes.DrawCards &&
                    command.Identity.PayloadHash == CanonicalJson.ComputeHash(payload))))
            .Returns(Result<RunCommandReceipt>.Success(expected));
        var gateway = Create(processor.Object);

        var result = gateway.Execute(
            runId,
            new GameplayCommandEnvelope(
                new RunCommandIdentity(commandId, " draw_cards ", 1, 0),
                payload));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(expected, result.Value.Receipt);
    }

    [Fact]
    public void Execute_RejectsEnvelopeWhosePayloadHashDoesNotMatch()
    {
        var processor = new Mock<IRunCommandProcessor>();
        var gateway = Create(processor.Object);

        var result = gateway.Execute(
            Guid.NewGuid(),
            new GameplayCommandEnvelope(
                new RunCommandIdentity(Guid.NewGuid(), RunCommandTypes.DrawCards, 1, 0, "wrong"),
                JsonSerializer.SerializeToElement(new { count = 1 })));

        Assert.True(result.IsFailure);
        Assert.Contains("payload hash", result.Error, StringComparison.OrdinalIgnoreCase);
        processor.Verify(service => service.Execute(
            It.IsAny<Guid>(), It.IsAny<GameplayCommandEnvelope>()), Times.Never);
    }

    [Fact]
    public void Execute_StartEncounter_RoutesThroughCoordinator_AndReturnsDurableReceipt()
    {
        var runId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var combat = new Core.Combat.Models.CombatState { CombatId = Guid.NewGuid() };
        var state = new RunState { RunId = runId };
        var receipt = Receipt(runId, commandId, RunCommandTypes.StartEncounter) with { State = state };
        var processor = new Mock<IRunCommandProcessor>();
        processor.SetupSequence(service => service.FindReceipt(runId, commandId))
            .Returns(Result<RunCommandReceipt?>.Success(null))
            .Returns(Result<RunCommandReceipt?>.Success(receipt));
        var combats = new Mock<ICombatRunCoordinator>();
        combats.Setup(service => service.StartEncounter(
                runId,
                It.Is<IReadOnlyList<CombatParticipantReference>>(participants =>
                    participants.SequenceEqual(new[]
                    {
                        new CombatParticipantReference("hero", "player_warrior", "player",
                            new ControllerBinding { Kind = ControllerKind.Player }),
                        new CombatParticipantReference("enemy", "enemy_goblin", "opposition",
                            new ControllerBinding { Kind = ControllerKind.AI, PolicyId = "gambit" })
                    })),
                It.Is<IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>>>(values =>
                    values["hero"]["energy"] == 3),
                It.Is<RunCommandIdentity>(identity => identity.CommandId == commandId),
                It.IsAny<JsonElement>()))
            .Returns(Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                RunState = state,
                CombatState = combat
            }));
        var gateway = Create(processor.Object, combats.Object);

        var result = gateway.Execute(
            runId,
            new GameplayCommandEnvelope(
                new RunCommandIdentity(commandId, RunCommandTypes.StartEncounter, 1, 0),
                JsonSerializer.SerializeToElement(new
                {
                    participants = new object[]
                    {
                        new { instanceId = "hero", definitionId = "player_warrior", sideId = "player",
                            controllerBinding = new { kind = "Player" } },
                        new { instanceId = "enemy", definitionId = "enemy_goblin", sideId = "opposition",
                            controllerBinding = new { kind = "AI", policyId = "gambit" } }
                    },
                    initialResourceValues = new Dictionary<string, IReadOnlyDictionary<string, float>>
                    {
                        ["hero"] = new Dictionary<string, float> { ["energy"] = 3 }
                    }
                })));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(combat, result.Value.CombatState);
        Assert.Equal(receipt, result.Value.Receipt);
        processor.Verify(service => service.Execute(
            It.IsAny<Guid>(), It.IsAny<GameplayCommandEnvelope>()), Times.Never);
    }

    [Fact]
    public void Execute_PlayCard_RoutesImmutableInstanceAndTargetsThroughCoordinator()
    {
        var runId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var combatId = Guid.NewGuid();
        var cardInstanceId = Guid.NewGuid();
        var combat = new CombatState
        {
            CombatId = combatId,
            Actors = new Dictionary<string, CombatActorState>
            {
                ["hero"] = new() { InstanceId = "hero", SideId = "player", ControllerBinding = new() { Kind = ControllerKind.Player } }
            }
        };
        var run = new RunState
        {
            RunId = runId,
            ActiveEncounterId = combatId,
            Encounters = [new RunEncounterState { NodeId = "combat", Combat = combat }]
        };
        var receipt = Receipt(runId, commandId, GameplayCommandTypes.PlayCard) with { State = run };
        var processor = new Mock<IRunCommandProcessor>();
        processor.SetupSequence(service => service.FindReceipt(runId, commandId))
            .Returns(Result<RunCommandReceipt?>.Success(null))
            .Returns(Result<RunCommandReceipt?>.Success(receipt));
        var runs = new Mock<IRunManager>();
        runs.Setup(service => service.GetRun(runId))
            .Returns(Result<RunState>.Success(run));
        var combats = new Mock<ICombatRunCoordinator>();
        combats.Setup(service => service.ExecuteAction(
                combatId,
                It.Is<CombatActionCommand>(action =>
                    action.ActionType == ActionType.PLAY_CARD &&
                    action.CardInstanceId == cardInstanceId &&
                    action.ActorId == "hero" &&
                    action.TargetIds.SequenceEqual(new[] { "enemy_2", "enemy_1" }) &&
                    action.RunId == runId),
                It.Is<RunCommandIdentity>(identity => identity.CommandId == commandId),
                It.IsAny<JsonElement>()))
            .Returns(Result<CombatRunActionResult>.Success(new CombatRunActionResult
            {
                RunState = run,
                CombatState = combat
            }));
        var gateway = Create(processor.Object, combats.Object, runs.Object);

        var result = gateway.Execute(
            runId,
            new GameplayCommandEnvelope(
                new RunCommandIdentity(commandId, GameplayCommandTypes.PlayCard, 1, 0),
                JsonSerializer.SerializeToElement(new
                {
                    actorId = "hero",
                    cardInstanceId,
                    targetIds = new[] { "enemy_2", "enemy_1" }
                })),
            combatId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(receipt, result.Value.Receipt);
        combats.VerifyAll();
    }

    private static GameplayCommandGateway Create(
        IRunCommandProcessor processor,
        ICombatRunCoordinator? combats = null,
        IRunManager? runs = null)
    {
        return new GameplayCommandGateway(
            processor,
            runs ?? Mock.Of<IRunManager>(),
            combats ?? Mock.Of<ICombatRunCoordinator>(),
            new GameEventContextAccessor());
    }

    private static RunCommandReceipt Receipt(Guid runId, Guid commandId, string type) => new()
    {
        CommandId = commandId,
        CommandType = type,
        Sequence = 2,
        Step = 1,
        State = new RunState { RunId = runId },
        JournalEntry = new RunJournalEntry()
    };
}
