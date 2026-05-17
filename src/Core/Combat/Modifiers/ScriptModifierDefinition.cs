namespace Core.Combat.Modifiers;

/// <summary>
/// Data-driven modifier definition loaded from JSON.
/// </summary>
public record ScriptModifierDefinition
{
    public string ModifierId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ModifierKey { get; init; } = string.Empty;
    public string? FormulaValue { get; init; }
    public float BaseValue { get; init; }
    public int DefaultStacks { get; init; } = 1;
    public int MaxStacks { get; init; } = 99;
    public int DefaultDuration { get; init; } = -1;
    public List<string> RequiredTags { get; init; } = new();
    public List<string> ExcludedTags { get; init; } = new();
    public List<string> Tags { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}
