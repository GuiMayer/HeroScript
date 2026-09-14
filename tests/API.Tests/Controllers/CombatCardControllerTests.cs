using API.Controllers;
using API.Models;
using API.Services;
using Core.Common;
using Core.Run;
using Core.Run.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class CombatCardControllerTests
{
    [Fact]
    public void Evaluate_ForwardsCardAndTargetContext()
    {
        var combatId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var service = new Mock<ICardInspectionService>();
        service.Setup(item => item.Inspect(It.Is<CardInspectionRequest>(request =>
                request.CombatId == combatId &&
                request.CardInstanceId == cardId &&
                request.ActorId == "hero" &&
                request.SelectedTargetIds.SequenceEqual(new[] { "enemy" }))))
            .Returns(Result<CardInspectionResult>.Success(new CardInspectionResult
            {
                ResolutionFingerprint = "preview"
            }));
        var controller = new CombatCardController(
            service.Object,
            Runs(combatId),
            new ToolAccessPolicy(new ToolAccessSettings { Profile = "dev_modder" }),
            Mock.Of<ILogger<CombatCardController>>());

        var response = controller.Evaluate(combatId, cardId, "hero", ["enemy"]);

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Equal("preview", Assert.IsType<CardInspectionResult>(ok.Value).ResolutionFingerprint);
    }

    [Fact]
    public void EvaluateHand_ReturnsOneBatchProjection()
    {
        var combatId = Guid.NewGuid();
        var service = new Mock<ICardInspectionService>();
        service.Setup(item => item.InspectHand(
                combatId,
                "hero",
                It.IsAny<string[]>(),
                null,
                InspectionDetailLevel.Full))
            .Returns(Result<IReadOnlyList<CardInspectionResult>>.Success(
                [new CardInspectionResult(), new CardInspectionResult()]));
        var controller = new CombatCardController(
            service.Object,
            Runs(combatId),
            new ToolAccessPolicy(new ToolAccessSettings { Profile = "dev_modder" }),
            Mock.Of<ILogger<CombatCardController>>());

        var response = controller.EvaluateHand(combatId, "hero", []);

        Assert.IsType<OkObjectResult>(response);
        service.Verify(item => item.InspectHand(
            combatId,
            "hero",
            It.IsAny<string[]>(),
            null,
            InspectionDetailLevel.Full), Times.Once);
    }

    private static IRunQueryService Runs(Guid combatId)
    {
        var runs = new Mock<IRunQueryService>();
        runs.Setup(item => item.GetRunByCombat(combatId)).Returns(Result<RunState>.Success(new RunState
        {
            ResolvedMode = new ResolvedGameMode
            {
                CapabilityPolicy = new CapabilityPolicyDefinition
                {
                    CardInspectionDetail = InspectionDetailLevel.Full
                }
            }
        }));
        return runs.Object;
    }
}
