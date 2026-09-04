using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Calculations;
using Core.Effects;
using Core.Entity.Definitions;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
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
        ValidateResources(runtime, errors);
        ValidateEntities(runtime, errors);
        ValidateCardComponentBundles(runtime, errors);
        ValidateCards(runtime, errors);
        ValidateActions(runtime, errors);
        ValidateStatusEffects(runtime, errors);
        ValidateRelics(runtime, errors);
        ValidateEnemyPools(runtime, errors);
        ValidateCardPools(runtime, errors);
        ValidateCardUpgrades(runtime, errors);
        ValidateCardSelections(runtime, errors);
        ValidateShops(runtime, errors);
        ValidatePreparations(runtime, errors);
        ValidateDailyChallenges(runtime, errors);
        ValidatePipelines(runtime, errors);
        ValidateCalculationPipelines(runtime, errors);

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
            RequireArray(runtime, errors, "modes", id, definition, "calculationPipelineIds", "calculation-pipelines");
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
            if (!TryGetProperty(definition, "startingResources", out var resources) ||
                resources.ValueKind != JsonValueKind.Object ||
                !resources.EnumerateObject().Any())
            {
                errors.Add($"runs/{id} requires at least one starting resource");
            }
            else
            {
                foreach (var resource in resources.EnumerateObject())
                {
                    Require(runtime, errors, "runs", id, resource.Name, "resources");
                    if (resource.Value.ValueKind != JsonValueKind.Number ||
                        !resource.Value.TryGetSingle(out var amount) ||
                        !IsFinite(amount))
                    {
                        errors.Add($"runs/{id} resource {resource.Name} requires a finite numeric amount");
                    }
                }
            }
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
                Require(
                    runtime,
                    errors,
                    "combat-rules",
                    id,
                    combat.Flow.ResourceCycle.ResourceId,
                    "resources");
                if (!string.IsNullOrWhiteSpace(combat.Flow.ActionBudget.ResourceId))
                {
                    Require(
                        runtime,
                        errors,
                        "combat-rules",
                        id,
                        combat.Flow.ActionBudget.ResourceId,
                        "resources");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"combat-rules/{id} could not be parsed: {exception.Message}");
            }
        }
    }

    private static void ValidateCards(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        var compiler = new CardContentCompiler();
        foreach (var (id, definition) in runtime.GetDefinitions("cards"))
        {
            var card = definition.Deserialize<CardContentDefinition>(CreateJsonOptions());
            if (card == null)
            {
                errors.Add($"cards/{id} is invalid");
                continue;
            }
            if (card.Components.Count == 0 && card.ComponentBundleIds.Count == 0)
            {
                RequireProperty(runtime, errors, "cards", id, definition, "actionId", "actions");
                continue;
            }

            var compiled = compiler.Compile(id, runtime);
            if (compiled.IsFailure)
                errors.Add($"cards/{id}: {compiled.Error}");
            foreach (var value in FindStringProperties(definition, "resourceId", "targetResource"))
                Require(runtime, errors, "cards", id, value, "resources");
            foreach (var value in FindStringProperties(definition, "statusId"))
                Require(runtime, errors, "cards", id, value, "status-effects");
        }
    }

    private static void ValidateCardComponentBundles(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("card-component-bundles"))
        {
            var bundle = definition.Deserialize<CardComponentBundleDefinition>(CreateJsonOptions());
            if (bundle == null)
            {
                errors.Add($"card-component-bundles/{id} is invalid");
                continue;
            }
            foreach (var value in FindStringProperties(definition, "resourceId", "targetResource"))
                Require(runtime, errors, "card-component-bundles", id, value, "resources");
            foreach (var value in FindStringProperties(definition, "statusId"))
                Require(runtime, errors, "card-component-bundles", id, value, "status-effects");
        }
    }

    private static void ValidateActions(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("actions"))
        {
            foreach (var value in FindStringProperties(definition, "resourceId", "targetResource"))
                Require(runtime, errors, "actions", id, value, "resources");
            foreach (var value in FindStringProperties(definition, "statusId"))
                Require(runtime, errors, "actions", id, value, "status-effects");

            try
            {
                var action = definition.Deserialize<ActionDefinition>(CreateJsonOptions());
                if (action == null)
                {
                    errors.Add($"actions/{id} is invalid");
                    continue;
                }
                foreach (var effect in EnumerateEffects(action.Effects))
                {
                    if (effect.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE &&
                        string.IsNullOrWhiteSpace(effect.TargetResource))
                    {
                        errors.Add(
                            $"actions/{id} effect {effect.EffectId} ({effect.Type}) requires targetResource");
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Add($"actions/{id} could not be parsed: {exception.Message}");
            }
        }
    }

    private static void ValidateResources(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("resources"))
        {
            try
            {
                var resource = definition.Deserialize<ResourceDefinition>(CreateJsonOptions());
                if (resource == null)
                {
                    errors.Add($"resources/{id} is invalid");
                    continue;
                }

                var validation = ResourceDefinitionValidator.Validate(resource, id);
                if (validation.IsFailure)
                    errors.Add($"resources/{id}: {validation.Error}");
            }
            catch (Exception exception)
            {
                errors.Add($"resources/{id} could not be parsed: {exception.Message}");
            }
        }
    }

    private static void ValidateEntities(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("entities"))
        {
            try
            {
                var entity = definition.Deserialize<EntityDefinition>(CreateJsonOptions());
                if (entity == null)
                {
                    errors.Add($"entities/{id} is invalid");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(entity.DefinitionId))
                    errors.Add($"entities/{id} requires definitionId");
                else if (!string.Equals(entity.DefinitionId, id, StringComparison.Ordinal))
                    errors.Add($"entities/{id} definitionId does not match its content key");
                if (!string.IsNullOrWhiteSpace(entity.BaseDefinitionId))
                    Require(runtime, errors, "entities", id, entity.BaseDefinitionId, "entities");
                foreach (var (resourceId, pool) in entity.Resources?.Resources ??
                         new Dictionary<string, ResourcePoolDefinition>())
                {
                    Require(runtime, errors, "entities", id, resourceId, "resources");
                    if (!IsFinite(pool.Current) || !IsFinite(pool.Max) || pool.Max < 0)
                        errors.Add($"entities/{id} resource {resourceId} has invalid current/max values");
                }
                foreach (var actionId in entity.AI?.Actions ?? [])
                    Require(runtime, errors, "entities", id, actionId, "actions");
            }
            catch (Exception exception)
            {
                errors.Add($"entities/{id} could not be parsed: {exception.Message}");
            }
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
                var duplicateTrigger = status.Triggers
                    .Where(trigger => !string.IsNullOrWhiteSpace(trigger.TriggerId))
                    .GroupBy(trigger => trigger.TriggerId, StringComparer.Ordinal)
                    .FirstOrDefault(group => group.Count() > 1);
                if (duplicateTrigger != null)
                    errors.Add($"status-effects/{id} has duplicate trigger {duplicateTrigger.Key}");
                foreach (var trigger in status.Triggers)
                {
                    if (string.IsNullOrWhiteSpace(trigger.TriggerId))
                        errors.Add($"status-effects/{id} has trigger without triggerId");
                    if (!Enum.TryParse<StatusTriggerBoundary>(trigger.Boundary, false, out var boundary) ||
                        boundary == StatusTriggerBoundary.Unspecified)
                        errors.Add($"status-effects/{id} trigger {trigger.TriggerId} has invalid boundary");
                    if (trigger.Effects.Count == 0)
                        errors.Add($"status-effects/{id} trigger {trigger.TriggerId} has no effects");
                    foreach (var effect in EnumerateEffects(trigger.Effects))
                    {
                        if (effect.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE)
                        {
                            if (string.IsNullOrWhiteSpace(effect.TargetResource))
                                errors.Add($"status-effects/{id} effect {effect.EffectId} requires targetResource");
                            else
                                Require(runtime, errors, "status-effects", id, effect.TargetResource, "resources");
                        }
                        if (effect.Type is EffectType.APPLY_STATUS or EffectType.REMOVE_STATUS &&
                            !string.IsNullOrWhiteSpace(effect.StatusId))
                            Require(runtime, errors, "status-effects", id, effect.StatusId, "status-effects");
                    }
                }
                foreach (var influence in status.Influences)
                {
                    if (string.IsNullOrWhiteSpace(influence.InfluenceId) ||
                        string.IsNullOrWhiteSpace(influence.Channel) ||
                        string.IsNullOrWhiteSpace(influence.Bucket) ||
                        influence.Value.HasValue == !string.IsNullOrWhiteSpace(influence.Formula))
                        errors.Add($"status-effects/{id} has invalid influence {influence.InfluenceId}");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"status-effects/{id} could not be parsed: {exception.Message}");
            }
        }
    }

    private static IEnumerable<EffectDefinition> EnumerateEffects(
        IEnumerable<EffectDefinition> effects)
    {
        foreach (var effect in effects)
        {
            yield return effect;
            foreach (var nested in EnumerateEffects(effect.ChainedEffects ?? []))
                yield return nested;
            foreach (var nested in EnumerateEffects(effect.ConditionalEffects ?? []))
                yield return nested;
        }
    }

    private static void ValidateRelics(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, element) in runtime.GetDefinitions("relics"))
        {
            try
            {
                var relic = element.Deserialize<RelicDefinition>(CreateJsonOptions());
                if (relic == null)
                {
                    errors.Add($"relics/{id} is invalid");
                    continue;
                }
                if (relic.StackLimit < 1)
                    errors.Add($"relics/{id} has invalid stackLimit");
                foreach (var trigger in relic.Triggers)
                {
                    if (string.IsNullOrWhiteSpace(trigger.TriggerId) ||
                        string.IsNullOrWhiteSpace(trigger.Boundary) ||
                        trigger.Effects.Count == 0)
                    {
                        errors.Add($"relics/{id} has invalid trigger {trigger.TriggerId}");
                        continue;
                    }
                    foreach (var effect in EnumerateEffects(trigger.Effects))
                    {
                        if (effect.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE)
                        {
                            if (string.IsNullOrWhiteSpace(effect.TargetResource))
                                errors.Add($"relics/{id} effect {effect.EffectId} requires targetResource");
                            else
                                Require(runtime, errors, "relics", id, effect.TargetResource, "resources");
                        }
                        if (effect.Type is EffectType.APPLY_STATUS or EffectType.REMOVE_STATUS &&
                            !string.IsNullOrWhiteSpace(effect.StatusId))
                            Require(runtime, errors, "relics", id, effect.StatusId, "status-effects");
                    }
                }
                foreach (var influence in relic.Influences)
                {
                    if (string.IsNullOrWhiteSpace(influence.InfluenceId) ||
                        string.IsNullOrWhiteSpace(influence.Channel) ||
                        string.IsNullOrWhiteSpace(influence.Bucket) ||
                        influence.Value.HasValue == !string.IsNullOrWhiteSpace(influence.Formula))
                        errors.Add($"relics/{id} has invalid influence {influence.InfluenceId}");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"relics/{id} could not be parsed: {exception.Message}");
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
        var compiler = new CardContentCompiler();
        var resolver = new EffectiveCardResolver();
        foreach (var (id, definition) in runtime.GetDefinitions("card-upgrades"))
        {
            RequireArray(runtime, errors, "card-upgrades", id, definition, "cardDefinitionIds", "cards");
            try
            {
                var upgrade = definition.Deserialize<CardUpgradeDefinition>(CreateJsonOptions());
                if (upgrade == null)
                {
                    errors.Add($"card-upgrades/{id} is invalid");
                    continue;
                }
                upgrade = upgrade with
                {
                    UpgradeId = string.IsNullOrWhiteSpace(upgrade.UpgradeId) ? id : upgrade.UpgradeId
                };
                var cardIds = upgrade.CardDefinitionIds.Count == 0
                    ? runtime.GetDefinitions("cards").Keys.OrderBy(value => value, StringComparer.Ordinal)
                    : upgrade.CardDefinitionIds.OrderBy(value => value, StringComparer.Ordinal);
                foreach (var cardId in cardIds)
                {
                    var compiled = compiler.Compile(cardId, runtime);
                    if (compiled.IsFailure)
                    {
                        errors.Add($"card-upgrades/{id}: {compiled.Error}");
                        continue;
                    }
                    var validation = resolver.ValidateUpgrade(compiled.Value, upgrade);
                    if (validation.IsFailure)
                        errors.Add($"card-upgrades/{id}: {validation.Error}");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"card-upgrades/{id} could not be parsed: {exception.Message}");
            }
        }
    }

    private static void ValidateShops(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("shops"))
        {
            RequireProperty(runtime, errors, "shops", id, definition, "cardPoolId", "card-pools");
            foreach (var cardId in FindStringProperties(definition, "cardId"))
                Require(runtime, errors, "shops", id, cardId, "cards");
            foreach (var resourceId in FindStringProperties(definition, "resourceId"))
                Require(runtime, errors, "shops", id, resourceId, "resources");
            var shop = definition.Deserialize<ShopDefinition>(CreateJsonOptions());
            if (shop == null)
            {
                errors.Add($"shops/{id} is invalid");
                continue;
            }
            ValidateResourceAmounts($"shops/{id} reroll baseCosts", shop.Reroll.BaseCosts, errors);
            ValidateResourceAmounts($"shops/{id} reroll costsPerReroll", shop.Reroll.CostsPerReroll, errors);
            foreach (var item in shop.Items)
                ValidateResourceAmounts($"shops/{id} item {item.ItemId} costs", item.Costs, errors);
        }
    }

    private static void ValidateCardSelections(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("card-selections"))
        {
            RequireProperty(runtime, errors, "card-selections", id, definition, "cardPoolId", "card-pools");
            foreach (var resourceId in FindStringProperties(definition, "resourceId"))
                Require(runtime, errors, "card-selections", id, resourceId, "resources");
            var selection = definition.Deserialize<CardSelectionDefinition>(CreateJsonOptions());
            if (selection == null)
            {
                errors.Add($"card-selections/{id} is invalid");
                continue;
            }
            ValidateResourceAmounts(
                $"card-selections/{id} reroll baseCosts",
                selection.Reroll.BaseCosts,
                errors);
            ValidateResourceAmounts(
                $"card-selections/{id} reroll costsPerReroll",
                selection.Reroll.CostsPerReroll,
                errors);
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
            foreach (var resourceId in FindStringProperties(definition, "resourceId"))
                Require(runtime, errors, "preparations", id, resourceId, "resources");
            var preparation = definition.Deserialize<PreparationDefinition>(CreateJsonOptions());
            if (preparation == null)
            {
                errors.Add($"preparations/{id} is invalid");
                continue;
            }
            foreach (var option in preparation.Options)
                ValidateResourceAmounts($"preparations/{id} option {option.OptionId} costs", option.Costs, errors);
        }
    }

    private static void ValidateResourceAmounts(
        string source,
        IReadOnlyList<ResourceAmount> amounts,
        ImmutableArray<string>.Builder errors)
    {
        var duplicate = amounts
            .Where(amount => !string.IsNullOrWhiteSpace(amount.ResourceId))
            .GroupBy(amount => amount.ResourceId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            errors.Add($"{source} contains duplicate resource {duplicate.Key}");
        foreach (var amount in amounts)
        {
            if (string.IsNullOrWhiteSpace(amount.ResourceId) ||
                !IsFinite(amount.Amount) ||
                amount.Amount < 0)
            {
                errors.Add($"{source} contains an invalid resource amount");
            }
        }
    }

    private static void ValidateDailyChallenges(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        foreach (var (id, definition) in runtime.GetDefinitions("daily-challenges"))
        {
            RequireProperty(runtime, errors, "daily-challenges", id, definition, "runDefinitionId", "runs");
            RequireProperty(runtime, errors, "daily-challenges", id, definition, "modeId", "modes");
            var challenge = definition.Deserialize<DailyChallengeDefinition>(CreateJsonOptions());
            if (challenge == null)
            {
                errors.Add($"daily-challenges/{id} is invalid");
                continue;
            }
            foreach (var weight in challenge.ScoreResourceWeights)
            {
                Require(runtime, errors, "daily-challenges", id, weight.Key, "resources");
                if (!IsFinite(weight.Value))
                    errors.Add($"daily-challenges/{id} score weight for {weight.Key} must be finite");
            }
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

    private static void ValidateCalculationPipelines(
        ContentRuntime runtime,
        ImmutableArray<string>.Builder errors)
    {
        var engine = new CalculationEngine();
        foreach (var (id, definition) in runtime.GetDefinitions("calculation-pipelines"))
        {
            try
            {
                var pipeline = definition.Deserialize<CalculationPipelineDefinition>(CreateJsonOptions());
                if (pipeline == null)
                {
                    errors.Add($"calculation-pipelines/{id} is invalid");
                    continue;
                }
                pipeline = pipeline with
                {
                    PipelineId = string.IsNullOrWhiteSpace(pipeline.PipelineId) ? id : pipeline.PipelineId
                };
                var validation = engine.Calculate(new CalculationRequest
                {
                    CalculationId = $"validation:{id}",
                    Channel = pipeline.Channel,
                    BaseValue = 0
                }, pipeline);
                if (validation.IsFailure)
                    errors.Add($"calculation-pipelines/{id}: {validation.Error}");
            }
            catch (Exception exception)
            {
                errors.Add($"calculation-pipelines/{id} could not be parsed: {exception.Message}");
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
