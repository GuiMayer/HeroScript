using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Common;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class ActorDecisionBindingTests
{
    [Theory]
    [InlineData("offense")]
    [InlineData("defense")]
    public void ConfiguredActorRoutineReachesTheSameDecisionPolicyWithoutChangingTheSharedInput(string routine)
    {
        var policy = new Mock<IDecisionPolicy>();
        policy.SetupGet(value => value.PolicyId).Returns("gambit");
        DecisionPolicyRequest? received = null;
        policy.Setup(value => value.Decide(It.IsAny<DecisionPolicyRequest>())).Callback<DecisionPolicyRequest>(value => received = value)
            .Returns(Result<DecisionPolicyResult>.Failure("no legal candidate"));
        var registry = new DecisionPolicyRegistry([policy.Object]);
        var request = new DecisionPolicyRequest(new RunState(), new CombatState(), "support", ["shared-enemy-policy"]);
        var result = registry.Decide(new() { Kind = ControllerKind.AI, PolicyId = "gambit", DecisionIds = [routine] }, request);
        Assert.True(result.IsFailure);
        Assert.Equal(new[] { routine }, received!.DecisionIds);
        Assert.Same(request.Combat, received.Combat);
        Assert.Same(request.Run, received.Run);
        Assert.Equal("support", received.ActorId);
        Assert.Equal(new[] { "shared-enemy-policy" }, request.DecisionIds);
    }
}
