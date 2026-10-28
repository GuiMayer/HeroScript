using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.GameplayEvents.Responses;
using Core.Run.Content;

namespace Core.Effects;

public sealed partial class EffectTriggerExecutor
{
    private Result<bool> HasAuthoredResponses(EffectTriggerExecutionRequest request)
    {
        var run = request.Run;
        if (run == null) return Result<bool>.Success(false);
        if (run.ResolvedMode?.Definition is { } mode && GameplayResponseAuthoringGuard.ContainsDeclarations(mode) ||
            run.Map.Nodes.Any(GameplayResponseAuthoringGuard.ContainsDeclarations) ||
            run.Relics.Any(GameplayResponseAuthoringGuard.ContainsDeclarations) ||
            request.Combat.GetAllActors().SelectMany(actor => actor.Components.Values).OfType<EntityResponseComponentState>()
                .Any(GameplayResponseAuthoringGuard.ContainsDeclarations) ||
            request.Combat.StatusEffects.Values.SelectMany(items => items).Any(status => GameplayResponseAuthoringGuard.ContainsDeclarations(status.Definition)) ||
            request.Card?.Components.OfType<CardResponseComponentDefinition>().Any() == true ||
            run.Deck.Topology.Instances.Values.SelectMany(card => card.Upgrades).Any(GameplayResponseAuthoringGuard.ContainsDeclarations))
            return Result<bool>.Success(true);
        if (_contentRuntimes == null) return Result<bool>.Success(false);
        var runtime = _contentRuntimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        return runtime.IsFailure ? Result<bool>.Failure(runtime.Error) : Result<bool>.Success(runtime.Value.ContainsGameplayResponses);
    }

    private ImmutableArray<IGameplayObserverProvider> AuthoredProviders() => _contentRuntimes == null
        ? [new ModeGameplayObserverProvider(), new EncounterGameplayObserverProvider(), new ActorGameplayObserverProvider(),
            new RelicGameplayObserverProvider(), new StatusGameplayObserverProvider()]
        : [new ModeGameplayObserverProvider(), new EncounterGameplayObserverProvider(), new ActorGameplayObserverProvider(),
            new RelicGameplayObserverProvider(), new StatusGameplayObserverProvider(), new CardGameplayObserverProvider(_contentRuntimes)];
}
