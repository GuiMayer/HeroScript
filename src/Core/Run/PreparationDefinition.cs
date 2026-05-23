namespace Core.Run;

public sealed record PreparationDefinition
{
    public string PreparationId { get; init; } = string.Empty;
    public List<PreparationOptionDefinition> Options { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed record PreparationOptionDefinition
{
    public string OptionId { get; init; } = string.Empty;
    public int GoldCost { get; init; }
    public int PowerPointCost { get; init; }
    public List<string> AddCardsToDiscard { get; init; } = new();
    public List<PreparationModifierGrantDefinition> ApplyModifiers { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed record PreparationModifierGrantDefinition
{
    public string OwnerId { get; init; } = "run";
    public string ModifierId { get; init; } = string.Empty;
    public int Stacks { get; init; } = 1;
    public int? Duration { get; init; }
    public string? SourceId { get; init; }
}

public sealed record PreparationState
{
    public Guid PreparationInstanceId { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public string PreparationId { get; init; } = string.Empty;
    public List<PreparationOptionState> Options { get; init; } = new();
    public List<string> AppliedOptionIds { get; init; } = new();
}

public sealed record PreparationOptionState
{
    public string OptionId { get; init; } = string.Empty;
    public int GoldCost { get; init; }
    public int PowerPointCost { get; init; }
    public List<string> AddCardsToDiscard { get; init; } = new();
    public List<PreparationModifierGrantState> ApplyModifiers { get; init; } = new();
    public List<Guid> AppliedModifierInstanceIds { get; init; } = new();
    public bool Applied { get; set; }
}

public sealed record PreparationModifierGrantState
{
    public string OwnerId { get; init; } = "run";
    public string ModifierId { get; init; } = string.Empty;
    public int Stacks { get; init; } = 1;
    public int? Duration { get; init; }
    public string? SourceId { get; init; }
}
