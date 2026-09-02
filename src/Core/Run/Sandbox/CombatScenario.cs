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

namespace Core.Run.Sandbox;

/// <summary>
/// Declarative, serializable input for a sandbox combat. It is intentionally
/// not a mutable combat snapshot: the compiler resolves it into authoritative
/// run and combat state.
/// </summary>
public sealed record CombatScenarioDefinition
{
    private ImmutableArray<ScenarioCardDefinition> _deck = [];
    private ImmutableArray<ScenarioEnemyDefinition> _enemies = [];

    public int SchemaVersion { get; init; } = 1;
    public string ModeId { get; init; } = string.Empty;
    public string? ContentRevision { get; init; }
    public ulong Seed { get; init; }
    public string AttemptKey { get; init; } = string.Empty;
    public ScenarioHeroDefinition Hero { get; init; } = new();
    public IReadOnlyList<ScenarioCardDefinition> Deck
    {
        get => _deck;
        init => _deck = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ScenarioEnemyDefinition> Enemies
    {
        get => _enemies;
        init => _enemies = value?.ToImmutableArray() ?? [];
    }
    public ScenarioInitialState InitialState { get; init; } = new();
}

public sealed record ScenarioHeroDefinition
{
    public string Alias { get; init; } = "hero";
    public string EntityDefinitionId { get; init; } = string.Empty;
}

public sealed record ScenarioEnemyDefinition
{
    public string Alias { get; init; } = string.Empty;
    public string EntityDefinitionId { get; init; } = string.Empty;
}

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

public sealed record ScenarioInitialEffectDefinition
{
    public string TargetAlias { get; init; } = string.Empty;
    public string StatusId { get; init; } = string.Empty;
    public int Stacks { get; init; } = 1;
}

public sealed record ScenarioInitialState
{
    private ImmutableDictionary<string, float> _heroResources =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableArray<ScenarioInitialEffectDefinition> _effects = [];

    public IReadOnlyDictionary<string, float> HeroResources
    {
        get => _heroResources;
        init => _heroResources = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    }
    public IReadOnlyList<ScenarioInitialEffectDefinition> Effects
    {
        get => _effects;
        init => _effects = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CompiledCombatScenario
{
    private ImmutableArray<CombatEntity> _enemies = [];
    private ImmutableDictionary<string, IReadOnlyList<StatusEffectInstance>> _initialStatusEffects =
        ImmutableDictionary<string, IReadOnlyList<StatusEffectInstance>>.Empty.WithComparers(StringComparer.Ordinal);

    public CombatScenarioDefinition Scenario { get; init; } = new();
    public string ScenarioHash { get; init; } = string.Empty;
    public ContentManifest ContentManifest { get; init; } = new();
    public RunStartOptions RunStart { get; init; } = new();
    public CombatEntity Hero { get; init; } = new();
    public IReadOnlyList<CombatEntity> Enemies
    {
        get => _enemies;
        init => _enemies = value?.ToImmutableArray() ?? [];
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
    private readonly IGameModeResolver _modes;
    private readonly ICardContentCatalog _cards;
    private readonly ICardPoolResolver _cardPools;
    private readonly IResourceCatalog<EnemyPoolDefinition> _enemyPools;
    private readonly IResourceCatalog<CardUpgradeDefinition> _upgrades;
    private readonly EntityDefinitionLoader _entities;
    private readonly IResourceManager _resources;
    private readonly IContentManifestProvider _manifests;
    private readonly IStatusEffectManager _statuses;
    private readonly IContentRuntimeResolver? _contentRuntimes;

    public CombatScenarioCompiler(
        IGameModeResolver modes,
        ICardContentCatalog cards,
        ICardPoolResolver cardPools,
        IResourceCatalog<EnemyPoolDefinition> enemyPools,
        IResourceCatalog<CardUpgradeDefinition> upgrades,
        EntityDefinitionLoader entities,
        IResourceManager resources,
        IContentManifestProvider manifests,
        IStatusEffectManager statuses,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _modes = modes;
        _cards = cards;
        _cardPools = cardPools;
        _enemyPools = enemyPools;
        _upgrades = upgrades;
        _entities = entities;
        _resources = resources;
        _manifests = manifests;
        _statuses = statuses;
        _contentRuntimes = contentRuntimes;
    }

    public Result<CompiledCombatScenario> Compile(CombatScenarioDefinition scenario, string configName = "default")
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (scenario.SchemaVersion != 1)
            return Result<CompiledCombatScenario>.Failure($"Unsupported combat scenario schema: {scenario.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(scenario.ModeId))
            return Result<CompiledCombatScenario>.Failure("Scenario modeId is required");
        if (string.IsNullOrWhiteSpace(scenario.AttemptKey))
            return Result<CompiledCombatScenario>.Failure("Scenario attemptKey is required");
        if (string.IsNullOrWhiteSpace(scenario.Hero.Alias) ||
            string.IsNullOrWhiteSpace(scenario.Hero.EntityDefinitionId))
        {
            return Result<CompiledCombatScenario>.Failure("Scenario hero alias and entityDefinitionId are required");
        }

        var manifest = ResolveManifest(scenario.ContentRevision, configName);
        if (manifest.IsFailure)
            return Result<CompiledCombatScenario>.Failure(manifest.Error);

        var mode = _modes is IRevisionedGameModeResolver revisionedModes
            ? revisionedModes.Resolve(scenario.ModeId, configName, manifest.Value.Revision)
            : _modes.Resolve(scenario.ModeId, configName);
        if (mode.IsFailure)
            return Result<CompiledCombatScenario>.Failure(mode.Error);
        if (!mode.Value.CapabilityPolicy.AllowScenarioAuthoring)
            return Result<CompiledCombatScenario>.Failure($"Game mode does not allow scenario authoring: {scenario.ModeId}");
        if (!mode.Value.CapabilityPolicy.AllowCustomDeck)
            return Result<CompiledCombatScenario>.Failure($"Game mode does not allow custom decks: {scenario.ModeId}");
        if (scenario.Deck.Count < 1 || scenario.Deck.Count > mode.Value.CapabilityPolicy.MaxCards)
            return Result<CompiledCombatScenario>.Failure($"Scenario deck must contain between 1 and {mode.Value.CapabilityPolicy.MaxCards} cards");
        if (scenario.Enemies.Count < 1 || scenario.Enemies.Count > mode.Value.CapabilityPolicy.MaxEnemies)
            return Result<CompiledCombatScenario>.Failure($"Scenario must contain between 1 and {mode.Value.CapabilityPolicy.MaxEnemies} enemies");
        if (scenario.InitialState.HeroResources.Count > 0 && !mode.Value.CapabilityPolicy.AllowResourceOverrides)
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
                configName,
                () => _cards.GetCard(card.DefinitionId, configName));
            if (definition.IsFailure)
                return Result<CompiledCombatScenario>.Failure(definition.Error);
            var resolvedUpgrades = new List<CardUpgradeState>();
            foreach (var upgradeId in card.UpgradeIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                var upgrade = GetContent<CardUpgradeDefinition>(
                    "card-upgrades",
                    upgradeId,
                    manifest.Value.Revision,
                    configName,
                    () => _upgrades.Get(upgradeId, configName));
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

        var allowedEnemies = ResolveAllowedEnemies(
            mode.Value.Definition.EnemyPoolIds,
            configName,
            manifest.Value.Revision);
        if (allowedEnemies.IsFailure)
            return Result<CompiledCombatScenario>.Failure(allowedEnemies.Error);
        var aliases = new HashSet<string>(StringComparer.Ordinal) { scenario.Hero.Alias };
        var factory = new EntityCombatAdapter(_resources);
        var hero = MaterializeParticipant(
            factory,
            scenario.Hero.Alias,
            scenario.Hero.EntityDefinitionId,
            true,
            manifest.Value.Revision,
            configName);
        if (hero.IsFailure)
            return Result<CompiledCombatScenario>.Failure(hero.Error);
        var heroWithResources = ApplyResources(hero.Value, scenario.InitialState.HeroResources);
        if (heroWithResources.IsFailure)
            return Result<CompiledCombatScenario>.Failure(heroWithResources.Error);
        var enemies = new List<CombatEntity>();
        foreach (var enemy in scenario.Enemies.OrderBy(item => item.Alias, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(enemy.Alias) || string.IsNullOrWhiteSpace(enemy.EntityDefinitionId))
                return Result<CompiledCombatScenario>.Failure("Scenario enemy alias and entityDefinitionId are required");
            if (!aliases.Add(enemy.Alias))
                return Result<CompiledCombatScenario>.Failure($"Scenario participant alias is duplicated: {enemy.Alias}");
            if (!allowedEnemies.Value.Contains(enemy.EntityDefinitionId))
                return Result<CompiledCombatScenario>.Failure($"Enemy is not allowed by the game mode: {enemy.EntityDefinitionId}");
            var materialized = MaterializeParticipant(
                factory,
                enemy.Alias,
                enemy.EntityDefinitionId,
                false,
                manifest.Value.Revision,
                configName);
            if (materialized.IsFailure)
                return Result<CompiledCombatScenario>.Failure(materialized.Error);
            enemies.Add(materialized.Value);
        }

        var normalized = scenario with
        {
            ContentRevision = manifest.Value.Revision,
            Deck = scenario.Deck.Select(card => card with
            {
                UpgradeIds = card.UpgradeIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()
            }).ToArray(),
            Enemies = scenario.Enemies.OrderBy(item => item.Alias, StringComparer.Ordinal).ToArray(),
            InitialState = scenario.InitialState with
            {
                Effects = scenario.InitialState.Effects
                    .OrderBy(effect => effect.TargetAlias, StringComparer.Ordinal)
                    .ThenBy(effect => effect.StatusId, StringComparer.Ordinal)
                    .ThenBy(effect => effect.Stacks)
                    .ToArray()
            }
        };
        var initialStatuses = CompileInitialStatusEffects(normalized, aliases, manifest.Value.Revision);
        if (initialStatuses.IsFailure)
            return Result<CompiledCombatScenario>.Failure(initialStatuses.Error);
        var scenarioHash = CanonicalJson.ComputeHash(normalized);
        return Result<CompiledCombatScenario>.Success(new CompiledCombatScenario
        {
            Scenario = normalized,
            ScenarioHash = scenarioHash,
            ContentManifest = manifest.Value,
            RunStart = new RunStartOptions(
                configName,
                mode.Value.Definition.RunDefinitionId ?? "default_run",
                normalized.Hero.Alias,
                normalized.Seed,
                manifest.Value.Revision,
                normalized.ModeId,
                StartingDeck: startingCards,
                StartingHandSize: System.Math.Min(
                    mode.Value.CombatRules.Flow.DeckCycle.DrawPerActivation,
                    startingCards.Count),
                ScenarioHash: scenarioHash,
                AttemptKey: normalized.AttemptKey,
                Scenario: normalized),
            Hero = heroWithResources.Value,
            Enemies = enemies.ToImmutableArray(),
            InitialStatusEffects = initialStatuses.Value
        });
    }

    private Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>> CompileInitialStatusEffects(
        CombatScenarioDefinition scenario,
        IReadOnlySet<string> participantAliases,
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
            if (!participantAliases.Contains(effect.TargetAlias))
            {
                return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Failure(
                    $"Initial status target is not a scenario participant: {effect.TargetAlias}");
            }
            if (string.IsNullOrWhiteSpace(effect.StatusId) || effect.Stacks < 1)
            {
                return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Failure(
                    "Initial status requires a statusId and positive stacks");
            }
            if (!seen.Add($"{effect.TargetAlias}\n{effect.StatusId}"))
            {
                return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Failure(
                    $"Initial status is duplicated for target: {effect.TargetAlias}/{effect.StatusId}");
            }

            var definition = GetContent<StatusEffectDefinition>(
                "status-effects",
                effect.StatusId,
                contentRevision,
                configName: null,
                () => _statuses.GetDefinition(effect.StatusId));
            if (definition.IsFailure)
                return Result<IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>>.Failure(definition.Error);
            var allocated = context.AllocateId($"scenario-status:{scenario.AttemptKey}:{effect.TargetAlias}:{effect.StatusId}:{index}");
            context = allocated.Context;
            if (!statuses.TryGetValue(effect.TargetAlias, out var targetStatuses))
            {
                targetStatuses = [];
                statuses[effect.TargetAlias] = targetStatuses;
            }
            targetStatuses.Add(new StatusEffectInstance
            {
                InstanceId = allocated.Value,
                StatusId = definition.Value.StatusId,
                Definition = definition.Value,
                TargetId = effect.TargetAlias,
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
        var current = _manifests.GetManifest(configName);
        if (current.IsFailure)
            return current;
        if (string.IsNullOrWhiteSpace(revision) || string.Equals(revision, current.Value.Revision, StringComparison.Ordinal))
            return current;
        var historical = _manifests.GetByRevision(revision);
        return historical.IsSuccess && string.Equals(historical.Value.ConfigName, configName, StringComparison.OrdinalIgnoreCase)
            ? historical
            : Result<ContentManifest>.Failure($"Scenario content revision is not available: {revision}");
    }

    private Result<HashSet<string>> ResolveAllowedCards(
        IReadOnlyList<string> poolIds,
        string configName,
        string contentRevision)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var runtimeResult = _contentRuntimes?.Resolve(contentRevision, configName);
        if (runtimeResult is { IsFailure: true })
            return Result<HashSet<string>>.Failure(runtimeResult.Error);

        foreach (var poolId in poolIds)
        {
            if (runtimeResult is { IsSuccess: true })
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
                continue;
            }

            var pool = _cardPools.ResolvePool(poolId, configName);
            if (pool.IsFailure)
                return Result<HashSet<string>>.Failure(pool.Error);
            ids.UnionWith(pool.Value.Cards.Select(card => card.CardId));
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
                configName,
                () => _enemyPools.Get(poolId, configName));
            if (pool.IsFailure)
                return Result<HashSet<string>>.Failure(pool.Error);
            ids.UnionWith(pool.Value.EntityDefinitionIds);
        }
        return Result<HashSet<string>>.Success(ids);
    }

    private Result<CombatEntity> MaterializeParticipant(
        EntityCombatAdapter factory,
        string alias,
        string definitionId,
        bool hero,
        string contentRevision,
        string configName)
    {
        var definition = _contentRuntimes == null
            ? _entities.LoadDefinition(definitionId)
            : _entities.LoadDefinition(definitionId, contentRevision, configName);
        if (definition.IsFailure)
            return Result<CombatEntity>.Failure(definition.Error);
        var entity = factory.CreateCombatEntityFromDefinition(
            alias,
            definition.Value,
            contentRevision,
            configName);
        if (entity.IsHero != hero)
            return Result<CombatEntity>.Failure($"Entity definition has an invalid scenario role: {definitionId}");
        return Result<CombatEntity>.Success(entity);
    }

    private Result<T> GetContent<T>(
        string kind,
        string id,
        string contentRevision,
        string? configName,
        Func<Result<T>> fallback)
    {
        if (_contentRuntimes == null)
            return fallback();

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        return runtime.IsFailure
            ? Result<T>.Failure(runtime.Error)
            : runtime.Value.GetDefinition<T>(kind, id);
    }

    private static Result<CombatEntity> ApplyResources(
        CombatEntity participant,
        IReadOnlyDictionary<string, float> overrides)
    {
        var current = participant;
        foreach (var (resourceId, value) in overrides.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return Result<CombatEntity>.Failure($"Resource override must be finite: {resourceId}");
            var resource = current.GetResource(resourceId);
            if (resource == null)
                return Result<CombatEntity>.Failure($"Resource is not present on scenario hero: {resourceId}");
            current = current.UpdateResource(resourceId, resource.Set(value));
        }
        return Result<CombatEntity>.Success(current);
    }
}
