using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Resources;
using Core.Combat.Models;

namespace Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectReactionOwnerScope { Source, Target }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectReactionSelection { Source, Target }

/// <summary>Observes one primary, applied resource transition. It never computes damage.</summary>
public sealed record EffectReactionDefinition
{
    public string ReactionId { get; init; } = string.Empty;
    public int Priority { get; init; }
    public EffectReactionOwnerScope OwnerScope { get; init; }
    public EffectReactionSelection Selection { get; init; } = EffectReactionSelection.Target;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SideRelationship? TargetRelationship { get; init; }
    public string ResourceId { get; init; } = string.Empty;
    public ResourceValueField ResourceField { get; init; }
    public ResourceMutationOperation? Operation { get; init; }
    public ImmutableArray<string> Channels { get; init; } = [];
    public ImmutableArray<string> RequiredTags { get; init; } = [];
    public ImmutableArray<string> ExcludedTags { get; init; } = [];
    public bool RequireLivingOwner { get; init; } = true;
    public ImmutableArray<EffectDefinition> Effects { get; init; } = [];
}

public static class EffectReactionPolicies
{
    public static Result Validate(IEnumerable<EffectReactionDefinition> reactions)
    {
        if (reactions is ImmutableArray<EffectReactionDefinition> array && array.IsDefault)
            return Result.Failure("Effect reactions cannot be an uninitialized array");
        var items = reactions.ToArray();
        if (items.Length > 16 || items.Any(item => item == null) ||
            items.Select(item => item.ReactionId).Distinct(StringComparer.Ordinal).Count() != items.Length)
            return Result.Failure("Effect reactions require unique IDs and at most 16 entries");
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ReactionId) || string.IsNullOrWhiteSpace(item.ResourceId) ||
                !Enum.IsDefined(item.OwnerScope) || !Enum.IsDefined(item.Selection) || !Enum.IsDefined(item.ResourceField) ||
                item.TargetRelationship is { } relationship && !Enum.IsDefined(relationship) ||
                item.Operation is not { } operation || !Enum.IsDefined(operation) ||
                item.Channels.IsDefaultOrEmpty || item.Channels.Length > 16 ||
                item.RequiredTags.IsDefault || item.ExcludedTags.IsDefault ||
                item.Channels.Concat(item.RequiredTags).Concat(item.ExcludedTags).Any(string.IsNullOrWhiteSpace) ||
                item.Effects.IsDefaultOrEmpty || item.Effects.Length > 16)
                return Result.Failure("Effect reaction requires an owner scope, resource operation, channels and bounded effects");
            var errors = EffectDefinitionValidator.Validate(item.Effects);
            if (!errors.IsEmpty) return Result.Failure(string.Join("; ", errors));
        }
        return Result.Success();
    }

    public static bool Matches(EffectReactionDefinition reaction, EffectDefinition effect,
        EffectApplicationRecord application, IReadOnlySet<string> tags) =>
        application.CalculationInfluenceId == null && application.ResourceOutcome != null &&
        StringComparer.OrdinalIgnoreCase.Equals(reaction.ResourceId, application.ResourceId) &&
        reaction.ResourceField == application.ResourceField && reaction.Operation == application.ResourceOperation &&
        reaction.Channels.Contains(effect.Parameters.FirstOrDefault(parameter =>
            parameter.Parameter == EffectNumericParameter.Amount)?.Channel ?? effect.CalculationChannel) &&
        reaction.RequiredTags.All(tags.Contains) && !reaction.ExcludedTags.Any(tags.Contains);
}
