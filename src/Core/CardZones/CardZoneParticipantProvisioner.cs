using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.CardZones;

public sealed record CardZoneParticipantProvisioningResult(
    CardZoneTopologyState State,
    ImmutableArray<CardZoneAddress> CreatedAddresses,
    string Fingerprint);

/// <summary>
/// Ensures empty actor-addressed containers in an existing immutable topology.
/// It never deals cards, executes lifecycle flows or advances determinism.
/// </summary>
public static class CardZoneParticipantProvisioner
{
    public static Result<CardZoneParticipantProvisioningResult> Ensure(
        CompiledCardZoneSystem system,
        CardZoneTopologyState state,
        IReadOnlyList<string> participantIds,
        string runOwnerId,
        string? scopeId = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(participantIds);
        if (string.IsNullOrWhiteSpace(runOwnerId) || runOwnerId == "$global")
            return Result<CardZoneParticipantProvisioningResult>.Failure("Participant zones require a distinct run owner");
        if (participantIds.Any(id => string.IsNullOrWhiteSpace(id) || id == "$global"))
            return Result<CardZoneParticipantProvisioningResult>.Failure("Participant zone identities must be nonempty actor identities");

        var actors = participantIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var expected = system.Zones.Values.Where(zone => zone.OwnerScope == CardZoneOwnerScope.Actor)
            .OrderBy(zone => zone.ZoneId, StringComparer.Ordinal)
            .SelectMany(zone => actors.Select(actor => new CardZoneAddress
                { ZoneId = zone.ZoneId, OwnerId = actor, ScopeId = scopeId })).ToImmutableArray();
        var missing = expected.Where(address => !state.ZoneItems.ContainsKey(address.Key)).ToImmutableArray();
        var candidate = state;
        if (!missing.IsEmpty)
        {
            var zones = state.ZoneItems.ToBuilder();
            foreach (var address in missing) zones.Add(address.Key, new CardZoneState { Address = address });
            candidate = state with { Zones = zones.ToImmutable() };
        }
        var valid = CardZoneTopologyValidator.ValidateAgainstSystem(candidate, system, runOwnerId);
        if (valid.IsFailure) return Result<CardZoneParticipantProvisioningResult>.Failure(valid.Error);
        return Result<CardZoneParticipantProvisioningResult>.Success(new(candidate, missing,
            // Structural guarantee only. The enclosing command hashes the full state.
            CanonicalJson.ComputeHash(new { system.SystemId, scopeId, ExpectedAddresses = expected })));
    }
}
