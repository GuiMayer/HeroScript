using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Entity.Definitions;
using Core.Entity.Integration;
using Core.Resources;
using Core.Run.Content;
using Core.StatusEffects;
using Core.Calculations;
using System.Text.Json.Serialization;

namespace Core.Run.Sandbox;

/// <summary>
/// Declarative, serializable input for a sandbox combat. It is intentionally
/// not a mutable combat snapshot: the compiler resolves it into authoritative
/// run and combat state.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CombatScenarioDefinition
{
    private ImmutableArray<ScenarioCardDefinition> _deck = [];
    private ImmutableArray<ScenarioParticipantDefinition> _participants = [];
    private ImmutableArray<ContextualInfluenceDefinition> _influences = [];

    public int SchemaVersion { get; init; } = 2;
    public string ModeId { get; init; } = string.Empty;
    public string? ContentRevision { get; init; }
    public ulong Seed { get; init; }
    public string AttemptKey { get; init; } = string.Empty;
    public CombatRelationshipPolicy Relationships { get; init; } = new();
    public ImmutableArray<CombatSide> Sides { get; init; } =
    [
        new() { SideId = "player" },
        new() { SideId = "opposition" }
    ];
    public IReadOnlyList<ContextualInfluenceDefinition> Influences
    {
        get => _influences;
        init => _influences = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ScenarioCardDefinition> Deck
    {
        get => _deck;
        init => _deck = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ScenarioParticipantDefinition> Participants
    {
        get => _participants;
        init => _participants = value?.ToImmutableArray() ?? [];
    }
    public ScenarioInitialState InitialState { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioParticipantDefinition
{
    public string InstanceId { get; init; } = string.Empty;
    public string EntityDefinitionId { get; init; } = string.Empty;
    public string SideId { get; init; } = string.Empty;
    public ControllerBinding ControllerBinding { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioCardDefinition
{
    private ImmutableArray<string> _upgradeIds = [];
    public string DefinitionId { get; init; } = string.Empty;
    public IReadOnlyList<string> UpgradeIds
    {
        get => _upgradeIds;
        init => _upgradeIds = value?.ToImmutableArray() ?? [];
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioInitialEffectDefinition
{
    public string TargetActorId { get; init; } = string.Empty;
    public string StatusId { get; init; } = string.Empty;
    public int Stacks { get; init; } = 1;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioInitialState
{
    private ImmutableDictionary<string, IReadOnlyDictionary<string, float>> _resourcesByActor =
        ImmutableDictionary<string, IReadOnlyDictionary<string, float>>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableArray<ScenarioInitialEffectDefinition> _effects = [];

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>> ResourcesByActor
    {
        get => _resourcesByActor;
        init => _resourcesByActor = value?.ToImmutableDictionary(
            item => item.Key,
            item => (IReadOnlyDictionary<string, float>)item.Value.ToImmutableDictionary(StringComparer.Ordinal),
            StringComparer.Ordinal)
            ?? ImmutableDictionary<string, IReadOnlyDictionary<string, float>>.Empty.WithComparers(StringComparer.Ordinal);
    }
    public IReadOnlyList<ScenarioInitialEffectDefinition> Effects
    {
        get => _effects;
        init => _effects = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CompiledCombatScenario
{
    private ImmutableArray<CombatActorState> _participants = [];
    private ImmutableDictionary<string, IReadOnlyList<StatusEffectInstance>> _initialStatusEffects =
        ImmutableDictionary<string, IReadOnlyList<StatusEffectInstance>>.Empty.WithComparers(StringComparer.Ordinal);

    public CombatScenarioDefinition Scenario { get; init; } = new();
    public string ScenarioHash { get; init; } = string.Empty;
    public ContentManifest ContentManifest { get; init; } = new();
    public RunStartOptions RunStart { get; init; } = new();
    public IReadOnlyList<CombatActorState> Participants
    {
        get => _participants;
        init => _participants = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>> InitialStatusEffects
    {
        get => _initialStatusEffects;
        init => _initialStatusEffects = value?
            .ToImmutableDictionary(
                item => item.Key,
                item => (IReadOnlyList<StatusEffectInstance>)item.Value.ToImmutableArray(),
                StringComparer.Ordinal)
            ?? ImmutableDictionary<string, IReadOnlyList<StatusEffectInstance>>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public interface ICombatScenarioCompiler
{
    Result<CompiledCombatScenario> Compile(CombatScenarioDefinition scenario, string configName = "default");
}

/// <summary>
/// Validates a scenario against the resolved mode and materializes all
/// identities before a run exists. The result has no mutable references to
/// caller input and can be hashed, replayed and safely persisted.
/// </summary>
public sealed class CombatScenarioCompiler : ICombatScenarioCompiler
{
    private readonly IRevisionedGameModeResolver _modes;
    private readonly EntityDefinitionLoader _entities;
    private readonly IResourceManager _resources;
    private readonly IContentManifestProvider _manifests;
    private readonly IPinnedContentCatalog<StatusEffectDefinition> _statuses;
    private readonly IContentRuntimeResolver _contentRuntimes;

    public CombatScenarioCompiler(
        IRevisionedGameModeResolver modes,
        EntityDefinitionLoader entities,
        IResourceManager resources,
        IContentManifestProvider manifests,
        IPinnedContentCatalog<StatusEffectDefinition> statuses,
        IContentRuntimeResolver contentRuntimes)
    {
        _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
        _statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
        _contentRuntimes = contentRuntimes ?? throw new ArgumentNullException(nameof(contentRuntimes));
    }

    public Result<CompiledCombatScenario> Compile(CombatScenarioDefinition scenario, string configName = "default")
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (scenario.SchemaVersion != 2)
            return Result<CompiledCombatScenario>.Failure($"Unsupported combat scenario schema: {scenario.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(scenario.ModeId))
            return Result<CompiledCombatScenario>.Failure("Scenario modeId is required");
        if (string.IsNullOrWhiteSpace(scenario.AttemptKey))
            return Result<CompiledCombatScenario>.Failure("Scenario attemptKey is required");
        var relationships = GameplayRelationshipValidator.Validate(
            scenario.Participants.Select(participant => participant.SideId), scenario.Sides, scenario.Relationships);
        if (relationships.IsFailure) return Result<CompiledCombatScenario>.Failure(relationships.Error);

        var manifest = ResolveManifest(scenario.ContentRevision, configName);
        if (manifest.IsFailure)
            return Result<CompiledCombatScenario>.Failure(manifest.Error);

        var mode = _modes.Resolve(scenario.ModeId, configName, manifest.Value.Revision);
        if (mode.IsFailure)
            return Result<CompiledCombatScenario>.Failure(mode.Error);
        var scenarioInfluences = ValidateScenarioInfluences(scenario, mode.Value, manifest.Value.Revision, configName);
        if (scenarioInfluences.IsFailure) return Result<CompiledCombatScenario>.Failure(scenarioInfluences.Error);
        if (!mode.Value.CapabilityPolicy.AllowScenarioAuthoring)
            return Result<CompiledCombatScenario>.Failure($"Game mode does not allow scenario authoring: {scenario.ModeId}");
        if (!mode.Value.CapabilityPolicy.AllowCustomDeck)
            return Result<CompiledCombatScenario>.Failure($"Game mode does not allow custom decks: {scenario.ModeId}");
        if (scenario.Deck.Count < 1 || scenario.Deck.Count > mode.Value.CapabilityPolicy.MaxCards)
            return Result<CompiledCombatScenario>.Failure($"Scenario deck must contain between 1 and {mode.Value.CapabilityPolicy.MaxCards} cards");
        if (scenario.Participants.Count < 1 || scenario.Participants.Count > mode.Value.CapabilityPolicy.MaxActors)
            return Result<CompiledCombatScenario>.Failure($"Scenario must contain between 1 and {mode.Value.CapabilityPolicy.MaxActors} actors");
        if (scenario.InitialState.ResourcesByActor.Count > 0 && !mode.Value.CapabilityPolicy.AllowResourceOverrides)
            return Result<CompiledCombatScenario>.Failure("Game mode does not allow initial resource overrides");
        if (scenario.InitialState.Effects.Count > 0 && !mode.Value.CapabilityPolicy.AllowInitialEffects)
            return Result<CompiledCombatScenario>.Failure("Game mode does not allow initial status effects");

        var allowedCards = ResolveAllowedCards(
            mode.Value.Definition.CardPoolIds,
            configName,
            manifest.Value.Revision);
        if (allowedCards.IsFailure)
            return Result<CompiledCombatScenario>.Failure(allowedCards.Error);
        var startingCards = new List<RunStartingCard>();
        foreach (var card in scenario.Deck)
        {
            if (string.IsNullOrWhiteSpace(card.DefinitionId))
                return Result<CompiledCombatScenario>.Failure("Scenario deck contains an empty definitionId");
            if (!allowedCards.Value.Contains(card.DefinitionId))
                return Result<CompiledCombatScenario>.Failure($"Card is not allowed by the game mode: {card.DefinitionId}");
            var definition = GetContent<CardContentDefinition>(
                "cards",
                card.DefinitionId,
                manifest.Value.Revision,
                configName);
            if (definition.IsFailure)
                return Result<CompiledCombatScenario>.Failure(definition.Error);
            var resolvedUpgrades = new List<CardUpgradeState>();
            foreach (var upgradeId in card.UpgradeIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                var upgrade = GetContent<CardUpgradeDefinition>(
                    "card-upgrades",
                    upgradeId,
                    manifest.Value.Revision,
                    configName);
                if (upgrade.IsFailure)
                    return Result<CompiledCombatScenario>.Failure(upgrade.Error);
                if (!upgrade.Value.AppliesTo(card.DefinitionId))
                    return Result<CompiledCombatScenario>.Failure(
                        $"Upgrade '{upgradeId}' does not apply to card '{card.DefinitionId}'");
                resolvedUpgrades.Add(new CardUpgradeState
                {
                    UpgradeId = upgrade.Value.UpgradeId,
                    Patches = upgrade.Value.Patches
                });
            }
            startingCards.Add(new RunStartingCard { DefinitionId = definition.Value.CardId, Upgrades = resolvedUpgrades });
        }

        var allowedAiActors = ResolveAllowedEnemies(
            mode.Value.Definition.EnemyPoolIds,
            configName,
            manifest.Value.Revision);
        if (allowedAiActors.IsFailure)
            return Result<CompiledCombatScenario>.Failure(allowedAiActors.Error);
        var participantIds = new HashSet<string>(StringComparer.Ordinal);
        var materializer = new EntityMaterializer(_resources);
        var participants = new List<CombatActorState>();
        foreach (var participant in scenario.Participants)
        {
            if (string.IsNullOrWhiteSpace(participant.InstanceId) ||
                string.IsNullOrWhiteSpace(participant.EntityDefinitionId) ||
                string.IsNullOrWhiteSpace(participant.SideId) || participant.ControllerBinding == null ||
                !Enum.IsDefined(participant.ControllerBinding.Kind))
                return Result<CompiledCombatScenario>.Failure(
                    "Every scenario participant requires instanceId, entityDefinitionId, sideId and controllerBinding");
            if (!participantIds.Add(participant.InstanceId))
                return Result<CompiledCombatScenario>.Failure(
                    $"Scenario participant instanceId is duplicated: {participant.InstanceId}");
            if (participant.ControllerBinding.Kind == ControllerKind.AI &&
                !allowedAiActors.Value.Contains(participant.EntityDefinitionId))
                return Result<CompiledCombatScenario>.Failure(
                    $"AI actor is not allowed by the game mode: {participant.EntityDefinitionId}");
            var materialized = MaterializeParticipant(
                materializer,
                participant,
                manifest.Value.Revision,
                configName);
            if (materialized.IsFailure)
                return Result<CompiledCombatScenario>.Failure(materialized.Error);
            var overrides = scenario.InitialState.ResourcesByActor.GetValueOrDefault(participant.InstanceId)
                ?? ImmutableDictionary<string, float>.Empty;
            var withResources = ApplyResources(materialized.Value, overrides);
            if (withResources.IsFailure)
                return Result<CompiledCombatScenario>.Failure(withResources.Error);
            participants.Add(withResources.Value);
        }
        if (scenario.InitialState.ResourcesByActor.Keys.Any(id => !participantIds.Contains(id)))
            return Result<CompiledCombatScenario>.Failure("Resource override references an unknown scenario participant");

        var normalized = scenario with
        {
            ContentRevision = manifest.Value.Revision,
            Deck = scenario.Deck.Select(card => card with
            {
                UpgradeIds = card.UpgradeIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()
            }).ToArray(),
            Participants = scenario.Participants.ToArray(),
            InitialState = scenario.InitialState with
            {
                Effects = scenario.InitialState.Effects
                    .OrderBy(effect => effect.TargetActorId, StringComparer.Ordinal)
                    .ThenBy(effect => effect.StatusId, StringComparer.Ordinal)
                    .ThenBy(effect => effect.Stacks)
                    .ToArray()
            }
        };
        var initialStatuses = CompileInitialStatusEffects(normalized, participantIds, manifest.Value.Revision);
        if (initialStatuses.IsFailure)
            return Result<CompiledCombatScenario>.Failure(initialStatuses.Error);
        var scenarioHash = CanonicalJson.ComputeHash(normalized);
        var runOwner = normalized.Participants.FirstOrDefault(
            participant => participant.ControllerBinding.Kind == ControllerKind.Player)
            ?? normalized.Participants[0];
        return Result<CompiledCombatScenario>.Success(new CompiledCombatScenario
        {
            Scenario = normalized,
            ScenarioHash = scenarioHash,
            ContentManifest = manifest.Value,
            RunStart = new RunStartOptions(
                configName,
                mode.Value.Definition.RunDefinitionId ?? "default_run",
                runOwner.InstanceId,
                normalized.Seed,
                manifest.Value.Revision,
                normalized.ModeId,
                StartingDeck: startingCards,
                StartingHandSize: System.Math.Min(
                    mode.Value.CombatRules.Flow.DeckCycle.DrawPerActivation,
                    startingCards.Count),
                ScenarioHash: scenarioHash,
                AttemptKey: normalized.AttemptKey,
                Scenario: normalized,
                SettingId: configName),
            Participants = participants.ToImmutableArray(),
            InitialStatusEffects = initialStatuses.Value
        });
    }

    private Result ValidateScenarioInfluences(CombatScenarioDefinition scenario, ResolvedGameMode mode,
        string revision, string configName)
    {
        var duplicates = scenario.Influences.Where(item => !string.IsNullOrWhiteSpace(item.InfluenceId))
            .GroupBy(item => item.InfluenceId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicates != null) return Result.Failure($"Scenario contains duplicate influence: {duplicates.Key}");
        var runtime = _contentRuntimes.Resolve(revision, configName);
        if (runtime.IsFailure) return Result.Failure(runtime.Error);
        foreach (var influence in scenario.Influences)
        {
            var invalid = ContextualInfluencePolicies.Validate(influence);
            if (invalid != null) return Result.Failure($"Scenario {invalid}");
            var reachable = mode.Definition.CalculationPipelineIds.Select(id =>
                    runtime.Value.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", id))
                .Any(result => result.IsSuccess && result.Value.Channel == influence.Channel &&
                    result.Value.Buckets.Any(bucket => bucket.BucketId == influence.Bucket));
            if (!reachable)
                return Result.Failure($"Scenario influence {influence.InfluenceId} is not reachable through an enabled calculation pipeline");
        }
        return Result.Success();
    }

    private Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>> CompileInitialStatusEffects(
        CombatScenarioDefinition scenario,
        IReadOnlySet<string> participantIds,
        string contentRevision)
    {
        if (scenario.InitialState.Effects.Count == 0)
        {
            return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Success(
                ImmutableDictionary<string, IReadOnlyList<StatusEffectInstance>>.Empty.WithComparers(StringComparer.Ordinal));
        }

        var context = DeterministicContext.Create(scenario.Seed, contentRevision);
        var statuses = new Dictionary<string, List<StatusEffectInstance>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < scenario.InitialState.Effects.Count; index++)
        {
            var effect = scenario.InitialState.Effects[index];
            if (!participantIds.Contains(effect.TargetActorId))
            {
                return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Failure(
                    $"Initial status target is not a scenario participant: {effect.TargetActorId}");
            }
            if (string.IsNullOrWhiteSpace(effect.StatusId) || effect.Stacks < 1)
            {
                return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Failure(
                    "Initial status requires a statusId and positive stacks");
            }
            if (!seen.Add($"{effect.TargetActorId}\n{effect.StatusId}"))
            {
                return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Failure(
                    $"Initial status is duplicated for target: {effect.TargetActorId}/{effect.StatusId}");
            }

            var definition = _statuses.Get(effect.StatusId, contentRevision);
            if (definition.IsFailure)
                return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Failure(definition.Error);
            var allocated = context.AllocateId($"scenario-status:{scenario.AttemptKey}:{effect.TargetActorId}:{effect.StatusId}:{index}");
            context = allocated.Context;
            if (!statuses.TryGetValue(effect.TargetActorId, out var targetStatuses))
            {
                targetStatuses = [];
                statuses[effect.TargetActorId] = targetStatuses;
            }
            targetStatuses.Add(new StatusEffectInstance
            {
                InstanceId = allocated.Value,
                StatusId = definition.Value.StatusId,
                Definition = definition.Value,
                ContentRevision = contentRevision,
                TargetId = effect.TargetActorId,
                Stacks = System.Math.Min(effect.Stacks, definition.Value.MaxStacks),
                Duration = definition.Value.DefaultDuration,
                AppliedAt = allocated.Context.LogicalTimestamp.UtcDateTime,
                TurnApplied = 1,
                IsActive = true
            });
        }

        return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Success(
            statuses
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToImmutableDictionary(
                    item => item.Key,
                    item => (IReadOnlyList<StatusEffectInstance>)item.Value.OrderBy(status => status.InstanceId).ToArray(),
                    StringComparer.Ordinal));
    }

    private Result<ContentManifest> ResolveManifest(string? revision, string configName)
    {
        if (string.IsNullOrWhiteSpace(revision))
            return Result<ContentManifest>.Failure("Scenario contentRevision is required");

        var historical = _manifests.GetByRevision(revision);
        if (historical.IsFailure ||
            !string.Equals(historical.Value.ConfigName, configName, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ContentManifest>.Failure($"Scenario content revision is not available: {revision}");
        }

        var runtime = _contentRuntimes.Resolve(revision, configName);
        return runtime.IsFailure
            ? Result<ContentManifest>.Failure(runtime.Error)
            : historical;
    }

    private Result<HashSet<string>> ResolveAllowedCards(
        IReadOnlyList<string> poolIds,
        string configName,
        string contentRevision)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var runtimeResult = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtimeResult.IsFailure)
            return Result<HashSet<string>>.Failure(runtimeResult.Error);

        foreach (var poolId in poolIds)
        {
            var revisionedPool = runtimeResult.Value.GetDefinition<CardPoolDefinition>("card-pools", poolId);
            if (revisionedPool.IsFailure)
                return Result<HashSet<string>>.Failure(revisionedPool.Error);

            var includeTags = new HashSet<string>(revisionedPool.Value.IncludeTags, StringComparer.OrdinalIgnoreCase);
            var excludeTags = new HashSet<string>(revisionedPool.Value.ExcludeTags, StringComparer.OrdinalIgnoreCase);
            var explicitIds = new HashSet<string>(revisionedPool.Value.ExplicitCardIds, StringComparer.OrdinalIgnoreCase);
            foreach (var cardId in runtimeResult.Value.GetDefinitions("cards").Keys)
            {
                var card = runtimeResult.Value.GetDefinition<CardContentDefinition>("cards", cardId);
                if (card.IsFailure)
                    return Result<HashSet<string>>.Failure(card.Error);
                if (explicitIds.Count > 0 && !explicitIds.Contains(cardId))
                    continue;
                if (includeTags.Count > 0 && !includeTags.All(tag => card.Value.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                    continue;
                if (excludeTags.Any(tag => card.Value.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                    continue;
                if (revisionedPool.Value.RarityWeights.Count > 0 && !revisionedPool.Value.RarityWeights.ContainsKey(card.Value.Rarity))
                    continue;
                ids.Add(cardId);
            }
        }
        return Result<HashSet<string>>.Success(ids);
    }

    private Result<HashSet<string>> ResolveAllowedEnemies(
        IReadOnlyList<string> poolIds,
        string configName,
        string contentRevision)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var poolId in poolIds)
        {
            var pool = GetContent<EnemyPoolDefinition>(
                "enemy-pools",
                poolId,
                contentRevision,
                configName);
            if (pool.IsFailure)
                return Result<HashSet<string>>.Failure(pool.Error);
            ids.UnionWith(pool.Value.EntityDefinitionIds);
        }
        return Result<HashSet<string>>.Success(ids);
    }

    private Result<CombatActorState> MaterializeParticipant(
        EntityMaterializer materializer,
        ScenarioParticipantDefinition participant,
        string contentRevision,
        string configName)
    {
        var definition = _entities.LoadDefinition(participant.EntityDefinitionId, contentRevision, configName);
        if (definition.IsFailure)
            return Result<CombatActorState>.Failure(definition.Error);
        return materializer.Materialize(definition.Value, participant.InstanceId, contentRevision,
            participant.SideId, participant.ControllerBinding, configName);
    }

    private Result<T> GetContent<T>(
        string kind,
        string id,
        string contentRevision,
        string? configName)
    {
        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        return runtime.IsFailure
            ? Result<T>.Failure(runtime.Error)
            : runtime.Value.GetDefinition<T>(kind, id);
    }

    private static Result<CombatActorState> ApplyResources(
        CombatActorState participant,
        IReadOnlyDictionary<string, float> overrides)
    {
        var current = participant;
        foreach (var (resourceId, value) in overrides.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return Result<CombatActorState>.Failure($"Resource override must be finite: {resourceId}");
            var resource = current.GetResource(resourceId);
            if (resource == null)
                return Result<CombatActorState>.Failure($"Resource is not present on scenario actor: {resourceId}");
            var updated = current.ApplyResourceMutation(
                $"scenario-override:{current.InstanceId}:{resourceId}",
                resourceId,
                ResourceMutationOperation.Set,
                value);
            if (updated.IsFailure)
                return Result<CombatActorState>.Failure(updated.Error);
            current = updated.Value;
        }
        return Result<CombatActorState>.Success(current);
    }
}
