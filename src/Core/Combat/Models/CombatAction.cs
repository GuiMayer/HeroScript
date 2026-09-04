using System.Collections.Immutable;
using Core.Effects;

namespace Core.Combat.Models;

/// <summary>
/// Representa uma ação executada em combate.
/// Imutável.
/// </summary>
public record CombatAction
{
    private ImmutableArray<string> _targetIds = [];
    private ImmutableArray<EffectApplicationRecord> _applications = [];

    public Guid ActionId { get; init; } = Guid.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UnixEpoch;
    public int Turn { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public ActionType ActionType { get; init; }
    public string? PowerId { get; init; }  // Null para BASIC_ATTACK, PASS, END_TURN
    public string? TargetId { get; init; }  // Null para PASS, END_TURN
    public Guid? CardInstanceId { get; init; }
    public string? CardDefinitionId { get; init; }
    public IReadOnlyList<string> TargetIds
    {
        get => _targetIds;
        init => _targetIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectApplicationRecord> Applications
    {
        get => _applications;
        init => _applications = value?.ToImmutableArray() ?? [];
    }
}
