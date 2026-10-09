using System.Text.Json.Serialization;
using Core.Common;

namespace Core.Combat;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CombatParticipantIdentityBinding { Literal, RunPlayer }

/// <summary>
/// Separates the identity used inside one combat from the immutable content
/// definition used to materialize that participant.
/// </summary>
public sealed record CombatParticipantReference(
    string InstanceId,
    string DefinitionId,
    string SideId,
    Models.ControllerBinding ControllerBinding)
{
    public CombatParticipantIdentityBinding IdentityBinding { get; init; }
}

public static class CombatParticipantBindings
{
    public static Result<IReadOnlyList<CombatParticipantReference>> Resolve(
        IReadOnlyList<CombatParticipantReference> participants, string playerEntityId)
    {
        if (string.IsNullOrWhiteSpace(playerEntityId) || participants.Any(participant => participant == null || !Enum.IsDefined(participant.IdentityBinding)))
            return Result<IReadOnlyList<CombatParticipantReference>>.Failure("Invalid encounter participant identity binding");
        if (participants.Select(participant => participant.InstanceId).Distinct(StringComparer.Ordinal).Count() != participants.Count)
            return Result<IReadOnlyList<CombatParticipantReference>>.Failure("Encounter participant aliases must be unique before binding");
        var bound = participants.Select(participant => participant.IdentityBinding == CombatParticipantIdentityBinding.RunPlayer
            ? participant with { InstanceId = playerEntityId } : participant).ToArray();
        if (bound.Any(participant => string.IsNullOrWhiteSpace(participant.InstanceId)) ||
            bound.Select(participant => participant.InstanceId).Distinct(StringComparer.Ordinal).Count() != bound.Length)
            return Result<IReadOnlyList<CombatParticipantReference>>.Failure("Bound encounter participant identities must be nonempty and unique");
        return Result<IReadOnlyList<CombatParticipantReference>>.Success(bound);
    }
}
