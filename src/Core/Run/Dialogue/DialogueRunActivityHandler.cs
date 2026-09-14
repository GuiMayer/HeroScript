using Core.Common;

namespace Core.Run.Dialogue;

internal sealed class DialogueRunActivityHandler : RunActivityHandlerBase
{
    public override RunActivityType Type => RunActivityType.Dialogue;
    public override string DefinitionKind => "dialogues";
    public override IReadOnlySet<string> CommandTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RunCommandTypes.StartDialogue, RunCommandTypes.ChooseDialogueOption, RunCommandTypes.ResolveNode
    };
    public override bool IsComplete(RunState run, RunMapNodeState node) =>
        run.Dialogues.Any(dialogue => dialogue.ActivityNodeId == node.NodeId && dialogue.Completed);

    public override Result ValidateCommand(RunState run, RunMapNodeState node, string commandType, object payload) => payload switch
    {
        StartDialogueCommand start when start.DialogueId != node.Activity.DefinitionId => Result.Failure("Dialogue definition does not match this activity"),
        ChooseDialogueOptionCommand choice when DialogueTransitions.Current(run)?.DialogueInstanceId != choice.DialogueInstanceId => Result.Failure("Dialogue instance does not belong to this activity"),
        _ => base.ValidateCommand(run, node, commandType, payload)
    };

    public override IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node)
    {
        var dialogue = DialogueTransitions.Current(run);
        if (dialogue?.Completed == true) return [Resolve(run, node)];
        var commands = new List<RunAvailableCommand>();
        if (dialogue == null)
            commands.Add(Command(run, node, RunCommandTypes.StartDialogue, new { dialogueId = "string" }, new { dialogueId = node.Activity.DefinitionId }));
        else
        {
            var view = DialogueTransitions.View(run, dialogue);
            var ids = view.Choices.Where(choice => choice.Available).Select(choice => choice.ChoiceId).ToArray();
            if (ids.Length > 0)
                commands.Add(Command(run, node, RunCommandTypes.ChooseDialogueOption,
                    new { dialogueInstanceId = "guid", nodeId = "string", choiceId = "string" },
                    new { dialogueInstanceId = dialogue.DialogueInstanceId, nodeId = dialogue.CurrentNodeId, choiceIds = ids }));
        }
        if (node.CompletionPolicy == RunActivityCompletionPolicy.Optional) commands.Add(Resolve(run, node));
        return commands;
    }
}
