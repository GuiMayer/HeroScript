using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunMapTransitionsTests
{
    [Fact]
    public void Create_CopiesAndValidatesPinnedMapContent()
    {
        var nextNodeIds = new List<string> { "reward" };
        var definitions = new List<RunMapNodeDefinition>
        {
            new() { NodeId = "start", Activity = Encounter(), NextNodeIds = nextNodeIds },
            new() { NodeId = "reward", Activity = CardSelection() }
        };

        var result = RunMapTransitions.Create(definitions);
        nextNodeIds.Add("injected");
        definitions.Add(new RunMapNodeDefinition { NodeId = "injected" });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.Nodes.Count);
        Assert.Equal(new[] { "reward" }, result.Value.Nodes[0].NextNodeIds);
        Assert.Equal(new[] { "start" }, result.Value.VisitedNodeIds);

        var dangling = RunMapTransitions.Create(
        [
            new RunMapNodeDefinition { NodeId = "start", NextNodeIds = ["missing"] }
        ]);
        Assert.True(dangling.IsFailure);
        Assert.Contains("unknown node", dangling.Error);
    }

    [Fact]
    public void ResolveAndAdvance_AreImmutableDeterministicTransitions()
    {
        var initial = CreateRun();
        var sameInitial = CreateRun();

        var firstResolve = RunMapTransitions.Resolve(initial, "start");
        var secondResolve = RunMapTransitions.Resolve(sameInitial, "start");
        var firstAdvance = RunMapTransitions.Advance(firstResolve.Value.State, "reward");
        var secondAdvance = RunMapTransitions.Advance(secondResolve.Value.State, "reward");

        Assert.True(firstResolve.IsSuccess, firstResolve.IsFailure ? firstResolve.Error : null);
        Assert.True(firstAdvance.IsSuccess, firstAdvance.IsFailure ? firstAdvance.Error : null);
        Assert.Empty(initial.Map.ResolvedNodeIds);
        Assert.Equal("start", initial.CurrentNodeId);
        Assert.Equal("reward", firstAdvance.Value.State.CurrentNodeId);
        Assert.Equal(new[] { "reward", "start" }, firstAdvance.Value.State.Map.VisitedNodeIds);
        Assert.Equal(new[] { "start" }, firstAdvance.Value.State.Map.ResolvedNodeIds);
        Assert.Equal(initial.Determinism.Step + 2, firstAdvance.Value.State.Determinism.Step);
        Assert.Equal(
            CanonicalJson.ComputeHash(firstAdvance.Value.State),
            CanonicalJson.ComputeHash(secondAdvance.Value.State));
    }

    [Fact]
    public void Advance_RequiresResolvedCurrentNodeAndLegalUnvisitedTarget()
    {
        var initial = CreateRun();

        var unresolved = RunMapTransitions.Advance(initial, "reward");
        var resolved = RunMapTransitions.Resolve(initial, "start").Value.State;
        var illegal = RunMapTransitions.Advance(resolved, "boss");

        Assert.True(unresolved.IsFailure);
        Assert.Contains("must be resolved", unresolved.Error);
        Assert.True(illegal.IsFailure);
        Assert.Contains("Illegal map transition", illegal.Error);
    }

    [Fact]
    public void AvailableCommands_FollowCurrentMapState()
    {
        var initial = CreateRun();
        var beforeResolution = Assert.Single(RunMapTransitions.GetAvailableCommands(initial));
        var resolved = RunMapTransitions.Resolve(initial, "start").Value.State;
        var afterResolution = Assert.Single(RunMapTransitions.GetAvailableCommands(resolved));
        var terminal = RunMapTransitions.Advance(resolved, "reward").Value.State;

        Assert.Equal(RunCommandTypes.StartEncounter, beforeResolution.Type);
        Assert.Empty(beforeResolution.TargetNodeIds);
        Assert.Equal(RunCommandTypes.AdvanceNode, afterResolution.Type);
        Assert.Equal(new[] { "reward" }, afterResolution.TargetNodeIds);
        Assert.Equal(RunCommandTypes.CreateCardSelection,
            Assert.Single(RunMapTransitions.GetAvailableCommands(terminal)).Type);
    }

    private static RunState CreateRun()
    {
        var map = RunMapTransitions.Create(
        [
            new RunMapNodeDefinition
            {
                NodeId = "start",
                Activity = Encounter(),
                NextNodeIds = ["reward"]
            },
            new RunMapNodeDefinition
            {
                NodeId = "reward",
                Activity = CardSelection(),
                NextNodeIds = ["boss"]
            },
            new RunMapNodeDefinition { NodeId = "boss", Activity = Encounter() }
        ]).Value;

        return new RunState
        {
            RunId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            CurrentNodeId = "start",
            Map = map,
            Determinism = DeterministicContext.Create(42, new string('a', 64))
        };
    }

    private static RunActivityDefinition Encounter() => new() { Type = RunActivityType.Encounter };
    private static RunActivityDefinition CardSelection() => new()
    {
        Type = RunActivityType.CardSelection,
        DefinitionId = "reward"
    };
}
