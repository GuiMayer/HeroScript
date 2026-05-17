using Core.Combat.Modifiers;

namespace API.Models.Modifiers;

public class ModifierDefinitionResponse
{
    public string ModifierId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ModifierKey { get; set; } = string.Empty;
    public string? FormulaValue { get; set; }
    public float BaseValue { get; set; }
    public int DefaultStacks { get; set; }
    public int MaxStacks { get; set; }
    public int DefaultDuration { get; set; }
    public List<string> RequiredTags { get; set; } = new();
    public List<string> ExcludedTags { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    public static ModifierDefinitionResponse FromDefinition(ScriptModifierDefinition definition)
    {
        return new ModifierDefinitionResponse
        {
            ModifierId = definition.ModifierId,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            ModifierKey = definition.ModifierKey,
            FormulaValue = definition.FormulaValue,
            BaseValue = definition.BaseValue,
            DefaultStacks = definition.DefaultStacks,
            MaxStacks = definition.MaxStacks,
            DefaultDuration = definition.DefaultDuration,
            RequiredTags = definition.RequiredTags,
            ExcludedTags = definition.ExcludedTags,
            Tags = definition.Tags
        };
    }
}

public class ModifierInstanceResponse
{
    public Guid InstanceId { get; set; }
    public string ModifierId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public int Stacks { get; set; }
    public int Duration { get; set; }
    public bool IsActive { get; set; }
    public ModifierDefinitionResponse Definition { get; set; } = new();

    public static ModifierInstanceResponse FromInstance(ScriptModifierInstance instance)
    {
        return new ModifierInstanceResponse
        {
            InstanceId = instance.InstanceId,
            ModifierId = instance.ModifierId,
            OwnerId = instance.OwnerId,
            SourceId = instance.SourceId,
            Stacks = instance.Stacks,
            Duration = instance.Duration,
            IsActive = instance.IsActive,
            Definition = ModifierDefinitionResponse.FromDefinition(instance.Definition)
        };
    }
}
