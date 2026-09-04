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
        var compiler = new CardContentCompiler();
        var effectiveCards = new EffectiveCardResolver();
        foreach (var instance in run.Deck.CardInstances.Values
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
            var heroResources = Rebind(encounter.Combat.Hero.ResourceState, runtime);
            if (heroResources.IsFailure)
                return Result<RunState>.Failure(heroResources.Error);

            var enemies = new List<Core.Combat.Models.CombatEntity>(encounter.Combat.Enemies.Count);
            foreach (var enemy in encounter.Combat.Enemies)
            {
                var enemyResources = Rebind(enemy.ResourceState, runtime);
                if (enemyResources.IsFailure)
                    return Result<RunState>.Failure(enemyResources.Error);
                enemies.Add(enemy with { ResourceState = enemyResources.Value });
            }

            encounters.Add(encounter with
            {
                Combat = encounter.Combat with
                {
                    Hero = encounter.Combat.Hero with { ResourceState = heroResources.Value },
                    Enemies = enemies
                }
            });
        }

        return Result<RunState>.Success(run with
        {
            ResourceState = runResources.Value,
            Encounters = encounters.ToImmutableArray()
        });
    }

    public static bool RequiresRuntime(RunState run) =>
        run.Deck.CardInstances.Count > 0 ||
        run.ResourceState.Resources.Count > 0 ||
        run.Encounters.Any(encounter => encounter.Combat.GetAllEntities()
            .Any(entity => entity.ResourceState.Resources.Count > 0));

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
