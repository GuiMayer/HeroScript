using System.Collections.Immutable;
using Core.Common;
using Core.Combat.TurnPhase;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.StatusEffects;

namespace Core.Content.Migrations;

/// <summary>Pure typed migration. Neither edits files nor publishes revisions or changes captured historical bundles.</summary>
public static class GameplayBoundaryResponseMigration
{
    public static Result<StatusEffectDefinition> Status(StatusEffectDefinition definition, GameplayEventCatalog catalog)
    {
        var converted = Convert(definition.Triggers, definition.Responses, catalog, fromSource: true);
        return converted.IsFailure ? Result<StatusEffectDefinition>.Failure(converted.Error)
            : Result<StatusEffectDefinition>.Success(definition.Triggers.Count == 0 ? definition : definition with { Triggers = [], Responses = converted.Value });
    }

    public static Result<RelicDefinition> Relic(RelicDefinition definition, GameplayEventCatalog catalog)
    {
        if (definition.Reactions is { IsDefault: true }) return Result<RelicDefinition>.Failure("Resource reactions are invalid");
        if (definition.Triggers.Count == 0 && definition.Reactions is not { IsEmpty: false })
            return Result<RelicDefinition>.Success(definition);
        var converted = Convert(definition.Triggers, definition.Responses, catalog, fromSource: false);
        if (converted.IsFailure) return Result<RelicDefinition>.Failure(converted.Error);
        var reactions = GameplayResourceResponseMigration.Convert(definition.Reactions ?? [], catalog);
        if (reactions.IsFailure) return Result<RelicDefinition>.Failure(reactions.Error);
        if (definition.Reactions is { IsEmpty: false } && definition.Responses is { IsEmpty: false })
            return Result<RelicDefinition>.Failure("Mixed historical and canonical reaction authoring is ambiguous");
        var all = (converted.Value ?? []).AddRange(reactions.Value);
        if (all.Length > 16 || all.Select(item => item.ResponseId).Distinct(StringComparer.Ordinal).Count() != all.Length)
            return Result<RelicDefinition>.Failure("Combined response IDs are ambiguous or exceed their budget");
        return Result<RelicDefinition>.Success(definition with { Triggers = [], Reactions = null, Responses = all });
    }

    public static Result<PhaseSequenceDefinition> Phases(PhaseSequenceDefinition sequence, GameplayEventCatalog catalog)
    {
        var phases = ImmutableArray.CreateBuilder<PhaseDefinition>();
        var changed = false;
        foreach (var phase in sequence.Phases)
        {
            if (phase.EntryEffects.Count == 0 && phase.ExitEffects.Count == 0) { phases.Add(phase); continue; }
            if (phase.Responses is { IsEmpty: false } || phase.Responses is { IsDefault: true })
                return Result<PhaseSequenceDefinition>.Failure("Mixed historical and canonical phase authoring is ambiguous");
            var responses = ImmutableArray.CreateBuilder<GameplayResponseDefinition>();
            foreach (var (label, kind, effects) in new[] { ("entry", "phase.entered", phase.EntryEffects), ("exit", "phase.exited", phase.ExitEffects) })
            {
                if (effects.Count == 0) continue;
                var response = new GameplayResponseDefinition { ResponseId = $"phase:{sequence.SequenceId}:{phase.PhaseId}:{label}",
                    Timing = ResponseExecutionTiming.ContainerBoundary,
                    Observe = new() { Kinds = new() { Include = [kind] }, Boundary = new() { BoundaryIds = [phase.PhaseId] } },
                    Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner }, Missing = MissingResponseBinding.RejectAction },
                    Availability = new() { RequireLivingOwner = false }, Propagation = ResponsePropagation.AllowResponses,
                    Order = new() { Descending = true }, Effects = effects.ToImmutableArray() };
                var compiled = GameplayResponseCompiler.Compile(response, catalog, new() { ContainerBoundary = true });
                if (compiled.IsFailure) return Result<PhaseSequenceDefinition>.Failure(compiled.Error);
                responses.Add(response);
            }
            phases.Add(phase with { EntryEffects = [], ExitEffects = [], Responses = responses.ToImmutable() }); changed = true;
        }
        return Result<PhaseSequenceDefinition>.Success(changed ? sequence with { Phases = phases.ToImmutable() } : sequence);
    }

    private static Result<ImmutableArray<GameplayResponseDefinition>?> Convert(IReadOnlyList<EffectTriggerDefinition> triggers,
        ImmutableArray<GameplayResponseDefinition>? existing, GameplayEventCatalog catalog, bool fromSource)
    {
        if (triggers.Count == 0) return Result<ImmutableArray<GameplayResponseDefinition>?>.Success(existing);
        if (existing is { IsEmpty: false } || existing is { IsDefault: true } || triggers.Count > 16 ||
            triggers.Select(trigger => trigger.TriggerId).Distinct(StringComparer.Ordinal).Count() != triggers.Count)
            return Result<ImmutableArray<GameplayResponseDefinition>?>.Failure("Ambiguous mixed authoring, duplicate IDs or migration budget exceeded");
        var result = ImmutableArray.CreateBuilder<GameplayResponseDefinition>();
        foreach (var trigger in triggers)
        {
            var response = new GameplayResponseDefinition
            {
                ResponseId = trigger.TriggerId, Timing = ResponseExecutionTiming.ContainerBoundary,
                Observe = new() { Kinds = new() { Include = [GameplayBoundarySources.Kind(trigger.Boundary)] },
                    Boundary = new() { BoundaryIds = [trigger.Boundary] } },
                Bindings = new() { Source = new() { Kind = fromSource ? ResponseActorBindingKind.ObserverSource : ResponseActorBindingKind.Owner },
                    Target = new() { Kind = ResponseActorBindingKind.Owner }, Missing = MissingResponseBinding.RejectAction },
                Availability = new() { RequireLivingOwner = false }, Propagation = ResponsePropagation.AllowResponses,
                Order = new() { Priority = trigger.Priority, Descending = true }, Effects = trigger.Effects.ToImmutableArray()
            };
            var timing = GameplayContainerBoundaryResponses.Validate(response, fromSource ? "status" : "relic");
            var compiled = GameplayResponseCompiler.Compile(response, catalog, new() { ContainerBoundary = true });
            if (timing.IsFailure || compiled.IsFailure)
                return Result<ImmutableArray<GameplayResponseDefinition>?>.Failure(timing.IsFailure ? timing.Error : compiled.Error);
            result.Add(response);
        }
        return Result<ImmutableArray<GameplayResponseDefinition>?>.Success(result.ToImmutable());
    }
}
