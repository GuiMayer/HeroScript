using Core.Common;
using Core.Content;
using Core.Resources;
using System.Collections.Immutable;

namespace Core.Run.Content;

/// <summary>
/// Validates state-pinned card instances against a candidate content runtime.
/// Development hot reload is rejected atomically when a new base container no
/// longer accepts an upgrade patch already owned by the run.
/// </summary>
public static class RunContentCompatibilityValidator
{
    public static Result ValidateForActivation(RunState run, ContentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(runtime);
        if (run.PlayerEntity != null)
        {
            if (run.PlayerEntity.InstanceId != run.PlayerEntityId) return Result.Failure("Persistent player identity mismatch");
            var player = PersistentPlayerTransitions.Rebind(run.PlayerEntity, runtime);
            if (player.IsFailure) return Result.Failure(player.Error);
        }
        foreach (var actor in run.Encounters.SelectMany(encounter => encounter.Combat.GetAllActors()))
        {
            var attributes = RebindAttributes(actor, runtime);
            if (attributes.IsFailure) return Result.Failure(attributes.Error);
        }
        var compiler = new CardContentCompiler();
        var effectiveCards = new EffectiveCardResolver();
        foreach (var instance in run.Deck.Topology.Instances.Values
                     .OrderBy(card => card.CardInstanceId))
        {
            var compiled = compiler.Compile(instance.DefinitionId, runtime);
            if (compiled.IsFailure)
            {
                return Result.Failure(
                    $"Card instance {instance.CardInstanceId} cannot activate target content: {compiled.Error}");
            }
            var effective = effectiveCards.Resolve(compiled.Value, instance);
            if (effective.IsFailure)
            {
                return Result.Failure(
                    $"Card instance {instance.CardInstanceId} cannot activate target content: {effective.Error}");
            }
            var references = GameplayContentValidator.ValidateCardContainer(runtime,
                $"card-instances/{instance.CardInstanceId}", effective.Value.Components);
            if (references.IsFailure) return references;
        }
        return Result.Success();
    }

    /// <summary>
    /// Produces the content-compatible state that can be committed by the
    /// activation command. Nothing is mutated when any pinned card or resource
    /// is incompatible with the target revision.
    /// </summary>
    public static Result<RunState> PrepareForActivation(RunState run, ContentRuntime runtime)
    {
        var compatibility = ValidateForActivation(run, runtime);
        if (compatibility.IsFailure)
            return Result<RunState>.Failure(compatibility.Error);

        var runResources = Rebind(run.ResourceState, runtime);
        if (runResources.IsFailure)
            return Result<RunState>.Failure(runResources.Error);

        var encounters = new List<RunEncounterState>(run.Encounters.Length);
        foreach (var encounter in run.Encounters)
        {
            var actors = new Dictionary<string, Core.Combat.Models.CombatActorState>(StringComparer.Ordinal);
            foreach (var actor in encounter.Combat.GetAllActors())
            {
                var resources = Rebind(actor.ResourceState, runtime);
                if (resources.IsFailure)
                    return Result<RunState>.Failure(resources.Error);
                var attributes = RebindAttributes(actor, runtime);
                if (attributes.IsFailure) return Result<RunState>.Failure(attributes.Error);
                actors[actor.InstanceId] = (actor with
                {
                    ContentRevision = runtime.Manifest.Revision,
                    Components = actor.Components.ToImmutableDictionary(StringComparer.Ordinal).SetItems(attributes.Value)
                }).WithResourceState(resources.Value);
            }

            encounters.Add(encounter with
            {
                Combat = encounter.Combat with { Actors = actors }
            });
        }

        return Result<RunState>.Success(run with
        {
            PlayerEntity = run.PlayerEntity == null ? null : PersistentPlayerTransitions.Rebind(run.PlayerEntity, runtime).Value,
            ResourceState = runResources.Value,
            Encounters = encounters.ToImmutableArray()
        });
    }

    public static bool RequiresRuntime(RunState run) =>
        run.PlayerEntity != null ||
        run.Deck.Topology.Instances.Count > 0 ||
        run.ResourceState.Resources.Count > 0 ||
        run.Encounters.Any(encounter => encounter.Combat.GetAllActors()
                .Any(entity => entity.ResourceState.Resources.Count > 0));

    private static Result<IReadOnlyDictionary<string, Core.Combat.Models.EntityComponentState>> RebindAttributes(
        Core.Combat.Models.CombatActorState actor, ContentRuntime runtime)
    {
        var stats = actor.Components.Where(pair => pair.Value is Core.Combat.Models.StatEntityComponentState)
            .ToImmutableDictionary(StringComparer.Ordinal);
        if (stats.Count == 0)
            return Result<IReadOnlyDictionary<string, Core.Combat.Models.EntityComponentState>>.Success(stats);
        var rebound = PersistentPlayerTransitions.RebindAttributes(actor with { Components = stats }, runtime);
        return rebound.IsFailure
            ? Result<IReadOnlyDictionary<string, Core.Combat.Models.EntityComponentState>>.Failure(rebound.Error)
            : Result<IReadOnlyDictionary<string, Core.Combat.Models.EntityComponentState>>.Success(rebound.Value.Components);
    }

    private static Result<ResourceSet> Rebind(ResourceSet resources, ContentRuntime runtime)
    {
        if (resources.Resources.Count == 0)
            return Result<ResourceSet>.Success(resources);

        var definitions = new Dictionary<string, ResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceId in resources.Resources.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            var definition = runtime.GetDefinition<ResourceDefinition>("resources", resourceId);
            if (definition.IsFailure)
            {
                return Result<ResourceSet>.Failure(
                    $"Owner '{resources.OwnerId}' cannot activate target content: {definition.Error}");
            }
            definitions[resourceId] = definition.Value;
        }

        return resources.RebindDefinitions(definitions);
    }
}
