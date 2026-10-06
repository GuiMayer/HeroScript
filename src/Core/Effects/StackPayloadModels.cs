using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.Common;
using Core.Determinism;
using Core.Run.Content;

namespace Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StackParameterEvaluation { Snapshot, Dynamic }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StackMissingSourcePolicy { Fail, SkipContribution, UseOwner }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StackPayloadReapplyPolicy { PreserveLots, ReplaceAll }

public sealed record StackPayloadParameterDefinition
{
    public string ParameterId { get; init; } = string.Empty;
    public StackParameterEvaluation Evaluation { get; init; }
    public StackMissingSourcePolicy MissingSource { get; init; }
    public EffectNumericParameterDefinition Numeric { get; init; } = new();
}

public sealed record StackPayloadBinding
{
    public string ParameterId { get; init; } = string.Empty;
    public string? CardEffectComponentId { get; init; }
    public float? FlatValue { get; init; }
    public string? FormulaValue { get; init; }
}

public sealed record StackPayloadParameterState
{
    public StackPayloadParameterDefinition Definition { get; init; } = new();
    public CalculationQuantity? Snapshot { get; init; }
    public ImmutableHashSet<string> Tags { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
}

/// <summary>One application contribution inside the canonical status/modifier instance.</summary>
public sealed record StackPayloadLot
{
    public string LotId { get; init; } = string.Empty;
    public int Stacks { get; init; }
    public string ContentRevision { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string OwnerEntityId { get; init; } = string.Empty;
    public EffectProvenance Origin { get; init; } = new();
    public Guid? CardInstanceId { get; init; }
    public string? CardDefinitionId { get; init; }
    public string? CardFingerprint { get; init; }
    public ImmutableArray<CardInfluenceComponentDefinition> CardInfluences { get; init; } = [];
    public ImmutableSortedDictionary<string, float> Variables { get; init; } =
        ImmutableSortedDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableSortedDictionary<string, StackPayloadParameterState> Parameters { get; init; } =
        ImmutableSortedDictionary<string, StackPayloadParameterState>.Empty.WithComparers(StringComparer.Ordinal);
}

public static class StackPayloadPolicies
{
    public const int MaximumLots = 256;
    public const int MaximumParameters = 16;

    public static Result ValidateDefinitions(ImmutableArray<StackPayloadParameterDefinition> definitions, StackPayloadReapplyPolicy policy)
    {
        if (!Enum.IsDefined(policy) || definitions.Length > MaximumParameters ||
            definitions.Select(item => item.ParameterId).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            return Result.Failure("Invalid payload parameter count, IDs or reapply policy");
        foreach (var definition in definitions)
        {
            var numeric = definition.Numeric;
            if (!SafeId(definition.ParameterId) || !Enum.IsDefined(definition.Evaluation) || !Enum.IsDefined(definition.MissingSource) ||
                numeric.Parameter != EffectNumericParameter.Amount || string.IsNullOrWhiteSpace(numeric.UnitId) ||
                string.IsNullOrWhiteSpace(numeric.Channel) || string.IsNullOrWhiteSpace(numeric.PipelineId) ||
                numeric.InputQuantityId != null || numeric.Distribution != null || numeric.FlatValue == null && string.IsNullOrWhiteSpace(numeric.FormulaValue) ||
                numeric.FlatValue is { } flat && !float.IsFinite(flat) ||
                CalculationValuePolicy.Validate(numeric.Conversion).IsFailure ||
                definition.Evaluation == StackParameterEvaluation.Dynamic && HistoricalFormula(numeric.FormulaValue))
                return Result.Failure($"Invalid payload parameter {definition.ParameterId}");
        }
        return Result.Success();
    }

    public static bool SafeId(string? id) => !string.IsNullOrEmpty(id) && (char.IsAsciiLetter(id[0]) || id[0] == '_') &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    internal static bool HistoricalFormula(string? formula) => formula != null &&
        (formula.Contains("results.", StringComparison.OrdinalIgnoreCase) || formula.Contains("parent.", StringComparison.OrdinalIgnoreCase));

    public static Result<ImmutableArray<StackPayloadLot>> Merge(ImmutableArray<StackPayloadLot> existing, int previousCount,
        StackPayloadLot? incoming, int newCount, StackReapplyPolicy stacking, StackPayloadReapplyPolicy payloadPolicy,
        ImmutableArray<StackPayloadParameterDefinition> definitions)
    {
        var validation = ValidateDefinitions(definitions, payloadPolicy);
        if (validation.IsFailure) return Result<ImmutableArray<StackPayloadLot>>.Failure(validation.Error);
        if (definitions.IsEmpty)
            return incoming == null && existing.IsEmpty ? Result<ImmutableArray<StackPayloadLot>>.Success([])
                : Result<ImmutableArray<StackPayloadLot>>.Failure("Payload supplied to an instance without parameter definitions");
        if (incoming == null || incoming.Stacks < 1 || !ValidLot(incoming, definitions) ||
            previousCount > 0 && (existing.IsEmpty || existing.Sum(lot => (long)lot.Stacks) != previousCount ||
                existing.Any(lot => !ValidLot(lot, definitions))))
            return Result<ImmutableArray<StackPayloadLot>>.Failure("Missing or inconsistent stack payload lots");
        ImmutableArray<StackPayloadLot> result;
        if (previousCount == 0 || payloadPolicy == StackPayloadReapplyPolicy.ReplaceAll || stacking == StackReapplyPolicy.Replace)
            result = [incoming with { Stacks = newCount }];
        else
        {
            if (newCount < previousCount) return Result<ImmutableArray<StackPayloadLot>>.Failure("Unsupported payload merge count reduction");
            result = newCount == previousCount ? existing : existing.Add(incoming with { Stacks = newCount - previousCount });
        }
        return result.Length <= MaximumLots && result.Select(lot => lot.LotId).Distinct(StringComparer.Ordinal).Count() == result.Length
            ? Result<ImmutableArray<StackPayloadLot>>.Success(result)
            : Result<ImmutableArray<StackPayloadLot>>.Failure("Duplicate lots or payload lot limit exceeded");
    }

    public static bool ValidLot(StackPayloadLot lot, ImmutableArray<StackPayloadParameterDefinition> definitions) =>
        !string.IsNullOrWhiteSpace(lot.LotId) && !string.IsNullOrWhiteSpace(lot.ContentRevision) && lot.Stacks > 0 &&
        !string.IsNullOrWhiteSpace(lot.SourceEntityId) && !string.IsNullOrWhiteSpace(lot.OwnerEntityId) &&
        lot.Parameters.Keys.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(definitions.Select(item => item.ParameterId)
            .OrderBy(id => id, StringComparer.Ordinal)) && lot.Parameters.All(pair => pair.Key == pair.Value.Definition.ParameterId &&
                Compatible(pair.Value.Definition, definitions.Single(definition => definition.ParameterId == pair.Key)) &&
                ValidateDefinitions([pair.Value.Definition], StackPayloadReapplyPolicy.PreserveLots).IsSuccess &&
                (pair.Value.Definition.Evaluation == StackParameterEvaluation.Dynamic ? pair.Value.Snapshot == null :
                    pair.Value.Snapshot is { } quantity && quantity.UnitId == pair.Value.Definition.Numeric.UnitId &&
                    quantity.ContentRevision == lot.ContentRevision && float.IsFinite(quantity.Value) &&
                    !string.IsNullOrWhiteSpace(quantity.CalculationFingerprint) && !quantity.IncorporatedStages.IsEmpty));

    private static bool Compatible(StackPayloadParameterDefinition actual, StackPayloadParameterDefinition schema) =>
        actual.Evaluation == schema.Evaluation && actual.MissingSource == schema.MissingSource &&
        CanonicalJson.ComputeHash(actual.Numeric) == CanonicalJson.ComputeHash(schema.Numeric with
        { FlatValue = actual.Numeric.FlatValue, FormulaValue = actual.Numeric.FormulaValue });

    /// <summary>Partial ordinary removal preserves contributions in application order (oldest consumed first).</summary>
    public static ImmutableArray<StackPayloadLot> RemoveOldest(ImmutableArray<StackPayloadLot> lots, int removed)
    {
        var retained = ImmutableArray.CreateBuilder<StackPayloadLot>();
        foreach (var lot in lots)
        {
            var decrement = System.Math.Min(lot.Stacks, System.Math.Max(0, removed));
            removed -= decrement;
            if (lot.Stacks > decrement) retained.Add(lot with { Stacks = lot.Stacks - decrement });
        }
        return retained.ToImmutable();
    }
}
