using System.Collections.Immutable;
using Core.CardZones;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Content;
using Core.Run;
using Core.Run.Content;

namespace Core.Effects;

public sealed record RunEffectApplication(RunState Run, EffectApplicationRecord Record);

/// <summary>Run-owned primitives. No repository, event bus, or mutable manager is consulted.</summary>
public static class RunEffectReducer
{
    private static EffectStackChange StackChange(ModifierStackApplicationRecord change, EffectStackChangeReason reason,
        GameplayOwner owner) => new()
    {
        Store = EffectStackStore.Modifier, InstanceId = change.ModifierInstanceId, DefinitionId = change.ModifierId,
        PreviousStacks = change.PreviousStacks, CurrentStacks = change.CurrentStacks, Reason = reason, Owner = owner
    };

    public static bool Supports(EffectDefinition effect) => effect.Type is EffectType.CARD_ZONE_FLOW or
        EffectType.APPLY_MODIFIER or EffectType.REMOVE_MODIFIER ||
        effect.Type == EffectType.MODIFY_ATTRIBUTE && effect.AttributeMutation?.Lifetime == Core.Entity.AttributeLifetime.RunBase;

    public static Result<RunEffectApplication> Apply(RunState run, CombatState combat, ResolvedEffectCommand command,
        IContentRuntimeResolver? runtimes, string revision, ICardZoneFlowExecutor? cardZoneFlows = null)
    {
        var effect = command.Definition;
        var targetId = command.TargetEntityIds.Single();
        var record = new EffectApplicationRecord
        {
            EffectInstanceId = command.EffectInstanceId, EffectType = effect.Type,
            TargetEntityId = targetId, Provenance = command.Provenance, Identity = command.Identity
        };
        if (effect.Type == EffectType.MODIFY_ATTRIBUTE)
        {
            if (run.PlayerEntity == null || targetId != run.PlayerEntityId || combat.CombatId != Guid.Empty || run.GetActiveEncounter() != null)
                return Result<RunEffectApplication>.Failure("Persistent attributes can change only on the captured run player outside an encounter");
            var changed = Core.Entity.EntityAttributeTransitions.Apply(run.PlayerEntity, effect.AttributeMutation!, command.ResolvedValue);
            return changed.IsFailure ? Result<RunEffectApplication>.Failure(changed.Error) :
                Result<RunEffectApplication>.Success(new(run with { PlayerEntity = changed.Value.State }, record with
                { AttributeOutcome = changed.Value.Outcome, CalculationId = command.Calculation?.CalculationId,
                    CalculationFingerprint = command.Calculation?.Fingerprint }));
        }
        if (effect.Type is EffectType.APPLY_MODIFIER or EffectType.REMOVE_MODIFIER)
        {
            if (string.IsNullOrWhiteSpace(effect.ModifierId)) return Result<RunEffectApplication>.Failure("Modifier effect requires modifierId");
            var owner = effect.ModifierOwner ?? new GameplayOwner { Kind = GameplayOwnerKind.Entity, Id = targetId };
            if (owner.Kind == GameplayOwnerKind.Run && string.IsNullOrWhiteSpace(owner.Id)) owner = owner with { Id = run.RunId.ToString() };
            if (owner.Kind == GameplayOwnerKind.Run && owner.Id != run.RunId.ToString() ||
                owner.Kind == GameplayOwnerKind.Entity && combat.GetActor(owner.Id) == null ||
                owner.Kind == GameplayOwnerKind.Side && !combat.GetAllActors().Any(entity => combat.GetSideId(entity) == owner.Id))
                return Result<RunEffectApplication>.Failure("Modifier owner is outside this run/combat");
            if (effect.Type == EffectType.REMOVE_MODIFIER)
            {
                var removed = run.Modifiers
                    .Where(item => item.ModifierId == effect.ModifierId && item.Owner == owner)
                    .OrderBy(item => item.InstanceId)
                    .ToArray();
                if (effect.ModifierStacks is <= 0) return Result<RunEffectApplication>.Failure("Removed stack count must be positive");
                if (effect.ModifierStacks is { } decrement)
                {
                    var changes = removed.Select(item => new ModifierStackApplicationRecord
                    {
                        ModifierInstanceId = item.InstanceId,
                        ModifierId = item.ModifierId,
                        PreviousStacks = item.Stacks,
                        CurrentStacks = System.Math.Max(0, item.Stacks - decrement),
                        Removed = item.Stacks <= decrement
                    }).ToImmutableArray();
                    var remaining = run.Modifiers.Select(item => removed.Contains(item) ? item with
                        { Stacks = item.Stacks - decrement, PayloadLots = StackPayloadPolicies.RemoveOldest(item.PayloadLots, decrement) } : item)
                        .Where(item => item.Stacks > 0).ToImmutableArray();
                    return Result<RunEffectApplication>.Success(new(run with { Modifiers = remaining }, record with
                    {
                        ModifierId = effect.ModifierId,
                        RemovedModifierInstanceIds = changes.Where(item => item.Removed)
                            .Select(item => item.ModifierInstanceId).ToImmutableArray(),
                        StackChanges = changes.Select(change => StackChange(change, EffectStackChangeReason.Remove, owner)).ToImmutableArray()
                    }));
                }
                var removals = removed.Select(item => new ModifierStackApplicationRecord
                {
                    ModifierInstanceId = item.InstanceId,
                    ModifierId = item.ModifierId,
                    PreviousStacks = item.Stacks,
                    CurrentStacks = 0,
                    Removed = true
                }).ToImmutableArray();
                return Result<RunEffectApplication>.Success(new(run with
                    { Modifiers = run.Modifiers.RemoveRange(removed) }, record with
                {
                    ModifierId = effect.ModifierId,
                    RemovedModifierInstanceIds = removals.Select(item => item.ModifierInstanceId).ToImmutableArray(),
                    StackChanges = removals.Select(change => StackChange(change, EffectStackChangeReason.Remove, owner)).ToImmutableArray()
                }));
            }
            if (runtimes == null) return Result<RunEffectApplication>.Failure("Pinned modifier runtime is unavailable");
            var runtime = runtimes.Resolve(revision, run.ConfigName);
            if (runtime.IsFailure) return Result<RunEffectApplication>.Failure(runtime.Error);
            var definition = runtime.Value.GetDefinition<ScriptModifierDefinition>("modifiers", effect.ModifierId);
            if (definition.IsFailure) return Result<RunEffectApplication>.Failure(definition.Error);
            var applied = ModifierTransitions.Apply(run, definition.Value, owner, command.SourceEntityId,
                effect.ModifierStacks, effect.ModifierDuration, contentRevision: revision, payloadLot: command.PayloadLot);
            var previousStacks = applied.IsSuccess
                ? run.Modifiers.FirstOrDefault(item => item.InstanceId == applied.Value.Instance.InstanceId)?.Stacks ?? 0
                : 0;
            return applied.IsFailure ? Result<RunEffectApplication>.Failure(applied.Error)
                : Result<RunEffectApplication>.Success(new(applied.Value.Run,
                    record with
                    {
                        ModifierId = effect.ModifierId,
                        ModifierInstanceId = applied.Value.Instance.InstanceId,
                        StackChanges = [new()
                        {
                            Store = EffectStackStore.Modifier, InstanceId = applied.Value.Instance.InstanceId,
                            DefinitionId = effect.ModifierId, PreviousStacks = previousStacks,
                            Owner = applied.Value.Instance.Owner,
                            CurrentStacks = applied.Value.Instance.Stacks,
                            Reason = previousStacks > 0 ? EffectStackChangeReason.Reapply : EffectStackChangeReason.Apply
                        }]
                    }));
        }
        if (string.IsNullOrWhiteSpace(effect.CardZoneFlowId))
            return Result<RunEffectApplication>.Failure(
                $"Card-zone effect {effect.EffectId} requires an authored cardZoneFlowId");
        if (combat.GetActor(targetId) == null)
            return Result<RunEffectApplication>.Failure("Card-zone effect target is not a combat actor");
        if (effect.CardCount < 1 || effect.CardCount > EffectExecutionLimits.MaximumSteps)
            return Result<RunEffectApplication>.Failure("Card count is outside execution limits");
        if (!string.IsNullOrWhiteSpace(effect.CardDefinitionId))
        {
            if (runtimes == null)
                return Result<RunEffectApplication>.Failure("Pinned card content is unavailable");
            var runtime = runtimes.Resolve(revision, run.ConfigName);
            if (runtime.IsFailure) return Result<RunEffectApplication>.Failure(runtime.Error);
            var card = runtime.Value.GetDefinition<CardContentDefinition>("cards", effect.CardDefinitionId);
            if (card.IsFailure) return Result<RunEffectApplication>.Failure(card.Error);
        }
        var definitions = string.IsNullOrWhiteSpace(effect.CardDefinitionId)
            ? [] : Enumerable.Repeat(effect.CardDefinitionId, effect.CardCount).ToArray();
        var flowed = CardZoneRunFlowDispatcher.InvokeEffect(cardZoneFlows, run,
            effect.CardZoneFlowId, command.SourceEntityId, targetId,
            effect.CardInstanceIds, definitions, effect.CardCount);
        if (flowed.IsFailure) return Result<RunEffectApplication>.Failure(flowed.Error);
        var zoneAffected = flowed.Value.Steps
            .SelectMany(step => step.InstanceIds.Concat(step.CreatedInstanceIds).Concat(step.DestroyedInstanceIds))
            .Distinct().ToImmutableArray();
        return Result<RunEffectApplication>.Success(new(
            run with
            {
                Deck = new DeckState { Topology = flowed.Value.State },
                Determinism = flowed.Value.Context
            }, record with
            {
                CardInstanceIds = zoneAffected,
                CardZoneSteps = flowed.Value.Steps
            }));
    }
}
