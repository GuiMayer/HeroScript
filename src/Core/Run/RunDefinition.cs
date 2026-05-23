namespace Core.Run;

public sealed record RunDefinition
{
    public string RunId { get; init; } = "default_run";
    public int StartingGold { get; init; }
    public int StartingPowerPoints { get; init; }
    public int StartingHandSize { get; init; } = 5;
    public string CombatActivationRulesId { get; init; } = "default_activation";
    public List<string> StartingDeck { get; init; } = new();
    public List<RunMapNodeDefinition> MapNodes { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed record RunMapNodeDefinition
{
    public string NodeId { get; init; } = string.Empty;
    public string NodeType { get; init; } = "combat";
    public List<string> NextNodeIds { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}
