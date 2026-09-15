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
    public static bool Supports(EffectType type) => type is EffectType.DRAW_CARD or EffectType.DISCARD_CARD or
        EffectType.EXHAUST_CARD or EffectType.ADD_CARD_TO_HAND or EffectType.CARD_ZONE_FLOW or
        EffectType.APPLY_MODIFIER or EffectType.REMOVE_MODIFIER;

    public static Result<RunEffectApplication> Apply(RunState run, CombatState combat, ResolvedEffectCommand command,
        IContentRuntimeResolver? runtimes, string revision, ICardZoneFlowExecutor? cardZoneFlows = null)
    {
        var effect = command.Definition;
        var targetId = command.TargetEntityIds.Single();
        var record = new EffectApplicationRecord
        {
            EffectInstanceId = command.EffectInstanceId, EffectType = effect.Type,
            TargetEntityId = targetId, Provenance = command.Provenance
        };
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
                    var remaining = run.Modifiers.Select(item => removed.Contains(item) ? item with { Stacks = item.Stacks - decrement } : item)
                        .Where(item => item.Stacks > 0).ToImmutableArray();
                    return Result<RunEffectApplication>.Success(new(run with { Modifiers = remaining }, record with
                    {
                        ModifierId = effect.ModifierId,
                        RemovedModifierInstanceIds = changes.Where(item => item.Removed)
                            .Select(item => item.ModifierInstanceId).ToImmutableArray(),
                        ModifierStackChanges = changes
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
                    ModifierStackChanges = removals
                }));
            }
            if (runtimes == null) return Result<RunEffectApplication>.Failure("Pinned modifier runtime is unavailable");
            var runtime = runtimes.Resolve(revision, run.ConfigName);
            if (runtime.IsFailure) return Result<RunEffectApplication>.Failure(runtime.Error);
            var definition = runtime.Value.GetDefinition<ScriptModifierDefinition>("modifiers", effect.ModifierId);
            if (definition.IsFailure) return Result<RunEffectApplication>.Failure(definition.Error);
            var applied = ModifierTransitions.Apply(run, definition.Value, owner, command.SourceEntityId,
                effect.ModifierStacks, effect.ModifierDuration, contentRevision: revision);
            var previousStacks = applied.IsSuccess
                ? run.Modifiers.FirstOrDefault(item => item.InstanceId == applied.Value.Instance.InstanceId)?.Stacks ?? 0
                : 0;
            return applied.IsFailure ? Result<RunEffectApplication>.Failure(applied.Error)
                : Result<RunEffectApplication>.Success(new(applied.Value.Run,
                    record with
                    {
                        ModifierId = effect.ModifierId,
                        ModifierInstanceId = applied.Value.Instance.InstanceId,
                        ModifierStackChanges = [new()
                        {
                            ModifierInstanceId = applied.Value.Instance.InstanceId,
                            ModifierId = applied.Value.Instance.ModifierId,
                            PreviousStacks = previousStacks,
                            CurrentStacks = applied.Value.Instance.Stacks
                        }]
                    }));
        }
        if (run.ResolvedMode?.CardZoneSystem != null)
        {
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
                }, record with { CardInstanceIds = zoneAffected }));
        }
        if (effect.Type == EffectType.CARD_ZONE_FLOW)
            return Result<RunEffectApplication>.Failure("CARD_ZONE_FLOW requires a configured card-zone system");
        if (targetId != run.PlayerEntityId)
            return Result<RunEffectApplication>.Failure("Deck effects require the configured run deck owner");
        if (effect.CardCount < 1 || effect.CardCount > EffectExecutionLimits.MaximumSteps)
            return Result<RunEffectApplication>.Failure("Card count is outside execution limits");
        var handLimit = run.ResolvedMode?.CombatRules.Flow.DeckCycle.HandLimit ?? int.MaxValue;
        var before = run.Deck;
        Result<DeckTransition> transition;
        switch (effect.Type)
        {
            case EffectType.DRAW_CARD:
                var slots = System.Math.Max(0, handLimit - before.HandInstanceIds.Count);
                if (!effect.AllowPartialDraw && slots < effect.CardCount)
                    return Result<RunEffectApplication>.Failure("Draw would exceed the configured hand limit");
                transition = DeckTransitions.Draw(before, System.Math.Min(slots, effect.CardCount), run.Determinism,
                    effect.ShuffleDiscardWhenEmpty, effect.AllowPartialDraw);
                break;
            case EffectType.ADD_CARD_TO_HAND:
                if (string.IsNullOrWhiteSpace(effect.CardDefinitionId) || runtimes == null)
                    return Result<RunEffectApplication>.Failure("Adding cards requires cardDefinitionId and pinned content");
                if ((long)before.HandInstanceIds.Count + effect.CardCount > handLimit)
                    return Result<RunEffectApplication>.Failure("Adding cards would exceed the configured hand limit");
                var runtime = runtimes.Resolve(revision, run.ConfigName);
                if (runtime.IsFailure) return Result<RunEffectApplication>.Failure(runtime.Error);
                var card = runtime.Value.GetDefinition<CardContentDefinition>("cards", effect.CardDefinitionId);
                if (card.IsFailure) return Result<RunEffectApplication>.Failure(card.Error);
                transition = DeckTransitions.AddToHand(before, Enumerable.Repeat(effect.CardDefinitionId, effect.CardCount).ToArray(), run.Determinism);
                break;
            case EffectType.DISCARD_CARD:
            case EffectType.EXHAUST_CARD:
                var ids = effect.CardInstanceIds.IsEmpty ? before.HandInstanceIds.Take(effect.CardCount).ToImmutableArray() : effect.CardInstanceIds;
                if (ids.Length != effect.CardCount) return Result<RunEffectApplication>.Failure("Card selection count does not match cardCount");
                transition = DeckTransitions.MoveFromHand(before, ids.Select(id => id.ToString()).ToArray(),
                    effect.Type == EffectType.EXHAUST_CARD ? CardConsumeDestination.Exhaust : CardConsumeDestination.Discard, run.Determinism);
                break;
            default: return Result<RunEffectApplication>.Failure($"Unsupported run effect {effect.Type}");
        }
        if (transition.IsFailure) return Result<RunEffectApplication>.Failure(transition.Error);
        var affected = before.HandInstanceIds.Except(transition.Value.State.HandInstanceIds)
            .Concat(transition.Value.State.HandInstanceIds.Except(before.HandInstanceIds)).ToImmutableArray();
        return Result<RunEffectApplication>.Success(new(run with { Deck = transition.Value.State, Determinism = transition.Value.Context },
            record with { CardInstanceIds = affected }));
    }
}
