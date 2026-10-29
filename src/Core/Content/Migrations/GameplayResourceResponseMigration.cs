using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Core.Common;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;

namespace Core.Content.Migrations;

/// <summary>Converts typed resource filters and inline numeric references, without guessing from resource names or delta signs.</summary>
internal static class GameplayResourceResponseMigration
{
    internal static Result<ImmutableArray<GameplayResponseDefinition>> Convert(ImmutableArray<EffectReactionDefinition> reactions, GameplayEventCatalog catalog)
    {
        var valid = EffectReactionPolicies.Validate(reactions);
        if (valid.IsFailure) return Result<ImmutableArray<GameplayResponseDefinition>>.Failure(valid.Error);
        var result = ImmutableArray.CreateBuilder<GameplayResponseDefinition>();
        foreach (var reaction in reactions)
        {
            var effects = ImmutableArray.CreateBuilder<EffectDefinition>();
            foreach (var effect in reaction.Effects)
            {
                var converted = Effect(effect, 0);
                if (converted.IsFailure) return Result<ImmutableArray<GameplayResponseDefinition>>.Failure(converted.Error);
                effects.Add(converted.Value);
            }
            var definition = new GameplayResponseDefinition
            {
                ResponseId = reaction.ReactionId,
                Observe = new() { Kinds = new() { Include = ["resource.change.resolved"] },
                    Reasons = new() { Include = ["**"], Exclude = ["cost.payment", "cost.refund"] },
                    Source = new() { MatchesOwner = reaction.OwnerScope == EffectReactionOwnerScope.Source ? true : null },
                    Subject = new() { MatchesOwner = reaction.OwnerScope == EffectReactionOwnerScope.Target ? true : null },
                    SourceSubjectRelationship = reaction.TargetRelationship,
                    Resource = new() { ResourceIds = [reaction.ResourceId], Field = reaction.ResourceField, Operation = reaction.Operation,
                        Role = ResourceOccurrenceRole.PrimaryEffect, CalculationChannels = reaction.Channels.Distinct(StringComparer.Ordinal).ToImmutableArray() },
                    RequiredTags = reaction.RequiredTags.Distinct(StringComparer.Ordinal).ToImmutableArray(),
                    ExcludedTags = reaction.ExcludedTags.Distinct(StringComparer.Ordinal).ToImmutableArray() },
                Bindings = new() { Target = new() { Kind = reaction.Selection == EffectReactionSelection.Source
                    ? ResponseActorBindingKind.EventSource : ResponseActorBindingKind.EventSubject } },
                Availability = new() { Snapshot = ResponseSnapshot.AfterOccurrence, RequireLivingOwner = reaction.RequireLivingOwner },
                Order = new() { Group = reaction.Priority }, Effects = effects.ToImmutable(), Propagation = ResponsePropagation.RecordOnly
            };
            var compiled = GameplayResponseCompiler.Compile(definition, catalog);
            if (compiled.IsFailure) return Result<ImmutableArray<GameplayResponseDefinition>>.Failure(compiled.Error);
            result.Add(definition);
        }
        return Result<ImmutableArray<GameplayResponseDefinition>>.Success(result.ToImmutable());
    }

    private static Result<EffectDefinition> Effect(EffectDefinition effect, int depth)
    {
        if (depth > EffectExecutionLimits.MaximumDepth || effect.Chance != 1 || !effect.RandomInputs.IsEmpty ||
            effect.Target == EffectTarget.RANDOM_ENEMY || effect.Continuation != null || effect.CardZoneFlowId != null)
            return Result<EffectDefinition>.Failure("This resource response requires an explicit stochastic/continuation identity migration");
        var children = ImmutableArray.CreateBuilder<EffectDefinition>();
        foreach (var child in effect.ChainedEffects ?? [])
        {
            var converted = Effect(child, depth + 1);
            if (converted.IsFailure) return converted;
            children.Add(converted.Value);
        }
        try
        {
            return Result<EffectDefinition>.Success(effect with { FormulaValue = Expression(effect.FormulaValue), Condition = Expression(effect.Condition),
                Parameters = effect.Parameters.Select(parameter => parameter with { FormulaValue = Expression(parameter.FormulaValue) }).ToImmutableArray(),
                ChainedEffects = effect.ChainedEffects == null ? null : children.ToImmutable() });
        }
        catch (InvalidOperationException exception) { return Result<EffectDefinition>.Failure(exception.Message); }
    }

    private static string? Expression(string? expression)
    {
        if (expression == null) return null;
        // Exact numeric tokens only. Formula IDs need bundle-aware expansion; they are never rewritten by name heuristics.
        foreach (var token in expression.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (token != "stacks" && token != "$current" && token != "$initial" &&
                !new[] { "+", "-", "*", "/", "%", "^", "min", "max" }.Contains(token, StringComparer.Ordinal) &&
                !float.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) &&
                !new[] { "parent.", "source.", "target.", "run.", "event.", "scope.", "owner." }.Any(prefix => token.StartsWith(prefix, StringComparison.Ordinal)))
                throw new InvalidOperationException("Referenced formulas require bundle-aware migration before applying resource response changes");
        var mapped = Regex.Replace(expression, @"(?<![\w.])parent\.(requested_change|applied_change|limited_change)(?![\w.])", match => match.Groups[1].Value switch
        { "requested_change" => "event.change.requested_delta", "applied_change" => "event.change.applied_delta", _ => "event.change.limited_delta" });
        mapped = Regex.Replace(mapped, @"(?<![\w.])stacks(?![\w.])", "scope.container_stacks");
        if (mapped.Contains("parent.", StringComparison.Ordinal)) throw new InvalidOperationException("Unmapped parent result reference requires explicit migration");
        return mapped;
    }
}
