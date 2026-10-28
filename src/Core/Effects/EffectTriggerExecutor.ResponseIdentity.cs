using Core.Determinism;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;

namespace Core.Effects;

public sealed partial class EffectTriggerExecutor
{
    /// <summary>Effect identity uses the domain cause, never the diagnostic envelope's producer path or ordinal.</summary>
    private static string ResponseTriggerIdentity(GameplayResponseCandidate candidate)
    {
        var fact = candidate.Occurrence;
        object payload = fact.Payload switch
        {
            BoundaryOccurrencePayload boundary => new { boundary.BoundaryId },
            ActionOccurrencePayload action => new { action.ActionDefinitionId },
            ResourceChangePayload resource => new { resource.ResourceId, resource.Owner, resource.Field, resource.Operation,
                resource.Operand, resource.Before, resource.RequestedValue, resource.After, resource.Role, resource.CalculationChannel },
            _ => fact.Payload
        };
        return CanonicalJson.ComputeHash(new { candidate.Registration.RegistrationId, fact.Kind, fact.ReasonId,
            fact.SourceActorId, fact.SubjectActorId, fact.AttributedActorId, fact.ImpactId, fact.ProcId, payload,
            snapshot = fact.ImpactId == null ? CanonicalJson.ComputeHash(new { candidate.Snapshot.Run, candidate.Snapshot.Combat }) : null });
    }
}
