using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Entity;
using Core.Entity.Definitions;

namespace Core.Run;

/// <summary>Run base -> encounter initial attributes. Encounter mutations never overwrite the run base.</summary>
public static class PersistentPlayerTransitions
{
    public static Result<EntityState> Create(string instanceId, string definitionId, ContentRuntime runtime)
    {
        var definition = runtime.GetDefinition<EntityDefinition>("entities", definitionId);
        if (definition.IsFailure) return Result<EntityState>.Failure(definition.Error);
        var valid = EntityDefinitionValidator.Validate(definition.Value);
        if (valid.IsFailure) return Result<EntityState>.Failure(valid.Error);
        return Result<EntityState>.Success(new() { InstanceId = instanceId, DefinitionId = definitionId,
            ContentRevision = runtime.Manifest.Revision, Name = definition.Value.DisplayName,
            Components = definition.Value.Components.OfType<StatEntityComponentDefinition>().ToImmutableDictionary(component => component.ComponentId,
                component => (EntityComponentState)new StatEntityComponentState { ComponentId = component.ComponentId,
                    Values = component.Values, ValueRules = component.ValueRules }, StringComparer.Ordinal) });
    }

    public static Result<CombatState> Materialize(RunState run, CombatState combat)
    {
        if (run.PlayerEntity == null) return Result<CombatState>.Success(combat);
        var player = run.PlayerEntity;
        var actor = combat.GetActor(run.PlayerEntityId);
        if (player.InstanceId != run.PlayerEntityId || actor == null || actor.DefinitionId != player.DefinitionId ||
            actor.ContentRevision != player.ContentRevision || player.ContentRevision != run.Determinism.ContentRevision)
            return Result<CombatState>.Failure("Persistent player identity/revision does not match encounter");
        var components = actor.Components.Where(pair => pair.Value is not StatEntityComponentState).ToImmutableDictionary(StringComparer.Ordinal);
        return Result<CombatState>.Success(combat.ReplaceActor(actor with { Components = components.SetItems(player.Components) }));
    }

    public static Result<EntityState> Rebind(EntityState player, ContentRuntime runtime)
    {
        var declared = Create(player.InstanceId, player.DefinitionId, runtime);
        if (declared.IsFailure) return declared;
        if (!player.Components.Keys.Order(StringComparer.Ordinal).SequenceEqual(declared.Value.Components.Keys.Order(StringComparer.Ordinal)))
            return Result<EntityState>.Failure("Persistent attribute component schema changed");
        var components = ImmutableDictionary.CreateBuilder<string, EntityComponentState>(StringComparer.Ordinal);
        foreach (var (id, stored) in player.Components)
        {
            if (stored is not StatEntityComponentState stats || declared.Value.Components[id] is not StatEntityComponentState schema ||
                !stats.Values.Keys.Order(StringComparer.Ordinal).SequenceEqual(schema.Values.Keys.Order(StringComparer.Ordinal)))
                return Result<EntityState>.Failure("Persistent attribute value schema changed");
            var valid = EntityAttributeTransitions.Validate(stats.Values, schema.ValueRules);
            if (valid.IsFailure) return Result<EntityState>.Failure(valid.Error);
            components.Add(id, stats with { ValueRules = schema.ValueRules });
        }
        return Result<EntityState>.Success(player with { ContentRevision = runtime.Manifest.Revision, Components = components.ToImmutable() });
    }
}
