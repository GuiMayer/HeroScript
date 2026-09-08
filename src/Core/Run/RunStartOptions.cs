namespace Core.Run;

/// <summary>
/// Reproducible inputs required to start a run. When Seed is omitted, the
/// application boundary creates one and the chosen value is stored in RunState.
/// </summary>
public sealed record RunStartOptions(
    string ConfigName = "default",
    string RunDefinitionId = "default_run",
    string PlayerEntityId = "player",
    ulong? Seed = null,
    string? ContentRevision = null,
    string? ModeId = null,
    string? ChallengeId = null,
    IReadOnlyList<RunStartingCard>? StartingDeck = null,
    int? StartingHandSize = null,
    string? ScenarioHash = null,
    string? AttemptKey = null,
    Sandbox.CombatScenarioDefinition? Scenario = null,
    string? SettingId = null);

/// <summary>
/// An immutable card declaration consumed only at run creation. Scenario
/// compilation expands upgrade definitions before this reaches the aggregate.
/// </summary>
public sealed record RunStartingCard
{
    private readonly IReadOnlyList<CardUpgradeState> _upgrades = [];

    public string DefinitionId { get; init; } = string.Empty;
    public IReadOnlyList<CardUpgradeState> Upgrades
    {
        get => _upgrades;
        init => _upgrades = value?.ToArray() ?? [];
    }
}
