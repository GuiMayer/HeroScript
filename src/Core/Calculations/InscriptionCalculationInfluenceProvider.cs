using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Math;
using Core.Run.Loadouts;

namespace Core.Calculations;

/// <summary>Equipped character attributes contribute through authored channel/bucket contracts, never through card rewrites.</summary>
public sealed class InscriptionCalculationInfluenceProvider(IContentRuntimeResolver runtimes, IRuntimeFormulaEvaluator formulas) : ICalculationInfluenceProvider
{
    public string ProviderId => "character-inscriptions";

    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        Result<IReadOnlyList<CalculationInfluence>> Fail(string error) => Result<IReadOnlyList<CalculationInfluence>>.Failure(error);
        if (context.Run?.Loadouts is not { } loadouts) return Result<IReadOnlyList<CalculationInfluence>>.Success([]);
        var run = context.Run;
        var runtime = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure) return Fail(runtime.Error);
        var policy = LoadoutContentAccess.Policy(run, runtime.Value);
        if (policy.IsFailure) return Fail(policy.Error);
        var valid = LoadoutContentAccess.ValidateState(run, policy.Value);
        if (valid.IsFailure) return Fail(valid.Error);
        var result = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var actorId in new[] { context.Actor?.InstanceId, context.Target?.InstanceId }.Where(id => id != null).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!loadouts.Actors.TryGetValue(actorId!, out var equipped)) continue;
            var inventory = loadouts.Inventories[loadouts.InventoryId(run.RunId, actorId!)];
            foreach (var instanceId in equipped.Assignments.Values.Order())
            {
                var item = inventory.Items.First(value => value.InstanceId == instanceId);
                var acquired = runtimes.Resolve(item.AcquisitionRevision, run.ConfigName);
                if (acquired.IsFailure) return Fail(acquired.Error);
                if (acquired.Value.Manifest.Revision != item.AcquisitionRevision || !string.Equals(acquired.Value.Manifest.ConfigName, run.ConfigName, StringComparison.OrdinalIgnoreCase))
                    return Fail("Inscription influence setting or revision mismatch");
                var definition = acquired.Value.GetDefinition<InscriptionDefinition>("inscriptions", item.DefinitionId);
                if (definition.IsFailure) return Fail(definition.Error);
                foreach (var bonus in definition.Value.AttributeBonuses)
                {
                    var influence = bonus.Influence;
                    var scoped = influence.Scope == CalculationEntityScope.Actor ? context.Actor : context.Target;
                    if (scoped == null || scoped.InstanceId != actorId || !context.SelectsInfluence(influence.Channel, influence.Bucket)) continue;
                    var collected = ContextualInfluencePolicies.Resolve([influence], CalculationSourceKind.Inscription,
                        instanceId.ToString("D"), context with { ContentRevision = item.AcquisitionRevision,
                            Variables = CalculationOwnerResolver.BindResources(context, scoped) }, formulas);
                    if (collected.IsFailure) return Fail(collected.Error);
                    result.AddRange(collected.Value);
                }
            }
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }
}

internal static class CalculationOwnerResolver
{
    internal static Dictionary<string, float> BindResources(CalculationSourceContext context, Core.Combat.Models.CombatActorState owner)
    {
        var variables = context.Variables.Where(pair => !pair.Key.StartsWith("owner.resources.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        SetResources(variables, owner);
        return variables;
    }

    internal static void SetResources(IDictionary<string, float> variables, Core.Combat.Models.CombatActorState owner)
    {
        foreach (var key in variables.Keys.Where(key => key.StartsWith("owner.resources.", StringComparison.Ordinal)).ToArray()) variables.Remove(key);
        Core.Resources.ResourceFormulaVariables.AddOwner(variables, "owner", owner.ResourceState);
    }

    internal static Core.Combat.Models.CombatActorState? FormulaOwner(Core.Combat.Models.GameplayOwner owner, CalculationSourceContext context)
    {
        var candidates = context.Combat?.GetAllActors() ?? new[] { context.Actor, context.Target }.Where(actor => actor != null).Select(actor => actor!);
        return candidates.OrderBy(actor => actor.InstanceId, StringComparer.Ordinal).FirstOrDefault(actor => owner.Includes(actor, context.Combat, context.Run));
    }
}
