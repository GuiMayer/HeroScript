using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Combat.Flow;
using Core.Combat.TurnPhase;
using Core.Run;
using Core.StatusEffects;

namespace Core.Content;

public sealed record ContentGraphValidationResult
{
    public bool IsValid => Errors.IsEmpty;
    public ImmutableArray<string> Errors { get; init; } = [];
    public ImmutableArray<string> Warnings { get; init; } = [];
}

public interface IContentGraphValidator
{
    ContentGraphValidationResult Validate(ContentBundle bundle);
}

/// <summary>
/// Validates cross-module references over the exact immutable bundle that will
/// be published. Rules cover both executable modules and reserved content
/// modules so future runtimes inherit the same publication safety boundary.
/// </summary>
public sealed class ContentGraphValidator : IContentGraphValidator
{
    public ContentGraphValidationResult Validate(ContentBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var errors = ImmutableArray.CreateBuilder<string>();
        var warnings = ImmutableArray.CreateBuilder<string>();
        var created = ContentRuntime.Create(bundle);
        if (created.IsFailure)
        {
            errors.Add(created.Error);
            return new ContentGraphValidationResult { Errors = errors.ToImmutable() };
        }

        var runtime = created.Value;
        ValidateModes(runtime, errors);
        ValidateCombatRules(runtime, errors, warnings);
        ValidatePhaseSequences(runtime, errors);
        ValidateRuns(runtime, errors);
        ValidateCards(runtime, errors);
        ValidateActions(runtime, errors);
        ValidateStatusEffects(runtime, errors);
        ValidateEnemyPools(runtime, errors);
        ValidateCardPools(runtime, errors);
        ValidateCardUpgrades(runtime, errors);
        ValidateShops(runtime, errors);
        ValidatePreparations(runtime, errors);
        ValidateDailyChallenges(runtime, errors);
        ValidatePipelines(runtime, errors);

        var reservedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "boards", "zones", "keywords", "reaction-rules", "run-events"
        };
        foreach (var kind in bundle.Manifest.Artifacts
                     .Select(artifact => artifact.Kind)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(reservedKinds.Contains)
                     .OrderBy(kind => kind, StringComparer.Ordinal))
        {
            warnings.Add(
                $"Content kind '{kind}' is structurally valid and revisioned, but has no executable runtime yet");
        }

        return new ContentGraphValidationResult
        {
            Errors = errors.Distinct(StringComparer.Ordinal).OrderBy(error => error, StringComparer.Ordinal).ToImmutableArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).OrderBy(warning => warning, StringComparer.Ordinal).ToImmutableArray()
        };
    }

    private static void ValidateModes(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("modes"))
        {
            RequireProperty(runtime, errors, "modes", id, definition, "runDefinitionId", "runs");
            RequireProperty(runtime, errors, "modes", id, definition, "flowRulesId", "flow-rules");
            RequireProperty(runtime, errors, "modes", id, definition, "combatRulesId", "combat-rules");
            RequireProperty(runtime, errors, "modes", id, definition, "damagePipelineId", "pipelines");
            RequireProperty(runtime, errors, "modes", id, definition, "replayPolicyId", "replay-policies");
            RequireProperty(runtime, errors, "modes", id, definition, "timelinePolicyId", "timeline-policies");
            RequireProperty(runtime, errors, "modes", id, definition, "contentBindingPolicyId", "content-binding-policies");
            RequireProperty(runtime, errors, "modes", id, definition, "capabilityPolicyId", "capability-policies");
            RequireArray(runtime, errors, "modes", id, definition, "cardPoolIds", "card-pools");
            RequireArray(runtime, errors, "modes", id, definition, "enemyPoolIds", "enemy-pools");
        }
    }

    private static void ValidateRuns(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("runs"))
        {
            RequireArray(runtime, errors, "runs", id, definition, "startingDeck", "cards");
            ValidateMap(id, definition, errors);
        }
    }

    private static void ValidatePhaseSequences(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        foreach (var (id, definition) in runtime.GetDefinitions("phase-sequences"))
        {
            try
            {
                var sequence = definition.Deserialize<PhaseSequenceDefinition>(options);
                if (sequence == null)
                {
                    errors.Add($"phase-sequences/{id} is invalid");
                    continue;
                }
                var validation = PhaseSequenceLoader.ValidateSequence(sequence);
                if (validation.IsFailure)
                    errors.Add($"phase-sequences/{id}: {validation.Error}");
            }
            catch (Exception exception)
            {
                errors.Add($"phase-sequences/{id} could not be parsed: {exception.Message}");
            }
        }
    }

    private static void ValidateCombatRules(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors,
        ImmutableArray<string>.Builder warnings)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        foreach (var (id, definition) in runtime.GetDefinitions("combat-rules"))
        {
            RequireProperty(
                runtime,
                errors,
                "combat-rules",
                id,
                definition,
                "defaultPhaseSequenceId",
                "phase-sequences");

            try
            {
                var combat = definition.Deserialize<CombatRulesDefinition>(options);
                if (combat == null)
                {
                    errors.Add($"combat-rules/{id} is invalid");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(combat.DefaultPhaseSequenceId))
                {
                    var selectedSequence = runtime.GetDefinition<PhaseSequenceDefinition>(
                        "phase-sequences",
                        combat.DefaultPhaseSequenceId);
                    if (selectedSequence.IsSuccess)
                    {
                        var operationalSequence = PhaseSequenceLoader.ValidateCanonicalActivationSequence(
                            selectedSequence.Value);
                        if (operationalSequence.IsFailure)
                        {
                            errors.Add(
                                $"combat-rules/{id} selects unsupported phase sequence " +
                                $"'{combat.DefaultPhaseSequenceId}': {operationalSequence.Error}");
                        }
                    }
                }

                if (combat.Flow.Reactions.Strategy == ReactionStrategy.Unspecified)
                {
                    errors.Add($"combat-rules/{id} requires an explicit reaction strategy");
                    continue;
                }

                if (combat.Flow.Reactions.Strategy != ReactionStrategy.Disabled)
                {
                    warnings.Add(
                        $"combat-rules/{id} selects reaction strategy " +
                        $"'{combat.Flow.Reactions.Strategy}', which is reserved but not implemented");
                }
                if (combat.Flow.EncounterResolution.Strategy != EncounterResolutionStrategy.ManualAck)
                {
                    warnings.Add(
                        $"combat-rules/{id} selects encounter resolution strategy " +
                        $"'{combat.Flow.EncounterResolution.Strategy}', which is reserved but not implemented");
                }
                if (combat.Flow.Outcome.EvaluationBoundary != OutcomeEvaluationBoundary.AfterCurrentAction)
                {
                    warnings.Add(
                        $"combat-rules/{id} selects outcome evaluation boundary " +
                        $"'{combat.Flow.Outcome.EvaluationBoundary}', which is reserved but not implemented");
                }

                var implementedSubset = combat.Flow with
                {
                    Reactions = new ReactionPolicyDefinition { Strategy = ReactionStrategy.Disabled },
                    EncounterResolution = new EncounterResolutionPolicyDefinition
                    {
                        Strategy = EncounterResolutionStrategy.ManualAck
                    },
                    Outcome = combat.Flow.Outcome with
                    {
                        EvaluationBoundary = OutcomeEvaluationBoundary.AfterCurrentAction
                    }
                };
                var validation = CombatFlowPolicyValidator.Validate(implementedSubset);
                if (validation.IsFailure)
                    errors.Add($"combat-rules/{id}: {validation.Error}");
            }
            catch (Exception exception)
            {
                errors.Add($"combat-rules/{id} could not be parsed: {exception.Message}");
            }
        }
    }

    private static void ValidateCards(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("cards"))
            RequireProperty(runtime, errors, "cards", id, definition, "actionId", "actions");
    }

    private static void ValidateActions(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("actions"))
        {
            foreach (var value in FindStringProperties(definition, "resourceId", "targetResource"))
                Require(runtime, errors, "actions", id, value, "resources");
            foreach (var value in FindStringProperties(definition, "statusId"))
                Require(runtime, errors, "actions", id, value, "status-effects");
        }
    }

    private static void ValidateStatusEffects(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        foreach (var (id, definition) in runtime.GetDefinitions("status-effects"))
        {
            try
            {
                var status = definition.Deserialize<StatusEffectDefinition>(options);
                if (status == null)
                {
                    errors.Add($"status-effects/{id} is invalid");
                    continue;
                }
                if (status.DefaultDuration == 0 || status.DefaultDuration < -1)
                    errors.Add($"status-effects/{id} has invalid defaultDuration");
                if (status.DefaultDuration > 0 &&
                    status.DurationTickBoundary == StatusTriggerBoundary.Unspecified)
                {
                    errors.Add(
                        $"status-effects/{id} requires durationTickBoundary for finite duration");
                }
                if (status.Timing is StatusEffectTiming.START_OF_TURN or StatusEffectTiming.END_OF_TURN &&
                    status.TriggerBoundary == StatusTriggerBoundary.Unspecified)
                {
                    errors.Add(
                        $"status-effects/{id} requires triggerBoundary for turn timing");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"status-effects/{id} could not be parsed: {exception.Message}");
            }
        }
    }

    private static void ValidateEnemyPools(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("enemy-pools"))
            RequireArray(runtime, errors, "enemy-pools", id, definition, "entityDefinitionIds", "entities");
    }

    private static void ValidateCardPools(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("card-pools"))
            RequireArray(runtime, errors, "card-pools", id, definition, "explicitCardIds", "cards");
    }

    private static void ValidateCardUpgrades(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("card-upgrades"))
            RequireArray(runtime, errors, "card-upgrades", id, definition, "cardDefinitionIds", "cards");
    }

    private static void ValidateShops(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("shops"))
        {
            RequireProperty(runtime, errors, "shops", id, definition, "cardPoolId", "card-pools");
            foreach (var cardId in FindStringProperties(definition, "cardId"))
                Require(runtime, errors, "shops", id, cardId, "cards");
        }
    }

    private static void ValidatePreparations(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("preparations"))
        {
            foreach (var cardId in FindStringArrayItems(definition, "addCardsToDiscard"))
                Require(runtime, errors, "preparations", id, cardId, "cards");
            foreach (var modifierId in FindStringProperties(definition, "modifierId"))
                Require(runtime, errors, "preparations", id, modifierId, "modifiers");
        }
    }

    private static void ValidateDailyChallenges(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("daily-challenges"))
        {
            RequireProperty(runtime, errors, "daily-challenges", id, definition, "runDefinitionId", "runs");
            RequireProperty(runtime, errors, "daily-challenges", id, definition, "modeId", "modes");
        }
    }

    private static void ValidatePipelines(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("pipelines"))
        {
            foreach (var source in FindStringProperties(definition, "source")
                         .Where(value => value.StartsWith("formula:", StringComparison.OrdinalIgnoreCase)))
            {
                Require(runtime, errors, "pipelines", id, source["formula:".Length..], "formulas");
            }
        }
    }

    private static void ValidateMap(
        string runId,
        JsonElement definition,
        ImmutableArray<string>.Builder errors)
    {
        if (!TryGetProperty(definition, "mapNodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            return;
        var ids = nodes.EnumerateArray()
            .Select(node => TryGetString(node, "nodeId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
        var known = ids.ToHashSet(StringComparer.Ordinal);
        if (known.Count != ids.Length)
            errors.Add($"runs/{runId} contains duplicate map node ids");
        foreach (var node in nodes.EnumerateArray())
        {
            var nodeId = TryGetString(node, "nodeId") ?? "<unknown>";
            foreach (var next in FindStringArrayItems(node, "nextNodeIds").Where(next => !known.Contains(next)))
                errors.Add($"runs/{runId} map node '{nodeId}' references missing next node '{next}'");
        }
    }

    private static void RequireProperty(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors,
        string sourceKind,
        string sourceId,
        JsonElement definition,
        string propertyName,
        string targetKind)
    {
        var targetId = TryGetString(definition, propertyName);
        if (!string.IsNullOrWhiteSpace(targetId))
            Require(runtime, errors, sourceKind, sourceId, targetId, targetKind);
    }

    private static void RequireArray(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors,
        string sourceKind,
        string sourceId,
        JsonElement definition,
        string propertyName,
        string targetKind)
    {
        foreach (var targetId in FindStringArrayItems(definition, propertyName))
            Require(runtime, errors, sourceKind, sourceId, targetId, targetKind);
    }

    private static void Require(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors,
        string sourceKind,
        string sourceId,
        string targetId,
        string targetKind)
    {
        if (!runtime.GetDefinitions(targetKind).ContainsKey(targetId))
            errors.Add($"{sourceKind}/{sourceId} references missing {targetKind}/{targetId}");
    }

    private static IEnumerable<string> FindStringProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    yield return property.Value.GetString()!;
                foreach (var nested in FindStringProperties(property.Value, names))
                    yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var nested in FindStringProperties(item, names))
                yield return nested;
        }
    }

    private static IEnumerable<string> FindStringArrayItems(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                            yield return item.GetString()!;
                    }
                }
                foreach (var nested in FindStringArrayItems(property.Value, name))
                    yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var nested in FindStringArrayItems(item, name))
                yield return nested;
        }
    }

    private static string? TryGetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}
