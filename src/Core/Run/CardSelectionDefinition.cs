namespace Core.Run;

public sealed record CardSelectionDefinition
{
    public string SelectionId { get; init; } = string.Empty;
    public int PickCount { get; init; } = 1;
    public List<string> CardPool { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed record CardSelectionState
{
    public Guid SelectionInstanceId { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public string SelectionId { get; init; } = string.Empty;
    public int PickCount { get; init; }
    public List<string> Options { get; init; } = new();
    public bool Completed { get; set; }
    public List<string> PickedCardIds { get; init; } = new();
}
