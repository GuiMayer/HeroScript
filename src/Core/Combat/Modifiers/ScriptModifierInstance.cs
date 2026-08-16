namespace Core.Combat.Modifiers;

/// <summary>
/// Active modifier instance attached to a run, entity, or other source.
/// </summary>
public record ScriptModifierInstance
{
    public Guid InstanceId { get; init; }
    public string ModifierId { get; init; } = string.Empty;
    public ScriptModifierDefinition Definition { get; init; } = new();
    public string OwnerId { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public int Stacks { get; init; } = 1;
    public int Duration { get; init; } = -1;
    public bool IsActive { get; init; } = true;
}
