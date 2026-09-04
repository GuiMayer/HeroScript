using System.Text.Json;
using Core.Common;
using Core.Logging;
using Core.Run;

namespace Core.Effects.Handlers;

/// <summary>
/// Adaptador para comandos de economia/deck da run. O handler só traduz a
/// semântica do efeito; as transições determinísticas continuam no RunManager.
/// </summary>
public sealed class RunEffectHandler : IEffectHandler
{
    private static readonly IReadOnlySet<EffectType> Types = new HashSet<EffectType>
    {
        EffectType.MODIFY_RESOURCE,
        EffectType.DRAW_CARD,
        EffectType.DISCARD_CARD,
        EffectType.EXHAUST_CARD,
        EffectType.ADD_CARD_TO_HAND
    };

    private readonly IRunManager? _runs;
    private readonly EffectFormulaEvaluator _values;
    private readonly ILogger _logger;

    public RunEffectHandler(
        IRunManager? runs,
        EffectFormulaEvaluator values,
        ILogger logger)
    {
        _runs = runs;
        _values = values;
        _logger = logger;
    }

    public IReadOnlySet<EffectType> SupportedTypes => Types;
    public IReadOnlySet<EffectScope> SupportedScopes { get; } = new HashSet<EffectScope> { EffectScope.RUN };

    public EffectResult Execute(EffectExecutionRequest request) =>
        request.Effect.Definition.Type switch
        {
            EffectType.MODIFY_RESOURCE => Resource(request),
            EffectType.DRAW_CARD => Deck(request, "DRAW_CARD"),
            EffectType.DISCARD_CARD => Deck(request, "DISCARD_CARD"),
            EffectType.EXHAUST_CARD => Deck(request, "EXHAUST_CARD"),
            EffectType.ADD_CARD_TO_HAND => Deck(request, "ADD_CARD_TO_HAND"),
            _ => EffectResult.CreateFailure("Unsupported run effect")
        };

    private EffectResult Resource(EffectExecutionRequest request)
    {
        var definition = request.Effect.Definition;
        if (string.IsNullOrWhiteSpace(definition.TargetResource))
            return EffectResult.CreateFailure("MODIFY_RESOURCE effect requires targetResource");
        var value = _values.Calculate(request.Effect, request.TargetId, request.Context);
        var applied = ApplyRunResource(
            request.Context,
            definition.TargetResource,
            value,
            definition.Operation,
            definition.ResourceField);
        if (applied.IsFailure)
            return EffectResult.CreateFailure(applied.Error);

        _logger.LogDebug(
            $"Executing run resource effect: {definition.Operation} {value} {definition.TargetResource}");
        return EffectResult.CreateSuccess() with
        {
            ValueApplied = value,
            ResourceAffected = definition.TargetResource,
            AffectedEntityIds = new List<string> { request.TargetId },
            Metadata = RunMetadata(
                applied.Value,
                new Dictionary<string, object>
                {
                    ["resourceId"] = definition.TargetResource,
                    ["operation"] = definition.Operation.ToString(),
                    ["field"] = definition.ResourceField.ToString()
                })
        };
    }

    private EffectResult Deck(EffectExecutionRequest request, string operation)
    {
        var count = (int)(request.Effect.Definition.FlatValue ?? 1f);
        var applied = ApplyDeck(request.Context, request.Effect, operation, count);
        if (applied.IsFailure)
            return EffectResult.CreateFailure(applied.Error);

        _logger.LogDebug($"Executing deck effect: {operation} x{count} for {request.TargetId}");
        return EffectResult.CreateSuccess() with
        {
            ValueApplied = count,
            AffectedEntityIds = new List<string> { request.TargetId },
            Metadata = RunMetadata(applied.Value.RunState, new Dictionary<string, object>
            {
                ["deckOperation"] = operation,
                ["count"] = count,
                ["cards"] = applied.Value.Cards
            })
        };
    }

    private Result<RunState?> ApplyRunResource(
        IEffectContext context,
        string resourceId,
        float value,
        ResourceEffectOperation operation,
        Core.Resources.ResourceValueField field)
    {
        var runContext = context as RunEffectContext;
        if (runContext?.RunState == null)
            return Result<RunState?>.Success(null);
        if (_runs == null)
            return Result<RunState?>.Failure("Run resource effects require IRunManager");

        return _runs.ApplyRunResource(runContext.RunState.RunId, resourceId, value, operation, field)
            .Map<RunState?>(state => state);
    }

    private Result<(RunState? RunState, IReadOnlyList<string> Cards)> ApplyDeck(
        IEffectContext context,
        EffectInstance effect,
        string operation,
        int count)
    {
        var runContext = context as RunEffectContext;
        if (runContext?.RunState == null)
        {
            return Result<(RunState?, IReadOnlyList<string>)>.Success(
                (null, Array.Empty<string>()));
        }
        if (_runs == null)
        {
            return Result<(RunState?, IReadOnlyList<string>)>.Failure(
                "Run deck effects require IRunManager");
        }

        Result<IReadOnlyList<string>> operationResult = operation switch
        {
            "DRAW_CARD" => _runs.DrawCards(runContext.RunState.RunId, count),
            "DISCARD_CARD" => _runs.DiscardCards(
                runContext.RunState.RunId,
                SelectCards(effect, runContext.RunState.Deck.HandInstanceIds, count)),
            "EXHAUST_CARD" => _runs.ExhaustCards(
                runContext.RunState.RunId,
                SelectCards(effect, runContext.RunState.Deck.HandInstanceIds, count)),
            "ADD_CARD_TO_HAND" => _runs.AddCardsToHand(
                runContext.RunState.RunId,
                ResolveCardIds(effect, count)),
            _ => Result<IReadOnlyList<string>>.Failure($"Unsupported deck operation: {operation}")
        };
        if (operationResult.IsFailure)
            return Result<(RunState?, IReadOnlyList<string>)>.Failure(operationResult.Error);

        var updated = _runs.GetRun(runContext.RunState.RunId);
        return updated.IsSuccess
            ? Result<(RunState?, IReadOnlyList<string>)>.Success((updated.Value, operationResult.Value))
            : Result<(RunState?, IReadOnlyList<string>)>.Failure(updated.Error);
    }

    private static IReadOnlyList<string> SelectCards(
        EffectInstance effect,
        IReadOnlyList<Guid> source,
        int count)
    {
        var explicitCards = ResolveCardIds(effect, count);
        return explicitCards.Count > 0
            ? explicitCards
            : source.Take(count).Select(id => id.ToString()).ToList();
    }

    private static IReadOnlyList<string> ResolveCardIds(EffectInstance effect, int count)
    {
        if (effect.Definition.Metadata.TryGetValue("cardIds", out var cardIds))
        {
            if (cardIds is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                return element.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .ToList();
            }
            if (cardIds is IEnumerable<string> strings)
                return strings.ToList();
        }

        if (effect.Definition.Metadata.TryGetValue("cardId", out var cardId))
        {
            if (cardId is JsonElement element && element.ValueKind == JsonValueKind.String)
                return Enumerable.Repeat(element.GetString()!, count).ToList();
            if (cardId is string value && !string.IsNullOrWhiteSpace(value))
                return Enumerable.Repeat(value, count).ToList();
        }

        return Array.Empty<string>();
    }

    private static Dictionary<string, object> RunMetadata(
        RunState? state,
        Dictionary<string, object>? metadata = null)
    {
        var result = metadata != null
            ? new Dictionary<string, object>(metadata)
            : new Dictionary<string, object>();
        result["stateApplied"] = state != null;
        if (state != null)
        {
            result["runId"] = state.RunId;
            result["resources"] = state.ResourceState.Resources.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Current,
                StringComparer.OrdinalIgnoreCase);
            result["handCount"] = state.Deck.Hand.Count;
            result["drawPileCount"] = state.Deck.DrawPile.Count;
            result["discardPileCount"] = state.Deck.DiscardPile.Count;
            result["exhaustPileCount"] = state.Deck.ExhaustPile.Count;
        }

        return result;
    }
}
