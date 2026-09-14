using System.Collections.Immutable;
using System.Text.Json;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;
using Core.Run.Dialogue;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class DialogueTests
{
    [Fact]
    public void UnstartedNarrativeIsAbsentFromCanonicalSnapshots_AndPastDialoguesExposeNoLegalChoices()
    {
        var state = State();
        var json = CanonicalJson.Serialize(state);
        Assert.DoesNotContain("\"narrative\"", json);
        Assert.DoesNotContain("\"dialogues\"", json);
        var started = DialogueTransitions.Start(state, Definition(), null).Value;
        var left = started with { CurrentNodeId = "another-stop" };
        Assert.All(DialogueTransitions.View(left, left.Dialogues[0]).Choices, choice => Assert.False(choice.Available));
    }

    [Fact]
    public void BranchesAndFlags_AreImmutableDeterministicAndSurviveSerialization()
    {
        var original = State();
        var definition = Definition();
        var first = Play(original, definition);
        var second = Play(original, definition);
        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(second));
        Assert.Empty(original.Dialogues);
        Assert.Empty(original.NarrativeFlags);
        Assert.Equal(30, original.ResourceState.Current("gold"));
        Assert.Equal(10, first.ResourceState.Current("gold"));
        Assert.Equal("yes", first.NarrativeFlags["met"]);
        Assert.True(first.Dialogues[0].Completed);
        var json = JsonSerializer.Serialize(first);
        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(JsonSerializer.Deserialize<RunState>(json)));
    }

    [Fact]
    public void InvalidStaleAndRepeatedChoices_DoNotChangeTheSnapshot()
    {
        var run = DialogueTransitions.Start(State(), Definition(), null).Value;
        var id = run.Dialogues[0].DialogueInstanceId;
        var hash = CanonicalJson.ComputeHash(run);
        Assert.True(DialogueTransitions.Choose(run, new(id, "wrong", "buy"), null).IsFailure);
        Assert.True(DialogueTransitions.Choose(run, new(Guid.Empty, "hello", "buy"), null).IsFailure);
        Assert.True(DialogueTransitions.Choose(run, new(id, "hello", "secret"), null).IsFailure);
        Assert.True(DialogueTransitions.Choose(run, new(id, "hello", "unknown"), null).IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
        run = DialogueTransitions.Choose(run, new(id, "hello", "buy"), null).Value;
        Assert.True(DialogueTransitions.Choose(run, new(id, "hello", "buy"), null).IsFailure);
        Assert.Equal(10, run.ResourceState.Current("gold"));
        Assert.False(DialogueTransitions.View(run, run.Dialogues[0]).Choices.Single(choice => choice.ChoiceId == "buy").Available);
    }

    [Fact]
    public void LaterEffectFailure_RollsBackCostFlagsAndEarlierEffects()
    {
        var definition = Definition();
        var node = definition.Nodes[0];
        definition = definition with { Nodes = [node with { Choices = node.Choices.SetItem(0,
            node.Choices[0] with { NextNodeId = "end", Effects = [new() { EffectId = "choice" }] }) },
            new() { NodeId = "end", Text = Text("End"), EntryEffects = [new() { EffectId = "fail" }], Choices = [new() { ChoiceId = "close", Text = Text("Close") }] }] };
        var effects = new Mock<IRunActivityEffectExecutor>();
        effects.Setup(item => item.Execute(It.IsAny<RunState>(), It.IsAny<RunMapNodeState>(), RunActivityBoundary.Entry))
            .Returns((RunState state, RunMapNodeState boundary, RunActivityBoundary _) => boundary.EntryEffects[0].EffectId == "fail"
                ? Result<RunActivityEffectResult>.Failure("later effect rejected")
                : Result<RunActivityEffectResult>.Success(new() { State = state with { NarrativeFlags = state.NarrativeFlags.SetItem("effect", "applied") } }));
        var run = DialogueTransitions.Start(State(), definition, effects.Object).Value;
        var hash = CanonicalJson.ComputeHash(run);
        var rejected = DialogueTransitions.Choose(run, new(run.Dialogues[0].DialogueInstanceId, "hello", "buy"), effects.Object);
        Assert.True(rejected.IsFailure);
        Assert.Equal("later effect rejected", rejected.Error);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
        Assert.Empty(run.NarrativeFlags);
        Assert.Equal(30, run.ResourceState.Current("gold"));
    }

    [Fact]
    public void ConditionsCompose_AndHiddenChoicesOnlyAppearWhenSatisfied()
    {
        var run = DialogueTransitions.Start(State(), Definition(), null).Value;
        var initial = DialogueTransitions.View(run, run.Dialogues[0]);
        Assert.DoesNotContain(initial.Choices, choice => choice.ChoiceId == "secret");
        run = DialogueTransitions.Choose(run, new(run.Dialogues[0].DialogueInstanceId, "hello", "buy"), null).Value;
        Assert.Contains(DialogueTransitions.View(run, run.Dialogues[0]).Choices, choice => choice.ChoiceId == "secret" && choice.Available);
        Assert.True(DialogueTransitions.Meets(run, new() { Kind = DialogueConditionKind.All, Children = [
            new() { Kind = DialogueConditionKind.Flag, Id = "met", Value = "yes" },
            new() { Kind = DialogueConditionKind.Not, Children = [new() { Kind = DialogueConditionKind.ResourceAtLeast, Id = "gold", Amount = 20 }] }
        ] }));
    }

    [Fact]
    public void ProgressionRequiresCompletion_AndPreventsCrossActivityCommands()
    {
        var progression = new RunProgressionService(RunActivityRegistry.CreateDefault());
        var run = DialogueTransitions.Start(State(), Definition(), null).Value;
        Assert.True(progression.CanResolve(run, run.Map.Nodes[0]).IsFailure);
        var commands = progression.GetAvailableCommands(run).Value;
        Assert.Contains(commands, command => command.Type == RunCommandTypes.ChooseDialogueOption);
        Assert.DoesNotContain(commands, command => command.Type == RunCommandTypes.StartDialogue);
        run = DialogueTransitions.Choose(run, new(run.Dialogues[0].DialogueInstanceId, "hello", "leave"), null).Value;
        Assert.True(progression.CanResolve(run, run.Map.Nodes[0]).IsSuccess);
        Assert.True(DialogueTransitions.Start(run, Definition(), null).IsFailure);
        Assert.True(DialogueTransitions.Choose(run, new(run.Dialogues[0].DialogueInstanceId, "hello", "leave"), null).IsFailure);
    }

    [Fact]
    public void GraphValidationRejectsBrokenReferencesDuplicateChoicesAndTrappedCycles()
    {
        var definition = Definition();
        Assert.Empty(DialogueDefinitionValidator.Validate(definition));
        Assert.NotEmpty(DialogueDefinitionValidator.Validate(definition with { StartNodeId = "missing" }));
        var node = definition.Nodes[0];
        Assert.NotEmpty(DialogueDefinitionValidator.Validate(definition with { Nodes = [node with { Choices = [node.Choices[0], node.Choices[0]] }] }));
        Assert.NotEmpty(DialogueDefinitionValidator.Validate(definition with { Nodes = [node with { Choices = [node.Choices[0] with { NextNodeId = "missing" }] }] }));
        Assert.NotEmpty(DialogueDefinitionValidator.Validate(definition with { Nodes = [node with { Choices = [node.Choices[0]] }] }));
    }

    private static RunState Play(RunState state, DialogueDefinition definition)
    {
        var run = DialogueTransitions.Start(state, definition, null).Value;
        var id = run.Dialogues[0].DialogueInstanceId;
        run = DialogueTransitions.Choose(run, new(id, "hello", "buy"), null).Value;
        return DialogueTransitions.Choose(run, new(id, "hello", "secret"), null).Value;
    }

    private static DialogueText Text(string text) => new() { Text = text };
    private static DialogueDefinition Definition() => new()
    {
        DialogueId = "test", Title = Text("Test"), StartNodeId = "hello", Nodes = [new()
        {
            NodeId = "hello", Text = Text("Hello"), Choices = [
                new() { ChoiceId = "buy", Text = Text("Buy"), NextNodeId = "hello", Once = true,
                    Costs = [new() { ResourceId = "gold", Amount = 20 }], SetFlags = ImmutableDictionary<string, string>.Empty.Add("met", "yes") },
                new() { ChoiceId = "secret", Text = Text("Secret"), HideWhenUnavailable = true, Condition = new() { Kind = DialogueConditionKind.Flag, Id = "met", Value = "yes" } },
                new() { ChoiceId = "leave", Text = Text("Leave") }
            ]
        }]
    };

    private static RunState State() => new()
    {
        RunId = Guid.Parse("a0000000-0000-0000-0000-000000000001"), CurrentNodeId = "keeper",
        Determinism = DeterministicContext.Create(42, new string('a', 64)),
        Map = new() { Nodes = [new() { NodeId = "keeper", Activity = new() { Type = RunActivityType.Dialogue, DefinitionId = "test" } }] },
        ResourceState = new() { OwnerId = "player", Resources = new Dictionary<string, ResourcePool> { ["gold"] = ResourcePool.Materialize(new() { ResourceId = "gold", DisplayName = "Gold", DefaultMax = 100, DefaultCurrent = 30 }) } }
    };
}
