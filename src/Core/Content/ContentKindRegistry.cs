using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Effects;
using Core.Entity.Definitions;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.StatusEffects;
using Core.Meta;

namespace Core.Content;

public sealed record ContentKindDescriptor(
    string Kind,
    string CanonicalDirectory,
    Type DefinitionType,
    string? IdentityProperty = null,
    string SearchPattern = "*.json",
    Func<string, bool>? FileFilter = null)
{
    public bool Includes(string fileName) => FileFilter?.Invoke(fileName) ?? true;
}

public interface IContentKindRegistry
{
    IReadOnlyList<ContentKindDescriptor> Kinds { get; }
    Result<ContentKindDescriptor> Get(string kind);
    Result<JsonElement> Normalize(string kind, string definitionId, JsonElement definition);
    Result Validate(string kind, string definitionId, JsonElement definition);
}

/// <summary>
/// The sole registry of content kinds understood by the engine. Discovery,
/// package compilation, runtime deserialization and graph validation all use
/// this exact metadata instead of maintaining parallel path/type tables.
/// </summary>
public sealed class ContentKindRegistry : IContentKindRegistry
{
    private static readonly JsonSerializerOptions StrictOptions = CreateOptions();
    private readonly ImmutableDictionary<string, ContentKindDescriptor> _byKind;

    public static ContentKindRegistry Default { get; } = new(CreateDefaults());

    public ContentKindRegistry(IEnumerable<ContentKindDescriptor> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var ordered = kinds.OrderBy(kind => kind.Kind, StringComparer.Ordinal).ToImmutableArray();
        var duplicate = ordered.GroupBy(kind => kind.Kind, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException($"Duplicate content kind: {duplicate.Key}", nameof(kinds));
        _byKind = ordered.ToImmutableDictionary(kind => kind.Kind, StringComparer.OrdinalIgnoreCase);
        Kinds = ordered;
    }

    public IReadOnlyList<ContentKindDescriptor> Kinds { get; }

    public Result<ContentKindDescriptor> Get(string kind) =>
        string.IsNullOrWhiteSpace(kind)
            ? Result<ContentKindDescriptor>.Failure("Content kind is required")
            : _byKind.TryGetValue(kind, out var descriptor)
                ? Result<ContentKindDescriptor>.Success(descriptor)
                : Result<ContentKindDescriptor>.Failure($"Unknown content kind: {kind}");

    public Result<JsonElement> Normalize(string kind, string definitionId, JsonElement definition)
    {
        var descriptor = Get(kind);
        if (descriptor.IsFailure)
            return Result<JsonElement>.Failure(descriptor.Error);
        if (definition.ValueKind != JsonValueKind.Object)
            return Result<JsonElement>.Failure($"Content definition must be an object: {kind}/{definitionId}");
        if (string.IsNullOrWhiteSpace(descriptor.Value.IdentityProperty) ||
            definition.EnumerateObject().Any(property =>
                property.Name.Equals(descriptor.Value.IdentityProperty, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<JsonElement>.Success(definition.Clone());
        }

        var normalized = definition.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.Clone(),
            StringComparer.Ordinal);
        normalized[descriptor.Value.IdentityProperty!] = JsonSerializer.SerializeToElement(definitionId);
        return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(normalized));
    }

    public Result Validate(string kind, string definitionId, JsonElement definition)
    {
        var descriptor = Get(kind);
        if (descriptor.IsFailure)
            return Result.Failure(descriptor.Error);
        if (definition.ValueKind != JsonValueKind.Object)
            return Result.Failure($"Content definition must be an object: {kind}/{definitionId}");
        if (!string.IsNullOrWhiteSpace(descriptor.Value.IdentityProperty))
        {
            var identity = definition.EnumerateObject().FirstOrDefault(property =>
                property.Name.Equals(descriptor.Value.IdentityProperty, StringComparison.OrdinalIgnoreCase));
            if (identity.Value.ValueKind != JsonValueKind.String ||
                !string.Equals(identity.Value.GetString(), definitionId, StringComparison.Ordinal))
            {
                return Result.Failure(
                    $"Content definition {kind}/{definitionId} must declare " +
                    $"{descriptor.Value.IdentityProperty}='{definitionId}'");
            }
        }
        if (descriptor.Value.DefinitionType == typeof(JsonElement))
            return Result.Success();
        try
        {
            var value = JsonSerializer.Deserialize(
                definition.GetRawText(),
                descriptor.Value.DefinitionType,
                StrictOptions);
            return value == null
                ? Result.Failure($"Content definition is invalid: {kind}/{definitionId}")
                : Result.Success();
        }
        catch (JsonException exception)
        {
            return Result.Failure(
                $"Content definition {kind}/{definitionId} does not match its declared schema: {exception.Message}");
        }
    }

    private static IReadOnlyList<ContentKindDescriptor> CreateDefaults() =>
    [
        new("actions", "actions", typeof(ActionDefinition), "actionId"),
        new("actor-resource-lifecycle-policies", "actor-resource-lifecycle-policies", typeof(ActorResourceLifecyclePolicyDefinition), "actorResourceLifecyclePolicyId"),
        new("boards", "boards", typeof(JsonElement)),
        new("calculation-pipelines", "calculation-pipelines", typeof(CalculationPipelineDefinition), "pipelineId"),
        new("capability-policies", "capability-policies", typeof(CapabilityPolicyDefinition), "capabilityPolicyId"),
        new("card-component-bundles", "card-component-bundles", typeof(CardComponentBundleDefinition), "bundleId"),
        new("card-pools", "card-pools", typeof(CardPoolDefinition), "poolId"),
        new("card-selections", "card-selections", typeof(CardSelectionDefinition), "selectionId"),
        new("card-upgrades", "card-upgrades", typeof(CardUpgradeDefinition), "upgradeId"),
        new("card-zone-systems", "card-zone-systems", typeof(Core.CardZones.CardZoneSystemDefinition), "cardZoneSystemId"),
        new("cards", "cards", typeof(CardContentDefinition), "cardId"),
        new("combat-rules", "combat-rules", typeof(CombatRulesDefinition), "combatRulesId"),
        new("condensation-recipes", "condensation-recipes", typeof(CondensationRecipeDefinition), "recipeId"),
        new("companions", "companions", typeof(JsonElement)),
        new("content-binding-policies", "content-binding-policies", typeof(ContentBindingPolicyDefinition), "contentBindingPolicyId"),
        new("daily-challenges", "daily-challenges", typeof(DailyChallengeDefinition), "challengeId"),
        new("decks", "decks", typeof(JsonElement)),
        new("dialogues", "dialogues", typeof(Core.Run.Dialogue.DialogueDefinition), "dialogueId"),
        new("enemies", "enemies", typeof(JsonElement)),
        new("enemy-pools", "enemy-pools", typeof(EnemyPoolDefinition), "enemyPoolId"),
        new("entities", "entities", typeof(EntityDefinition), "definitionId"),
        new("flow-rules", "flow-rules", typeof(FlowRulesDefinition), "flowRulesId"),
        new("formulas", "formulas", typeof(Core.Math.FormulaDefinition), null),
        new("gambits", "gambits", typeof(Core.Combat.Gambits.GambitDefinition), "gambitId"),
        new("keywords", "keywords", typeof(JsonElement)),
        new("modes", "modes", typeof(GameModeDefinition), "modeId"),
        new("modifiers", "modifiers", typeof(ScriptModifierDefinition), "modifierId"),
        new("phase-sequences", "phase-sequences", typeof(PhaseSequenceDefinition), "sequenceId"),
        new("powers", "powers", typeof(JsonElement)),
        new("preparations", "preparations", typeof(PreparationDefinition), "preparationId"),
        new("profile-progress-policies", "profile-progress-policies", typeof(ProfileProgressPolicyDefinition), "profileProgressPolicyId"),
        new("races", "races", typeof(JsonElement)),
        new("relics", "relics", typeof(RelicDefinition), "relicId"),
        new("replay-policies", "replay-policies", typeof(ReplayPolicyDefinition), "replayPolicyId"),
        new("resources", "resources", typeof(ResourceDefinition), "resourceId"),
        new("run-events", "run-events", typeof(JsonElement)),
        new("run-progression-policies", "run-progression-policies", typeof(RunProgressionPolicyDefinition), "progressionPolicyId"),
        new("runs", "runs", typeof(RunDefinition), "runId"),
        new("shops", "shops", typeof(ShopDefinition), "shopId"),
        new("status-effects", "status-effects", typeof(StatusEffectDefinition), "statusId"),
        new("timeline-policies", "timeline-policies", typeof(TimelinePolicyDefinition), "timelinePolicyId"),
        new("zones", "zones", typeof(JsonElement))
    ];

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
