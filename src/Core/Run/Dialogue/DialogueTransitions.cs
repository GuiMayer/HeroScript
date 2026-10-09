using System.Collections.Immutable;
using Core.Common;
using Core.Effects;

namespace Core.Run.Dialogue;

/// <summary>Pure dialogue transitions; the caller commits the whole candidate through the run gateway.</summary>
public static class DialogueTransitions
{
    public static DialogueState? Current(RunState run) => run.Dialogues.LastOrDefault(
        dialogue => dialogue.ActivityNodeId == run.CurrentNodeId);

    public static bool Meets(RunState run, DialogueCondition? condition) => condition == null || condition.Kind switch
    {
        DialogueConditionKind.All => condition.Children.All(child => Meets(run, child)),
        DialogueConditionKind.Any => condition.Children.Any(child => Meets(run, child)),
        DialogueConditionKind.Not => condition.Children.Length == 1 && !Meets(run, condition.Children[0]),
        DialogueConditionKind.Flag => run.NarrativeFlags.TryGetValue(condition.Id, out var flag) && flag == condition.Value,
        DialogueConditionKind.ResourceAtLeast => run.ResourceState.Get(condition.Id) is { } resource && resource.Current >= condition.Amount,
        DialogueConditionKind.HasCard => run.Deck.Topology.Instances.Values.Any(card => card.DefinitionId == condition.Id),
        DialogueConditionKind.HasRelic => run.Relics.Any(relic => relic.DefinitionId == condition.Id),
        DialogueConditionKind.VisitedNode => run.Map.VisitedNodeIds.Contains(condition.Id),
        DialogueConditionKind.ResolvedNode => run.Map.ResolvedNodeIds.Contains(condition.Id),
        _ => false
    };

    public static bool Available(RunState run, DialogueState dialogue, DialogueChoiceDefinition choice) =>
        run.Lifecycle == RunLifecycleState.Active && run.ActiveEncounterId == null &&
        run.CurrentNodeId == dialogue.ActivityNodeId && !run.Map.ResolvedNodeIds.Contains(dialogue.ActivityNodeId) &&
        !dialogue.Completed && Meets(run, choice.Condition) &&
        (!choice.Once || !dialogue.History.Any(visit => visit.NodeId == dialogue.CurrentNodeId && visit.ChoiceId == choice.ChoiceId)) &&
        RunResourceTransitions.Spend(run.ResourceState, choice.Costs, "dialogue:availability").IsSuccess;

    public static DialogueView View(RunState run, DialogueState dialogue)
    {
        var node = dialogue.Definition.Nodes.Single(node => node.NodeId == dialogue.CurrentNodeId);
        var choices = dialogue.Completed ? [] : node.Choices.Select(choice =>
        {
            var available = Available(run, dialogue, choice);
            return (choice, available);
        }).Where(item => item.available || !item.choice.HideWhenUnavailable)
            .Select(item => new DialogueChoiceView(item.choice.ChoiceId, item.choice.Text, item.available,
                item.available ? null : item.choice.UnavailableText, item.choice.Costs)).ToImmutableArray();
        return new(dialogue.DialogueInstanceId, dialogue.Definition.DialogueId, dialogue.ActivityNodeId,
            dialogue.ContentRevision, dialogue.Definition.Title, node.NodeId, node.Speaker, node.Text,
            node.PortraitId, dialogue.Completed, choices, dialogue.History, dialogue.History.Select(visit =>
            {
                var visited = dialogue.Definition.Nodes.Single(item => item.NodeId == visit.NodeId);
                return new DialogueTranscriptLine(visit.NodeId, visit.ChoiceId,
                    visit.ChoiceId == null ? visited.Speaker : new DialogueText(),
                    visit.ChoiceId == null ? visited.Text : visited.Choices.Single(item => item.ChoiceId == visit.ChoiceId).Text);
            }).ToImmutableArray());
    }

    public static Result<RunState> Start(RunState run, DialogueDefinition definition, IRunActivityEffectExecutor? effects)
    {
        var legal = CheckActivity(run);
        if (legal.IsFailure) return Result<RunState>.Failure(legal.Error);
        if (legal.Value.Activity.DefinitionId != definition.DialogueId)
            return Result<RunState>.Failure("Dialogue does not match the current activity");
        if (Current(run) != null) return Result<RunState>.Failure("Dialogue already started for this activity");
        var errors = DialogueDefinitionValidator.Validate(definition);
        if (errors.Count > 0) return Result<RunState>.Failure(string.Join("; ", errors));
        var allocation = run.Determinism.AllocateId("dialogue");
        var dialogue = new DialogueState
        {
            DialogueInstanceId = allocation.Value, ActivityNodeId = run.CurrentNodeId!,
            ContentRevision = run.Determinism.ContentRevision, Definition = definition,
            CurrentNodeId = definition.StartNodeId, History = [new(definition.StartNodeId, null)]
        };
        var candidate = run with { Dialogues = run.Dialogues.Add(dialogue), Determinism = allocation.Context };
        var initial = definition.Nodes.Single(node => node.NodeId == definition.StartNodeId);
        var applied = Execute(candidate, dialogue, "entry", initial.EntryEffects, effects, initial.EntryEffectOwner);
        return applied.IsFailure ? applied : Result<RunState>.Success(applied.Value with { Determinism = applied.Value.Determinism.AdvanceStep() });
    }

    public static Result<RunState> Choose(RunState run, ChooseDialogueOptionCommand command, IRunActivityEffectExecutor? effects)
    {
        var legal = CheckActivity(run);
        if (legal.IsFailure) return Result<RunState>.Failure(legal.Error);
        var dialogue = Current(run);
        if (dialogue == null || dialogue.Completed || dialogue.DialogueInstanceId != command.DialogueInstanceId || dialogue.CurrentNodeId != command.NodeId)
            return Result<RunState>.Failure("Dialogue instance or node is not current");
        var node = dialogue.Definition.Nodes.Single(node => node.NodeId == dialogue.CurrentNodeId);
        var choice = node.Choices.FirstOrDefault(choice => choice.ChoiceId == command.ChoiceId);
        if (choice == null || !Available(run, dialogue, choice))
            return Result<RunState>.Failure("Dialogue choice is unavailable");
        var spent = RunResourceTransitions.Spend(run.ResourceState, choice.Costs, $"dialogue:{dialogue.DialogueInstanceId}:{node.NodeId}:{choice.ChoiceId}");
        if (spent.IsFailure) return Result<RunState>.Failure(spent.Error);
        var candidate = run with { ResourceState = spent.Value.State, NarrativeFlags = run.NarrativeFlags.SetItems(choice.SetFlags) };
        var applied = Execute(candidate, dialogue, $"choice:{choice.ChoiceId}", choice.Effects, effects, choice.EffectOwner);
        if (applied.IsFailure) return applied;
        candidate = applied.Value;
        var nextDialogue = dialogue with
        {
            CurrentNodeId = choice.NextNodeId ?? node.NodeId,
            Completed = choice.NextNodeId == null,
            History = dialogue.History.Add(new(node.NodeId, choice.ChoiceId))
        };
        if (!nextDialogue.Completed)
        {
            nextDialogue = nextDialogue with { History = nextDialogue.History.Add(new(nextDialogue.CurrentNodeId, null)) };
            var nextNode = dialogue.Definition.Nodes.Single(node => node.NodeId == nextDialogue.CurrentNodeId);
            var entered = Execute(candidate, nextDialogue, "entry", nextNode.EntryEffects, effects, nextNode.EntryEffectOwner);
            if (entered.IsFailure) return entered;
            candidate = entered.Value;
        }
        return Result<RunState>.Success(candidate with
        {
            Dialogues = candidate.Dialogues.SetItem(candidate.Dialogues.IndexOf(dialogue), nextDialogue),
            Determinism = candidate.Determinism.AdvanceStep()
        });
    }

    private static Result<RunMapNodeState> CheckActivity(RunState run)
    {
        var node = run.Map.Nodes.FirstOrDefault(node => node.NodeId == run.CurrentNodeId);
        return run.Lifecycle != RunLifecycleState.Active || run.ActiveEncounterId != null || node?.Activity.Type != RunActivityType.Dialogue ||
            run.Map.ResolvedNodeIds.Contains(node.NodeId)
            ? Result<RunMapNodeState>.Failure("The current activity is not an open dialogue")
            : Result<RunMapNodeState>.Success(node);
    }

    private static Result<RunState> Execute(RunState run, DialogueState dialogue, string boundary,
        IReadOnlyList<EffectDefinition> definitions, IRunActivityEffectExecutor? effects, RunActivityEffectOwner owner)
    {
        if (definitions.Count == 0) return Result<RunState>.Success(run);
        if (effects == null) return Result<RunState>.Failure("Run activity effect executor is unavailable");
        var result = effects.Execute(run, new RunMapNodeState
        {
            NodeId = $"{dialogue.ActivityNodeId}:dialogue:{dialogue.DialogueInstanceId}:{dialogue.CurrentNodeId}:{boundary}",
            EntryEffects = definitions,
            EntryEffectOwner = owner
        }, RunActivityBoundary.Entry);
        return result.IsFailure ? Result<RunState>.Failure(result.Error) : Result<RunState>.Success(result.Value.State);
    }
}
