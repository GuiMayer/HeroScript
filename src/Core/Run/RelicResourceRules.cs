using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Common;
using Core.Resources;

namespace Core.Run;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceRestrictedBalancePolicy { DiscardExcess }

/// <summary>Source-independent resource ceiling; never infers shield/health semantics from an ID.</summary>
public sealed record ResourceRestrictionDefinition
{
    public string RuleId { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public float CurrentCeiling { get; init; }
    public ResourceRestrictedBalancePolicy? ExistingBalance { get; init; }
}

/// <summary>Materializes the intersection of owner rules. It does not calculate damage or armor.</summary>
public static class RelicResourceRules
{
    public static Result Validate(IEnumerable<ResourceRestrictionDefinition> definitions)
    {
        if (definitions is ImmutableArray<ResourceRestrictionDefinition> array && array.IsDefault)
            return Result.Failure("Resource restrictions cannot be an uninitialized array");
        var items = definitions.ToArray();
        if (items.Length > 16 || items.Any(item => item == null) ||
            items.Select(item => item.RuleId).Distinct(StringComparer.Ordinal).Count() != items.Length)
            return Result.Failure("Resource restrictions require unique IDs and at most 16 entries");
        return items.Any(item => string.IsNullOrWhiteSpace(item.RuleId) || string.IsNullOrWhiteSpace(item.ResourceId) ||
            !float.IsFinite(item.CurrentCeiling) || item.ExistingBalance != ResourceRestrictedBalancePolicy.DiscardExcess)
            ? Result.Failure("Resource restriction requires a finite ceiling and explicit DiscardExcess balance policy")
            : Result.Success();
    }

    public static Result<CombatState> Materialize(RunState run, CombatState combat)
    {
        var current = combat;
        foreach (var actor in combat.GetAllActors())
        {
            var resolved = Actor(run, actor, combat);
            if (resolved.IsFailure) return Result<CombatState>.Failure(resolved.Error);
            if (!ReferenceEquals(resolved.Value, actor)) current = current.ReplaceActor(resolved.Value);
        }
        return Result<CombatState>.Success(current);
    }

    public static Result<RunState> Materialize(RunState run)
    {
        var result = run;
        if (run.PlayerEntity is { } player)
        {
            var actor = Actor(run, new CombatActorState { InstanceId = player.InstanceId, Components = player.Components }, null);
            if (actor.IsFailure) return Result<RunState>.Failure(actor.Error);
            if (!ReferenceEquals(actor.Value.Components, player.Components))
                result = result with { PlayerEntity = player with { Components = actor.Value.Components } };
        }
        var encounters = run.Encounters;
        for (var index = 0; index < encounters.Length; index++)
        {
            if (encounters[index].Resolved) continue;
            var combat = Materialize(result, encounters[index].Combat);
            if (combat.IsFailure) return Result<RunState>.Failure(combat.Error);
            if (!ReferenceEquals(combat.Value, encounters[index].Combat))
                encounters = encounters.SetItem(index, encounters[index] with { Combat = combat.Value });
        }
        return Result<RunState>.Success(encounters == run.Encounters ? result : result with { Encounters = encounters });
    }

    private static Result<CombatActorState> Actor(RunState run, CombatActorState actor, CombatState? combat)
    {
        var ceilings = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var relic in run.Relics)
        {
            if (!relic.Owner.Includes(actor, combat, run)) continue;
            var valid = Validate(relic.ResourceRestrictions ?? []);
            if (valid.IsFailure) return Result<CombatActorState>.Failure(valid.Error);
            foreach (var rule in relic.ResourceRestrictions ?? [])
                ceilings[rule.ResourceId] = ceilings.TryGetValue(rule.ResourceId, out var previous)
                    ? MathF.Min(previous, rule.CurrentCeiling) : rule.CurrentCeiling;
        }
        var components = actor.Components;
        foreach (var (id, component) in actor.Components)
        {
            if (component is not ResourceEntityComponentState resources || resources.State.OwnerId != actor.InstanceId) continue;
            var pools = resources.State.Resources.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
            var changed = false;
            foreach (var (resourceId, pool) in resources.State.Resources)
            {
                float? ceiling = ceilings.TryGetValue(resourceId, out var upper) ? upper : null;
                if (ceiling == pool.CurrentCeiling && !(ceiling is { } bound && pool.Current > bound)) continue;
                try { pools = pools.SetItem(resourceId, (pool with { CurrentCeiling = ceiling }).Set(pool.Current)); }
                catch (InvalidOperationException error) { return Result<CombatActorState>.Failure(error.Message); }
                changed = true;
            }
            if (changed) components = components.ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem(id, resources with { State = resources.State with { Resources = pools } });
        }
        return Result<CombatActorState>.Success(ReferenceEquals(components, actor.Components) ? actor : actor with { Components = components });
    }
}
