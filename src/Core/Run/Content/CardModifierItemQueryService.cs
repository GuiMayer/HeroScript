using System.Collections.Immutable;
using Core.CardZones;
using Core.Common;
using Core.Content;

namespace Core.Run.Content;

public sealed record CardModifierItemCompatibility(Guid CardInstanceId, bool Compatible, string? Reason);
public sealed record CardModifierItemView(CardModifierItemInstance Instance, CardModifierItemDefinition Definition,
    ImmutableArray<CardModifierItemCompatibility> Cards);
public sealed record CardModifierItemSnapshot(Guid RunId, int RunSequence, string ContentRevision,
    ImmutableArray<CardModifierItemDefinition> Catalog, ImmutableArray<CardModifierItemView> Items);
public interface ICardModifierItemQueryService
{
    Result<CardModifierItemSnapshot> Read(Guid runId);
}

/// <summary>Ownership and compatibility facts. Reuses the ledger transition and effective composer; no rule evaluation in clients.</summary>
public sealed class CardModifierItemQueryService(IRunQueryService runs, IContentRuntimeResolver runtimes,
    ICardContentCompiler compiler, IEffectiveCardResolver composer) : ICardModifierItemQueryService
{
    public Result<CardModifierItemSnapshot> Read(Guid runId)
    {
        var loaded = runs.GetRun(runId);
        if (loaded.IsFailure) return Result<CardModifierItemSnapshot>.Failure(loaded.Error);
        var run = loaded.Value;
        var active = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (active.IsFailure) return Result<CardModifierItemSnapshot>.Failure(active.Error);
        var catalog = ImmutableArray.CreateBuilder<CardModifierItemDefinition>();
        foreach (var id in active.Value.GetDefinitions("card-modifier-items").Keys.Order(StringComparer.Ordinal))
        {
            var definition = active.Value.GetDefinition<CardModifierItemDefinition>("card-modifier-items", id);
            if (definition.IsFailure) return Result<CardModifierItemSnapshot>.Failure(definition.Error);
            catalog.Add(definition.Value);
        }
        var inventory = run.CardModifierItems ?? new();
        var valid = inventory.Validate();
        if (valid.IsFailure) return Result<CardModifierItemSnapshot>.Failure(valid.Error);
        var visible = CardZoneReadModel.Project(run).Zones.SelectMany(zone => zone.Cards)
            .Select(card => card.CardInstanceId).Distinct().Order().ToArray();
        var items = ImmutableArray.CreateBuilder<CardModifierItemView>();
        foreach (var instance in inventory.Items)
        {
            var acquired = runtimes.Resolve(instance.AcquisitionRevision, run.ConfigName);
            if (acquired.IsFailure) return Result<CardModifierItemSnapshot>.Failure(acquired.Error);
            var definition = acquired.Value.GetDefinition<CardModifierItemDefinition>("card-modifier-items", instance.ItemDefinitionId);
            if (definition.IsFailure) return Result<CardModifierItemSnapshot>.Failure(definition.Error);
            var upgrade = acquired.Value.GetDefinition<CardUpgradeDefinition>("card-upgrades", definition.Value.UpgradeId);
            if (upgrade.IsFailure) return Result<CardModifierItemSnapshot>.Failure(upgrade.Error);
            var sealedUpgrade = CardBundleCompiler.Seal(upgrade.Value, acquired.Value);
            if (sealedUpgrade.IsFailure) return Result<CardModifierItemSnapshot>.Failure(sealedUpgrade.Error);
            var cards = ImmutableArray.CreateBuilder<CardModifierItemCompatibility>();
            foreach (var id in visible)
            {
                var card = run.Deck.GetCard(id)!;
                string? reason = instance.State != CardModifierItemState.Available ? "Item is not available" :
                    run.ActiveEncounterId != null ? "Application requires leaving combat" :
                    run.Lifecycle != RunLifecycleState.Active ? "Run is not active" : null;
                var eligible = RunEligibility.Require(run, Core.Meta.UnlockTargetKind.CardUpgrade, definition.Value.UpgradeId);
                if (reason == null && eligible.IsFailure) reason = eligible.Error;
                if (reason == null)
                {
                    var changed = CardInstanceUpgradeTransitions.Apply(card, sealedUpgrade.Value, instance.AcquisitionRevision);
                    if (changed.IsFailure) reason = changed.Error;
                    else
                    {
                        var compiled = compiler.Compile(card.DefinitionId, active.Value);
                        if (compiled.IsFailure) return Result<CardModifierItemSnapshot>.Failure(compiled.Error);
                        var composed = composer.Resolve(compiled.Value, changed.Value);
                        if (composed.IsFailure) reason = composed.Error;
                    }
                }
                cards.Add(new(id, reason == null, reason));
            }
            items.Add(new(instance, definition.Value, cards.ToImmutable()));
        }
        return Result<CardModifierItemSnapshot>.Success(new(run.RunId, run.Sequence,
            run.Determinism.ContentRevision, catalog.ToImmutable(), items.ToImmutable()));
    }
}
