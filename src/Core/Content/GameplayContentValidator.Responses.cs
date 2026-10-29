using System.Collections.Immutable;
using Core.Combat.TurnPhase;
using Core.Entity.Definitions;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.StatusEffects;

namespace Core.Content;

internal sealed partial class GameplayContentValidator
{
    private void ValidateGameplayResponses()
    {
        if (!ResponseRuntime.ContainsGameplayResponses) return;
        Visit<GameModeDefinition>("modes", (path, item) => Responses(path, item.Responses, "mode"));
        Visit<StatusEffectDefinition>("status-effects", (path, item) => Responses(path, item.Responses, "status"));
        Visit<RelicDefinition>("relics", (path, item) => Responses(path, item.Responses, "relic"));
        Visit<EntityDefinition>("entities", (path, item) => {
            foreach (var component in item.Components.OfType<EntityResponseComponentDefinition>())
                Responses($"{path}/components/{component.ComponentId}", component.Responses, "entity");
        });
        Visit<RunDefinition>("runs", (path, item) => {
            foreach (var node in item.MapNodes.Where(node => node.Responses != null))
            {
                if (node.Activity.Type != RunActivityType.Encounter)
                    Error(path, "Node responses currently require an encounter container");
                Responses($"{path}/mapNodes/{node.NodeId}", node.Responses, "encounter");
            }
        });
        Visit<PhaseSequenceDefinition>("phase-sequences", (path, item) => {
            foreach (var phase in item.Phases)
                Responses($"{path}/phases/{phase.PhaseId}", phase.Responses, "phase", phase.PhaseId);
        });
    }

    private void Responses(string path, ImmutableArray<GameplayResponseDefinition>? declarations, string container, string? phaseId = null)
    {
        if (declarations == null) return;
        var items = declarations.Value;
        if (items.IsDefault || items.Length > 16 || items.Any(item => item == null) ||
            items.Select(item => item.ResponseId).Distinct(StringComparer.Ordinal).Count() != items.Length)
        { Error(path, "Response declarations are invalid, duplicate or exceed the container budget"); return; }
        var catalog = ResponseRuntime.GameplayCatalog;
        if (catalog.IsFailure) { Error(path, catalog.Error); return; }
        foreach (var response in items)
        {
            var address = $"{path}/responses/{response.ResponseId}";
            var lifecycle = response.Timing == ResponseExecutionTiming.ContainerBoundary;
            if (lifecycle && container is not ("status" or "relic" or "phase") || container == "phase" && !lifecycle)
            { Error(address, "This response timing is not supported by the declared container"); continue; }
            var compiled = GameplayResponseCompiler.Compile(response, catalog.Value, new() { ContainerBoundary = lifecycle });
            if (compiled.IsFailure) { Error(address, compiled.Error); continue; }
            if (lifecycle)
            {
                var timing = container == "phase" ? GameplayContainerBoundaryResponses.ValidatePhase(response, phaseId!)
                    : GameplayContainerBoundaryResponses.Validate(response, container);
                if (timing.IsFailure) { Error(address, timing.Error); continue; }
            }
            if (container is "mode" or "encounter" && response.Bindings.Owner.Kind == ResponseActorBindingKind.ObserverOwner)
                Error(address, "Ownerless containers require an explicit owner binding");
            if (container != "card" && response.Availability.Kind != ResponseAvailabilityKind.ContainerActive)
                Error(address, "Card availability policies require a card container");
            foreach (var id in response.Observe.Resource?.ResourceIds ?? []) Reference(address, "resources", id, required: true);
            foreach (var id in response.Availability.ZoneIds)
                if (!ResponseRuntime.GetDefinitions("card-zone-systems").Keys.Select(systemId => ResponseRuntime.GetDefinition<Core.CardZones.CardZoneSystemDefinition>("card-zone-systems", systemId))
                    .Any(system => system.IsSuccess && system.Value.Zones.Any(zone => zone.ZoneId == id)))
                    Error(address, $"Unknown availability zone: {id}");
            var previous = _responseFormulaContext; var inherited = _responseInheritedVariables;
            _responseFormulaContext = true; _responseInheritedVariables = false;
            try
            {
                Formula(address, response.Condition);
                _responseInheritedVariables = lifecycle && container is "status" or "relic";
                Effects(address, response.Effects);
            }
            finally { _responseFormulaContext = previous; _responseInheritedVariables = inherited; }
        }
    }
}
