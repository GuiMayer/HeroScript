using System.Text.Json;
using Core.Combat;
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
                It.Is<RunCommand>(command =>
                    command.Identity.Type == RunCommandTypes.DrawCards &&
                    command.Identity.PayloadHash == CanonicalJson.ComputeHash(payload))))
            .Returns(Result<RunCommandReceipt>.Success(expected));
        var gateway = Create(processor.Object);

        var result = gateway.Execute(
            runId,
            new RunCommand(
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
            new RunCommand(
                new RunCommandIdentity(Guid.NewGuid(), RunCommandTypes.DrawCards, 1, 0, "wrong"),
                JsonSerializer.SerializeToElement(new { count = 1 })));

        Assert.True(result.IsFailure);
        Assert.Contains("payload hash", result.Error, StringComparison.OrdinalIgnoreCase);
        processor.Verify(service => service.Execute(
            It.IsAny<Guid>(), It.IsAny<RunCommand>()), Times.Never);
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
                "hero",
                It.Is<IReadOnlyList<string>>(enemies => enemies.SequenceEqual(new[] { "enemy" })),
                3,
                It.Is<RunCommandIdentity>(identity => identity.CommandId == commandId)))
            .Returns(Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                RunState = state,
                CombatState = combat
            }));
        var gateway = Create(processor.Object, combats.Object);

        var result = gateway.Execute(
            runId,
            new RunCommand(
                new RunCommandIdentity(commandId, RunCommandTypes.StartEncounter, 1, 0),
                JsonSerializer.SerializeToElement(new
                {
                    heroId = "hero",
                    enemyIds = new[] { "enemy" },
                    initialEnergy = 3
                })));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(combat, result.Value.CombatState);
        Assert.Equal(receipt, result.Value.Receipt);
        processor.Verify(service => service.Execute(
            It.IsAny<Guid>(), It.IsAny<RunCommand>()), Times.Never);
    }

    private static GameplayCommandGateway Create(
        IRunCommandProcessor processor,
        ICombatRunCoordinator? combats = null)
    {
        return new GameplayCommandGateway(
            processor,
            Mock.Of<IRunManager>(),
            combats ?? Mock.Of<ICombatRunCoordinator>(),
            Mock.Of<IActionManager>(),
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
