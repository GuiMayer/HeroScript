using System.Text.Json;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardSelectionTerminalChoiceTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void TerminalPolicyIsCapturedAndLegacyDecompositionKeepsOtherOptionsOpen(bool? configured, bool complete)
    {
        var run = Offer(configured);
        var before = CanonicalJson.ComputeHash(run);
        var selection = run.CardSelections[0];
        var result = CardSelectionTransitions.Decompose(run, selection.SelectionInstanceId, "first");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(complete, result.Value.Value.Completed);
        Assert.Equal(3, result.Value.State.ResourceState.Current("parts"));
        Assert.Equal(run.Determinism.RandomState, result.Value.State.Determinism.RandomState);
        Assert.Equal(run.Determinism.IdSequence, result.Value.State.Determinism.IdSequence);
        var commands = new RunProgressionService(RunActivityRegistry.CreateDefault()).GetAvailableCommands(result.Value.State).Value;
        Assert.Equal(complete, commands.Any(command => command.Type == RunCommandTypes.ResolveNode));
        Assert.Equal(!complete, commands.Any(command => command.Type == RunCommandTypes.PickCardReward));
        Assert.True(CardSelectionTransitions.Decompose(result.Value.State, selection.SelectionInstanceId, "first").IsFailure);
        var other = CardSelectionTransitions.Decompose(result.Value.State, selection.SelectionInstanceId, "second");
        Assert.Equal(!complete, other.IsSuccess);
        if (complete)
        {
            Assert.True(CardSelectionTransitions.Pick(result.Value.State, selection.SelectionInstanceId, ["second"]).IsFailure);
            Assert.True(CardSelectionTransitions.Reroll(result.Value.State, selection.SelectionInstanceId, selection.Options).IsFailure);
        }
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.Equal(0, run.ResourceState.Current("parts"));
        Assert.False(selection.Completed);
        if (configured == null) Assert.DoesNotContain("CompletesSelection", JsonSerializer.Serialize(selection));
        var restored = JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(result.Value.State))!;
        Assert.Equal(CanonicalJson.ComputeHash(result.Value.State), CanonicalJson.ComputeHash(restored));
        Assert.Equal(complete, restored.CardSelections[0].Completed);
    }

    [Fact]
    public void MissingPayoutResourceCannotCloseTheOfferOrPublishPartialReward()
    {
        var run = Offer(true);
        var selection = run.CardSelections[0];
        var broken = selection with { Options = [selection.Options[0] with { DecomposeRewards = [new() { ResourceId = "missing", Amount = 3 }] }] };
        run = run with { CardSelections = [broken] };
        var before = CanonicalJson.ComputeHash(run);
        Assert.True(CardSelectionTransitions.Decompose(run, selection.SelectionInstanceId, "first").IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.False(run.CardSelections[0].Completed);
    }

    private static RunState Offer(bool? terminal)
    {
        var run = new RunState { RunId = Guid.Parse("82200000-0000-4000-8000-000000000001"), CurrentNodeId = "reward",
            Determinism = DeterministicContext.Create(822, "reward-choice"),
            ResourceState = new() { OwnerId = "wallet", Resources = new Dictionary<string, ResourcePool>
                { ["parts"] = ResourcePool.Materialize(new() { ResourceId = "parts", DisplayName = "Parts", DefaultMax = 100 }, 0, 100) } },
            Map = new() { Nodes = [new() { NodeId = "reward", Activity = new() { Type = RunActivityType.CardSelection, DefinitionId = "reward" } }] } };
        return CardSelectionTransitions.Create(run, new() { SelectionId = "reward", PickCount = 1,
                Decompose = new() { Enabled = true, CompletesSelection = terminal } },
            new[] { "first", "second" }.Select(id => new CardSelectionOptionState { CardId = id,
                DecomposeRewards = [new() { ResourceId = "parts", Amount = 3 }] }).ToArray(), nodeId: "reward").State;
    }
}
