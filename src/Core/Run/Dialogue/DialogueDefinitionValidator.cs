namespace Core.Run.Dialogue;

public static class DialogueDefinitionValidator
{
    public static IReadOnlyList<string> Validate(DialogueDefinition definition)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(definition.DialogueId)) errors.Add("dialogueId is required");
        Text(definition.Title, "title", errors);
        var ids = definition.Nodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
        if (!ids.Contains(definition.StartNodeId)) errors.Add("startNodeId does not exist");
        if (ids.Count != definition.Nodes.Length || ids.Contains(string.Empty)) errors.Add("nodeIds must be nonempty and unique");
        foreach (var node in definition.Nodes)
        {
            Text(node.Text, $"{node.NodeId}/text", errors);
            if (node.Choices.IsEmpty) errors.Add($"{node.NodeId} requires a choice (use a terminal Continue to close)");
            if (node.Choices.Select(choice => choice.ChoiceId).Distinct(StringComparer.Ordinal).Count() != node.Choices.Length)
                errors.Add($"{node.NodeId} contains duplicate choiceIds");
            foreach (var choice in node.Choices)
            {
                var path = $"{node.NodeId}/{choice.ChoiceId}";
                if (string.IsNullOrWhiteSpace(choice.ChoiceId)) errors.Add($"{path}: choiceId is required");
                Text(choice.Text, path, errors);
                if (choice.NextNodeId != null && !ids.Contains(choice.NextNodeId)) errors.Add($"{path}: nextNodeId does not exist");
                if (choice.Costs.Any(cost => string.IsNullOrWhiteSpace(cost.ResourceId) || !float.IsFinite(cost.Amount) || cost.Amount < 0) ||
                    choice.Costs.Select(cost => cost.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != choice.Costs.Length)
                    errors.Add($"{path}: costs must be finite, nonnegative and unique per resource");
                if (choice.SetFlags.Any(flag => string.IsNullOrWhiteSpace(flag.Key) || flag.Value == null)) errors.Add($"{path}: flags require a name and string value");
                if (choice.Condition != null) Condition(choice.Condition, path, errors, 0);
            }
        }
        // Every authored node must be reachable and have a structural route to an ending.
        var reachable = new HashSet<string>(StringComparer.Ordinal) { definition.StartNodeId };
        var ending = definition.Nodes.Where(node => node.Choices.Any(choice => choice.NextNodeId == null)).Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var node in definition.Nodes)
            {
                if (reachable.Contains(node.NodeId))
                    foreach (var choice in node.Choices.Where(choice => choice.NextNodeId != null)) changed |= reachable.Add(choice.NextNodeId!);
                if (node.Choices.Any(choice => choice.NextNodeId != null && ending.Contains(choice.NextNodeId))) changed |= ending.Add(node.NodeId);
            }
        } while (changed);
        foreach (var node in definition.Nodes)
        {
            if (!reachable.Contains(node.NodeId)) errors.Add($"{node.NodeId}: unreachable dialogue node");
            if (!ending.Contains(node.NodeId)) errors.Add($"{node.NodeId}: no path to a dialogue ending");
        }
        return errors;
    }

    private static void Text(DialogueText text, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text.Text)) errors.Add($"{path}: English fallback text is required");
    }

    private static void Condition(DialogueCondition condition, string path, List<string> errors, int depth)
    {
        if (depth > 24) { errors.Add($"{path}: condition nesting exceeds 24"); return; }
        if (!Enum.IsDefined(condition.Kind)) errors.Add($"{path}: unknown condition kind");
        if (condition.Kind is DialogueConditionKind.All or DialogueConditionKind.Any or DialogueConditionKind.Not)
        {
            if (condition.Children.IsEmpty || (condition.Kind == DialogueConditionKind.Not && condition.Children.Length != 1))
                errors.Add($"{path}: invalid condition children");
            foreach (var child in condition.Children) Condition(child, path, errors, depth + 1);
        }
        else if (string.IsNullOrWhiteSpace(condition.Id) || !condition.Children.IsEmpty)
            errors.Add($"{path}: leaf conditions require an id and no children");
        if (!float.IsFinite(condition.Amount) || condition.Amount < 0) errors.Add($"{path}: invalid condition amount");
    }
}
