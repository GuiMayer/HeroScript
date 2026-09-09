using API.Controllers;
using Core.Combat.Flow;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class CombatResolutionControllerTests
{
    [Fact]
    public void Get_ReturnsDurableAnimationQueue()
    {
        var combatId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var resolution = new CombatResolutionRecord
        {
            CommandId = commandId,
            CombatId = combatId,
            CommandType = "END_TURN",
            Mode = AnimationFrameMode.CompactWithSnapshotLookup,
            RootSequence = 3,
            Frames =
            [
                new CombatAnimationFrame
                {
                    FrameId = Guid.NewGuid(),
                    Index = 0,
                    RunSequence = 3,
                    SnapshotSequence = 3,
                    TransitionType = "combat.activation.ended"
                }
            ]
        };
        var run = new RunState
        {
            RunId = Guid.NewGuid(),
            CombatResolutions = new Dictionary<Guid, CombatResolutionRecord>
            {
                [commandId] = resolution
            }
        };
        var runs = new Mock<IRunManager>();
        runs.Setup(manager => manager.GetRunByCombat(combatId))
            .Returns(Core.Common.Result<RunState>.Success(run));
        var controller = new CombatResolutionController(
            runs.Object,
            Mock.Of<ILogger<CombatResolutionController>>());

        var result = controller.Get(combatId, commandId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(resolution, ok.Value);
    }
}
