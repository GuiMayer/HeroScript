using System.Collections.Immutable;
using Core.CardZones;
using Core.Common;
using Core.Content;

namespace Core.Run.Content;

public sealed record CardModifierItemApplicationPlan(RunState Candidate,
    CardPresentationFacts Before, CardPresentationFacts After, Guid ItemInstanceId);

/// <summary>One pure authority for preview, compatibility and canonical application. No resource or RNG mutation.</summary>
public sealed class CardModifierItemApplicationPlanner(IContentRuntimeResolver runtimes,
    ICardContentCompiler compiler, IEffectiveCardResolver composer)
{
    public Result<CardModifierItemApplicationPlan> Plan(RunState run, Guid itemId, Guid cardId)
    {
        Result<CardModifierItemApplicationPlan> Fail(string reason) => Result<CardModifierItemApplicationPlan>.Failure(reason);
        if (run.Lifecycle != RunLifecycleState.Active || run.ActiveEncounterId != null)
            return Fail("Modifier item application requires an active run outside combat");
        var inventory = run.CardModifierItems ?? new();
        var valid = inventory.Validate();
        if (valid.IsFailure) return Fail(valid.Error);
        var item = inventory.Items.FirstOrDefault(value => value.ItemInstanceId == itemId);
        if (item == null || item.State != CardModifierItemState.Available) return Fail("Modifier item is not available");
        if (!CardZoneReadModel.Project(run).Zones.SelectMany(zone => zone.Cards).Any(card => card.CardInstanceId == cardId))
            return Fail("Card is not visible under the current zone policy");
        var card = run.Deck.GetCard(cardId);
        if (card == null) return Fail("Card instance not found");
        var active = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        var acquired = runtimes.Resolve(item.AcquisitionRevision, run.ConfigName);
        if (active.IsFailure) return Fail(active.Error);
        if (acquired.IsFailure) return Fail(acquired.Error);
        if (active.Value.Manifest.Revision != run.Determinism.ContentRevision || acquired.Value.Manifest.Revision != item.AcquisitionRevision ||
            !string.Equals(active.Value.Manifest.ConfigName, run.ConfigName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(acquired.Value.Manifest.ConfigName, run.ConfigName, StringComparison.OrdinalIgnoreCase))
            return Fail("Modifier item runtime revision or setting does not match its owner");
        var definition = acquired.Value.GetDefinition<CardModifierItemDefinition>("card-modifier-items", item.ItemDefinitionId);
        if (definition.IsFailure) return Fail(definition.Error);
        valid = definition.Value.Validate(acquired.Value);
        if (valid.IsFailure) return Fail(valid.Error);
        valid = RunEligibility.Require(run, Core.Meta.UnlockTargetKind.CardUpgrade, definition.Value.UpgradeId);
        if (valid.IsFailure) return Fail(valid.Error);
        var upgrade = acquired.Value.GetDefinition<CardUpgradeDefinition>("card-upgrades", definition.Value.UpgradeId);
        if (upgrade.IsFailure) return Fail(upgrade.Error);
        var closed = CardBundleCompiler.Seal(upgrade.Value, acquired.Value);
        if (closed.IsFailure) return Fail(closed.Error);
        var changed = CardInstanceUpgradeTransitions.Apply(card, closed.Value, item.AcquisitionRevision);
        if (changed.IsFailure) return Fail(changed.Error);
        var compiled = compiler.Compile(card.DefinitionId, active.Value);
        if (compiled.IsFailure) return Fail(compiled.Error);
        var before = composer.Resolve(compiled.Value, card);
        var after = composer.Resolve(compiled.Value, changed.Value);
        if (before.IsFailure) return Fail(before.Error);
        if (after.IsFailure) return Fail(after.Error);
        valid = GameplayContentValidator.ValidateCardContainer(active.Value, $"card-instances/{cardId}", after.Value.Components);
        if (valid.IsFailure) return Fail(valid.Error);
        var identity = new CardPresentationIdentity { Kind = CardPresentationSubjectKind.Transformation,
            DefinitionId = card.DefinitionId, CardInstanceId = cardId };
        var version = new CardPresentationVersion { RunId = run.RunId, RunSequence = run.Sequence, ContentRevision = run.Determinism.ContentRevision };
        var beforeFacts = CardBasePresentationProjector.Project(compiled.Value, before.Value, identity, version);
        var afterFacts = CardBasePresentationProjector.Project(compiled.Value, after.Value, identity, version);
        if (beforeFacts.IsFailure) return Fail(beforeFacts.Error);
        if (afterFacts.IsFailure) return Fail(afterFacts.Error);
        var used = item with { State = definition.Value.Use == CardModifierItemUse.Consume ? CardModifierItemState.Consumed : CardModifierItemState.Bound,
            CardInstanceId = cardId, TransformationId = changed.Value.Upgrades.Last().TransformationId };
        var nextInventory = inventory with { Items = inventory.Items.Select(value => value.ItemInstanceId == itemId ? used : value).ToImmutableArray() };
        valid = nextInventory.Validate();
        if (valid.IsFailure) return Fail(valid.Error);
        var topology = run.Deck.Topology with { Instances = run.Deck.Topology.InstanceItems.SetItem(cardId, changed.Value) };
        valid = CardZoneTopologyValidator.Validate(topology);
        if (valid.IsFailure) return Fail(valid.Error);
        return Result<CardModifierItemApplicationPlan>.Success(new(run with { CardModifierItems = nextInventory,
            Deck = new() { Topology = topology }, Determinism = run.Determinism.AdvanceStep() }, beforeFacts.Value, afterFacts.Value, itemId));
    }
}
