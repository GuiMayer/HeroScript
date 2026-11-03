using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardPaymentQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectionUsesOneVersionAndNeverReturnsHiddenIdentitiesOrExecutionOrder(bool hidden)
    {
        var combatId = Guid.NewGuid(); var playing = Guid.NewGuid(); var fee = Guid.NewGuid(); var hiddenId = Guid.NewGuid();
        var combat = new CombatState { CombatId = combatId, Determinism = DeterministicContext.Create(17, "content") };
        var run = new RunState { RunId = Guid.NewGuid(), Sequence = 3, ActiveEncounterId = combatId,
            Encounters = [new() { Combat = combat }], Determinism = DeterministicContext.Create(16, "content") };
        var reads = new Mock<IRunQueryService>(); reads.Setup(r => r.GetRunByCombat(combatId)).Returns(Result<RunState>.Success(run));
        var faces = new Mock<ICardPresentationQueryService>();
        faces.Setup(p => p.Read(run, false, combat, "hero", InspectionDetailLevel.Resolved)).Returns(Result<CardPresentationBatch>.Success(new(run.RunId, 3, "content",
            [new() { Identity = new() { CardInstanceId = playing } }, new() { Identity = new() { CardInstanceId = fee } }])));
        var inspections = new Mock<ICardInspectionService>();
        inspections.Setup(p => p.Inspect(run, combat, It.IsAny<CardInspectionRequest>())).Returns(Result<CardInspectionResult>.Success(new()
        {
            IsPlayable = true, Version = new() { RunId = run.RunId, RunSequence = 3, CombatId = combatId, ContentRevision = "content" },
            Evaluation = new() { IsLegal = true, PaymentRequirements = [new("cost", "fee", null, "custom", "destination", 1, false,
                CardPaymentOrder.SourceOrder, hidden ? [fee, hiddenId] : [fee], null)],
                ZonePayments = [new("cost", "fee", "transfer", [fee], false)] },
            PreviewApplications = [new() { CardInstanceIds = [hiddenId, fee], CardZoneSteps = [new() { SourceOrderBefore = [hiddenId, fee] }] }]
        }));
        var service = new CardPaymentQueryService(reads.Object, inspections.Object, faces.Object);
        var request = new CardPaymentPreviewRequest(3, 0, new() { ActorId = "hero", ActionType = ActionType.PLAY_CARD, CardInstanceId = playing });
        var hash = CanonicalJson.ComputeHash(run);
        var result = service.Preview(combatId, request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(!hidden, result.Value.IsLegal);
        Assert.Null(result.Value.Evaluation.ZonePayments);
        Assert.DoesNotContain(hiddenId, result.Value.Evaluation.PaymentRequirements!.Value[0].EligibleCardIds);
        Assert.Empty(result.Value.Applications[0].CardZoneSteps);
        Assert.Equal(new[] { fee }, result.Value.Applications[0].CardInstanceIds);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
        Assert.StartsWith(RunCommandErrors.VersionConflictPrefix, service.Preview(combatId, request with { ExpectedSequence = 2 }).Error);
    }
}
