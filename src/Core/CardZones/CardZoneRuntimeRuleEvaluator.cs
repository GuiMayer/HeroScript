using Core.Common;
using Core.Content;
using Core.Math;
using Core.Run;
using Core.Run.Content;

namespace Core.CardZones;

/// <summary>
/// Reads authored card tags from the pinned content revision, including the
/// instance's upgrades. Zone rules never consult a mutable live catalog.
/// </summary>
public interface ICardZoneCardMetadataResolver
{
    Result<IReadOnlyList<string>> ResolveTags(CardInstanceState instance, CardZoneFlowContext context);
}

public sealed class RevisionedCardZoneCardMetadataResolver : ICardZoneCardMetadataResolver
{
    private readonly IContentRuntimeResolver _runtimes;
    private readonly ICardContentCompiler _compiler;
    private readonly IEffectiveCardResolver _effectiveCards;

    public RevisionedCardZoneCardMetadataResolver(
        IContentRuntimeResolver runtimes,
        ICardContentCompiler compiler,
        IEffectiveCardResolver effectiveCards)
    {
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _effectiveCards = effectiveCards ?? throw new ArgumentNullException(nameof(effectiveCards));
    }

    public Result<IReadOnlyList<string>> ResolveTags(CardInstanceState instance, CardZoneFlowContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ContentRevision))
            return Result<IReadOnlyList<string>>.Failure("Card-zone tag selection requires a pinned content revision");
        var runtime = _runtimes.Resolve(context.ContentRevision, context.ConfigName);
        if (runtime.IsFailure) return Result<IReadOnlyList<string>>.Failure(runtime.Error);
        var compiled = _compiler.Compile(instance.DefinitionId, runtime.Value);
        if (compiled.IsFailure) return Result<IReadOnlyList<string>>.Failure(compiled.Error);
        var effective = _effectiveCards.Resolve(compiled.Value, instance);
        return effective.IsFailure
            ? Result<IReadOnlyList<string>>.Failure(effective.Error)
            : Result<IReadOnlyList<string>>.Success(effective.Value.Tags);
    }
}

/// <summary>
/// Adapts the common formula pipeline to card-zone conditions and counts. A
/// nonzero finite result is true; counts must be exact nonnegative integers.
/// </summary>
public sealed class CardZoneRuntimeRuleEvaluator : ICardZoneRuleEvaluator
{
    private readonly IRuntimeFormulaEvaluator _formulas;
    private readonly ICardZoneCardMetadataResolver _cards;

    public CardZoneRuntimeRuleEvaluator(
        IRuntimeFormulaEvaluator formulas,
        ICardZoneCardMetadataResolver cards)
    {
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
    }

    public Result<bool> EvaluateCondition(string expression, CardZoneFlowContext context)
    {
        var evaluated = Evaluate(expression, context);
        if (evaluated.IsFailure) return Result<bool>.Failure(evaluated.Error);
        return float.IsFinite(evaluated.Value)
            ? Result<bool>.Success(evaluated.Value != 0f)
            : Result<bool>.Failure("Card-zone condition must evaluate to a finite number");
    }

    public Result<int> EvaluateCount(string expression, CardZoneFlowContext context)
    {
        var evaluated = Evaluate(expression, context);
        if (evaluated.IsFailure) return Result<int>.Failure(evaluated.Error);
        var value = evaluated.Value;
        return float.IsFinite(value) && value >= 0f && value < 2_147_483_648f && value == MathF.Truncate(value)
            ? Result<int>.Success((int)value)
            : Result<int>.Failure("Card-zone count must evaluate to a nonnegative exact integer");
    }

    public Result<bool> Matches(
        CardInstanceState instance,
        CardZoneSelectionDefinition selection,
        CardZoneFlowContext context)
    {
        if (selection.Strategy == CardZoneSelectionStrategy.ByTags)
        {
            var tags = _cards.ResolveTags(instance, context);
            return tags.IsFailure
                ? Result<bool>.Failure(tags.Error)
                : Result<bool>.Success(selection.RequiredTags.All(tag =>
                    tags.Value.Contains(tag, StringComparer.Ordinal)));
        }
        if (selection.Strategy == CardZoneSelectionStrategy.ByCondition &&
            !string.IsNullOrWhiteSpace(selection.Condition))
        {
            var variables = context.Variables.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            variables["cardUpgradeCount"] = instance.Upgrades.Count;
            return EvaluateCondition(selection.Condition, context with { Variables = variables });
        }
        return Result<bool>.Failure($"Unsupported card-zone predicate: {selection.Strategy}");
    }

    private Result<float> Evaluate(string expression, CardZoneFlowContext context)
    {
        var variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in context.Variables)
        {
            if (!double.IsFinite(pair.Value) || pair.Value < -float.MaxValue || pair.Value > float.MaxValue)
                return Result<float>.Failure($"Card-zone variable {pair.Key} must be a finite float");
            variables[pair.Key] = (float)pair.Value;
        }
        return _formulas is IRevisionedRuntimeFormulaEvaluator revisioned &&
               !string.IsNullOrWhiteSpace(context.ContentRevision)
            ? revisioned.EvaluateAtRevision(expression, context.ContentRevision, variables)
            : _formulas.Evaluate(expression, variables);
    }
}
