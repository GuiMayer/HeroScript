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
    string? ChallengeId = null);
