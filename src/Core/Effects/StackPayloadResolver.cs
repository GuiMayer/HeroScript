using System.Collections.Immutable;
using Core.Calculations;
using Core.Common;
using Core.Determinism;
using Core.Run.Content;

namespace Core.Effects;

public sealed record StackPayloadCapture(StackPayloadLot? Lot, ImmutableArray<CalculationResult> Calculations);
public sealed record StackPayloadEvaluation(CalculationQuantity Quantity, ImmutableArray<CalculationResult> Calculations);

/// <summary>Captures constitutive bases, not a world snapshot. All magnitudes use the common calculator.</summary>
public sealed class StackPayloadResolver(ICalculationResolver resolver, ICalculationEngine engine)
{
    public Result<StackPayloadCapture> Capture(EffectTriggerExecutionRequest request, EffectDefinition effect,
        ImmutableArray<StackPayloadParameterDefinition> definitions, StackPayloadReapplyPolicy policy,
        string targetId, int stacks, string impactId, IReadOnlyDictionary<string, float> variables)
    {
        var validation = StackPayloadPolicies.ValidateDefinitions(definitions, policy);
        if (validation.IsFailure) return Result<StackPayloadCapture>.Failure(validation.Error);
        if (effect.PayloadBindings.Any(binding => !definitions.Any(item => item.ParameterId == binding.ParameterId)))
            return Result<StackPayloadCapture>.Failure("Payload binding references an unknown parameter");
        if (definitions.IsEmpty) return Result<StackPayloadCapture>.Success(new(null, []));
        if (request.Combat.GetActor(request.SourceEntityId) is not { } source)
            return Result<StackPayloadCapture>.Failure("Payload application requires its actual source actor");
        var parameters = ImmutableSortedDictionary.CreateBuilder<string, StackPayloadParameterState>(StringComparer.Ordinal);
        var traces = ImmutableArray.CreateBuilder<CalculationResult>();
        foreach (var schema in definitions.OrderBy(item => item.ParameterId, StringComparer.Ordinal))
        {
            var binding = effect.PayloadBindings.SingleOrDefault(item => item.ParameterId == schema.ParameterId);
            var numeric = schema.Numeric;
            var origin = effect;
            string? componentId = request.Provenance.ComponentId;
            if (binding?.CardEffectComponentId is { } reference)
            {
                var component = request.Card?.Components.OfType<CardEffectComponentDefinition>()
                    .SingleOrDefault(item => item.ComponentId == reference);
                if (component == null) return Result<StackPayloadCapture>.Failure($"Missing effective card component: {reference}");
                origin = component.Effect;
                componentId = reference;
                var amount = origin.Parameters.SingleOrDefault(item => item.Parameter == EffectNumericParameter.Amount);
                if (amount?.InputQuantityId != null)
                    return Result<StackPayloadCapture>.Failure("A payload card base cannot reference a transported quantity");
                numeric = numeric with { FlatValue = amount?.FlatValue ?? origin.FlatValue,
                    FormulaValue = amount?.FormulaValue ?? origin.FormulaValue };
            }
            else if (binding != null) numeric = numeric with { FlatValue = binding.FlatValue, FormulaValue = binding.FormulaValue };
            var definition = schema with { Numeric = numeric };
            validation = StackPayloadPolicies.ValidateDefinitions([definition], policy);
            if (validation.IsFailure) return Result<StackPayloadCapture>.Failure(validation.Error);
            var tags = CalculationResolver.NormalizeTags(origin, request.Tags).ToImmutableHashSet(StringComparer.Ordinal);
            CalculationQuantity? snapshot = null;
            if (definition.Evaluation == StackParameterEvaluation.Snapshot)
            {
                var calculated = resolver.ResolveParameter(origin, numeric, $"{impactId}:payload:{schema.ParameterId}", new()
                {
                    ContentRevision = request.ContentRevision, Combat = request.Combat, Run = request.Run,
                    Actor = source, Target = request.Combat.GetActor(targetId), Card = request.Card,
                    ComponentId = componentId, Variables = variables, Tags = tags, CaptureOnly = true
                });
                if (calculated.IsFailure) return Result<StackPayloadCapture>.Failure(calculated.Error);
                if (calculated.Value.Calculation?.Quantity is not { } quantity || quantity.IncorporatedStages.IsEmpty)
                    return Result<StackPayloadCapture>.Failure("Payload capture requires a staged calculation profile");
                snapshot = quantity;
                traces.Add(calculated.Value.Calculation);
            }
            parameters.Add(schema.ParameterId, new() { Definition = definition, Snapshot = snapshot, Tags = tags });
        }
        var lot = new StackPayloadLot
        {
            LotId = CanonicalJson.ComputeHash(new { impactId, Parameters = parameters.ToImmutable() }),
            Stacks = stacks, ContentRevision = request.ContentRevision, SourceEntityId = request.SourceEntityId,
            OwnerEntityId = targetId, Origin = request.Provenance,
            CardInstanceId = request.Card?.CardInstanceId, CardDefinitionId = request.Card?.DefinitionId,
            CardFingerprint = request.Card?.Fingerprint,
            CardInfluences = request.Card?.Components.OfType<CardInfluenceComponentDefinition>().ToImmutableArray() ?? [],
            Variables = request.Variables.Where(pair => !Generated(pair.Key))
                .ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Parameters = parameters.ToImmutable()
        };
        return Result<StackPayloadCapture>.Success(new(lot, traces.ToImmutable()));
    }

    public Result<StackPayloadEvaluation> Evaluate(EffectTriggerExecutionRequest request, string targetId,
        string parameterId, string calculationId)
    {
        if (request.StackPayloadLots.Length is < 1 or > EffectExecutionLimits.MaximumSteps ||
            request.StackPayloadLots.Select(lot => lot.LotId).Distinct(StringComparer.Ordinal).Count() != request.StackPayloadLots.Length)
            return Result<StackPayloadEvaluation>.Failure("Invalid payload lot selection");
        var contributions = new List<WeightedCalculationQuantity>();
        var traces = ImmutableArray.CreateBuilder<CalculationResult>();
        foreach (var lot in request.StackPayloadLots.OrderBy(lot => lot.LotId, StringComparer.Ordinal))
        {
            if (lot.ContentRevision != request.ContentRevision || !lot.Parameters.TryGetValue(parameterId, out var parameter) ||
                !StackPayloadPolicies.ValidLot(lot, lot.Parameters.Values.Select(item => item.Definition).ToImmutableArray()))
                return Result<StackPayloadEvaluation>.Failure("Payload parameter, revision or lot is invalid");
            var quantity = parameter.Snapshot;
            if (parameter.Definition.Evaluation == StackParameterEvaluation.Dynamic)
            {
                var source = request.Combat.GetActor(lot.SourceEntityId);
                var owner = request.Combat.GetActor(lot.OwnerEntityId);
                if (source is not { IsAlive: true })
                {
                    switch (parameter.Definition.MissingSource)
                    {
                        case StackMissingSourcePolicy.SkipContribution: continue;
                        case StackMissingSourcePolicy.UseOwner when owner is { IsAlive: true }: source = owner; break;
                        default: return Result<StackPayloadEvaluation>.Failure($"Dynamic payload source is unavailable: {lot.SourceEntityId}");
                    }
                }
                if (owner == null) return Result<StackPayloadEvaluation>.Failure("Dynamic payload owner is unavailable");
                var card = lot.CardInstanceId is { } cardId ? new EffectiveCardDefinition
                {
                    CardInstanceId = cardId, DefinitionId = lot.CardDefinitionId!, Fingerprint = lot.CardFingerprint!,
                    Components = lot.CardInfluences
                } : null;
                var calculated = resolver.ResolveParameter(new() { Type = EffectType.APPLY_STATUS }, parameter.Definition.Numeric,
                    $"{calculationId}:lot:{lot.LotId}", new()
                    {
                        ContentRevision = lot.ContentRevision, Combat = request.Combat, Run = request.Run,
                        Actor = source, Target = request.Combat.GetActor(targetId), Card = card,
                        Variables = GameplayFormulaContext.Build(source!, request.Combat.GetActor(targetId), owner, request.Run, lot.Variables),
                        Tags = parameter.Tags, CaptureOnly = true
                    });
                if (calculated.IsFailure) return Result<StackPayloadEvaluation>.Failure(calculated.Error);
                if (calculated.Value.Calculation?.Quantity is not { } evaluated || evaluated.IncorporatedStages.IsEmpty)
                    return Result<StackPayloadEvaluation>.Failure("Dynamic payload requires a staged calculation profile");
                quantity = evaluated;
                traces.Add(calculated.Value.Calculation);
            }
            contributions.Add(new(lot.LotId, quantity!, lot.Stacks));
        }
        var aggregate = CalculationQuantityAggregation.Sum(engine, calculationId, contributions);
        if (aggregate.IsFailure) return Result<StackPayloadEvaluation>.Failure(aggregate.Error);
        traces.Add(aggregate.Value.Trace);
        return Result<StackPayloadEvaluation>.Success(new(aggregate.Value.Quantity, traces.ToImmutable()));
    }

    private static bool Generated(string key) => key.StartsWith("source.", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("owner.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("target.", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("run.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("results.", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("resources.", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("parent.", StringComparison.OrdinalIgnoreCase) || key is "stacks" or "duration" or "repeat_index" or "target_index";
}
