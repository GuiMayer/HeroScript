using System.Collections.Immutable;

namespace Core.Combat.Models;

/// <summary>
/// Command submitted by any controller type to execute an action for a combat actor.
/// </summary>
public sealed record CombatActionCommand
{
    private ImmutableDictionary<string, float> _runModifiers =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public string ActorId { get; init; } = string.Empty;
    public ActionType ActionType { get; init; }
    public string? PowerId { get; init; }
    public string? TargetId { get; init; }
    public string? CostOptionId { get; init; }
    public Guid? RunId { get; init; }
    public string? CardId { get; init; }
    public IReadOnlyDictionary<string, float> RunModifiers
    {
        get => _runModifiers;
        init => _runModifiers = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Controle otimista opcional. Quando informado, impede que um comando seja
    /// aplicado sobre um snapshot diferente daquele observado pelo chamador.
    /// </summary>
    public ulong? ExpectedStep { get; init; }
}
