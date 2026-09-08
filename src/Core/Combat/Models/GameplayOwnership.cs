using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Run;
using Core.Common;

namespace Core.Combat.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameplayOwnerKind { Entity, Side, Run, Global }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ControllerKind { Player, AI, None }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SideRelationship { Ally, Enemy, Neutral }

public sealed record ControllerBinding
{
    public ControllerKind Kind { get; init; } = ControllerKind.None;
    public string? PolicyId { get; init; }
}

/// <summary>Serializable authored binding; materialization creates runtime state from it.</summary>
public sealed record ControllerBindingDefinition
{
    public ControllerKind Kind { get; init; } = ControllerKind.None;
    public string? PolicyId { get; init; }

    public ControllerBinding Materialize() => new() { Kind = Kind, PolicyId = PolicyId };
}

public static class ControllerBindingDefinitionValidator
{
    public static Result Validate(ControllerBindingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return ControllerBindingValidator.Validate(definition.Materialize());
    }
}

public static class ControllerBindingValidator
{
    public static Result Validate(ControllerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!Enum.IsDefined(binding.Kind))
            return Result.Failure("Controller kind is invalid");
        if (binding.Kind == ControllerKind.AI && string.IsNullOrWhiteSpace(binding.PolicyId))
            return Result.Failure("AI controller requires policyId");
        if (binding.Kind != ControllerKind.AI && !string.IsNullOrWhiteSpace(binding.PolicyId))
            return Result.Failure("Only an AI controller can declare policyId");
        return Result.Success();
    }
}

/// <summary>Serializable identity; it never points at a mutable runtime object.</summary>
public sealed record GameplayOwner
{
    public GameplayOwnerKind Kind { get; init; } = GameplayOwnerKind.Entity;
    public string Id { get; init; } = string.Empty;

    public bool Includes(CombatActorState entity, CombatState combat, RunState? run) => Kind switch
    {
        GameplayOwnerKind.Entity => StringComparer.Ordinal.Equals(Id, entity.InstanceId),
        GameplayOwnerKind.Side => StringComparer.Ordinal.Equals(Id, combat.GetSideId(entity)),
        // A run-scoped item belongs to the run's player, not every combat participant.
        GameplayOwnerKind.Run => run != null && StringComparer.Ordinal.Equals(Id, run.RunId.ToString()) &&
            StringComparer.Ordinal.Equals(entity.InstanceId, run.PlayerEntityId),
        GameplayOwnerKind.Global => true,
        _ => false
    };
}

public sealed record CombatSide
{
    public string SideId { get; init; } = string.Empty;
}

public sealed record SideRelationshipRule
{
    public string FromSideId { get; init; } = string.Empty;
    public string ToSideId { get; init; } = string.Empty;
    public SideRelationship Relationship { get; init; }
}

/// <summary>Relationships are directed, allowing asymmetric and multi-sided encounters.</summary>
public sealed record CombatRelationshipPolicy
{
    private ImmutableArray<SideRelationshipRule> _rules = [];
    public SideRelationship SameSide { get; init; } = SideRelationship.Ally;
    public SideRelationship DifferentSides { get; init; } = SideRelationship.Enemy;
    public IReadOnlyList<SideRelationshipRule> Rules
    {
        get => _rules;
        init => _rules = value?.ToImmutableArray() ?? [];
    }

    public SideRelationship Resolve(string from, string to) =>
        _rules.FirstOrDefault(rule => rule.FromSideId == from && rule.ToSideId == to)?.Relationship
        ?? (StringComparer.Ordinal.Equals(from, to) ? SameSide : DifferentSides);
}

public static class GameplayRelationshipValidator
{
    public static Result Validate(IEnumerable<string> participantSideIds, IReadOnlyList<CombatSide> sides,
        CombatRelationshipPolicy relationships)
    {
        var participantSides = participantSideIds.ToArray();
        if (participantSides.Any(string.IsNullOrWhiteSpace)) return Result.Failure("Every participant requires a sideId");
        if (sides.Count == 0) return Result.Failure("Every combat requires an explicit side catalog");
        if (sides.Any(side => side == null || string.IsNullOrWhiteSpace(side.SideId)))
            return Result.Failure("Invalid side identity");
        if (sides.Select(side => side.SideId).Distinct(StringComparer.Ordinal).Count() != sides.Count)
            return Result.Failure("Duplicate sideId");
        var known = sides.Select(side => side.SideId).ToHashSet(StringComparer.Ordinal);
        if (participantSides.Any(id => !known.Contains(id))) return Result.Failure("Participant references an undeclared side");
        if (relationships == null || !Enum.IsDefined(relationships.SameSide) || !Enum.IsDefined(relationships.DifferentSides))
            return Result.Failure("Invalid default side relationship");
        var pairs = new HashSet<(string From, string To)>();
        foreach (var rule in relationships.Rules)
        {
            if (rule == null || !known.Contains(rule.FromSideId) || !known.Contains(rule.ToSideId) || !Enum.IsDefined(rule.Relationship))
                return Result.Failure("Relationship references an undeclared side or an invalid policy");
            if (!pairs.Add((rule.FromSideId, rule.ToSideId))) return Result.Failure("Duplicate directed side relationship");
        }
        return Result.Success();
    }
}
