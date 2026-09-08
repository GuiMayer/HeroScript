using Core.Abstractions.Persistence;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.LegalActions;
using Core.Combat.Modifiers;
using Core.Config;
using Core.Content;
using Core.Events;
using Core.Effects;
using Core.Resources;
using Core.Run.Content;

namespace Core.Run.Runtime;

public enum GameplayPersistenceMode
{
    Authoritative,
    Ephemeral
}

public sealed record GameplayRuntimeOptions(
    GameplayPersistenceMode PersistenceMode,
    IRunCommitStore? CommitStore = null,
    IOperationalEventBus? OperationalTelemetry = null,
    IRunCommitReader? HistoryReader = null);

public sealed record GameplayRuntime(
    RunManager Runs,
    RunSessionCoordinator RunCommands,
    CombatRunCoordinator Combats,
    GameplayCommandGateway Gateway,
    IReadOnlyList<string> RegisteredCommandTypes);

public interface IGameplayRuntimeFactory
{
    GameplayRuntime Create(GameplayRuntimeOptions options);
    IReadOnlyList<string> RegisteredCommandTypes { get; }
}

/// <summary>
/// The sole composition root for stateful gameplay runtime services. Live,
/// replay and simulation differ only in persistence and operational sinks.
/// </summary>
public sealed class GameplayRuntimeFactory : IGameplayRuntimeFactory
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ICardPoolResolver _cardPools;
    private readonly ICardContentCatalog _cards;
    private readonly IPinnedContentCatalog<ScriptModifierDefinition> _modifiers;
    private readonly IContentManifestProvider _manifests;
    private readonly IResourceCatalog<RelicDefinition> _relics;
    private readonly IResourceCatalog<CardUpgradeDefinition> _upgrades;
    private readonly IResourceCatalog<GameModeDefinition> _modes;
    private readonly IGameModeResolver _modeResolver;
    private readonly IContentPublicationService _publications;
    private readonly IContentRuntimeResolver _contentRuntimes;
    private readonly IResourceManager _resources;
    private readonly ICombatFactory _combatFactory;
    private readonly ICombatFlowPlanner _flow;
    private readonly IDecisionPolicyRegistry _decisions;
    private readonly ILegalActionResolver _legalActions;
    private readonly IEffectTriggerExecutor _effectTriggers;
    private readonly IGameEventContextAccessor _eventContext;
    private readonly IGameplayCommandCodec _codec;
    private readonly IReadOnlyList<string> _registeredCommandTypes;

    public GameplayRuntimeFactory(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ICardPoolResolver cardPools,
        ICardContentCatalog cards,
        IPinnedContentCatalog<ScriptModifierDefinition> modifiers,
        IContentManifestProvider manifests,
        IResourceCatalog<RelicDefinition> relics,
        IResourceCatalog<CardUpgradeDefinition> upgrades,
        IResourceCatalog<GameModeDefinition> modes,
        IGameModeResolver modeResolver,
        IContentPublicationService publications,
        IContentRuntimeResolver contentRuntimes,
        IResourceManager resources,
        ICombatFactory combatFactory,
        ICombatFlowPlanner flow,
        IDecisionPolicyRegistry decisions,
        ILegalActionResolver legalActions,
        IEffectTriggerExecutor effectTriggers,
        IGameEventContextAccessor eventContext,
        IGameplayCommandCodec codec)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _cardPools = cardPools ?? throw new ArgumentNullException(nameof(cardPools));
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
        _manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
        _relics = relics ?? throw new ArgumentNullException(nameof(relics));
        _upgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));
        _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        _modeResolver = modeResolver ?? throw new ArgumentNullException(nameof(modeResolver));
        _publications = publications ?? throw new ArgumentNullException(nameof(publications));
        _contentRuntimes = contentRuntimes ?? throw new ArgumentNullException(nameof(contentRuntimes));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _combatFactory = combatFactory ?? throw new ArgumentNullException(nameof(combatFactory));
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        _legalActions = legalActions ?? throw new ArgumentNullException(nameof(legalActions));
        _effectTriggers = effectTriggers ?? throw new ArgumentNullException(nameof(effectTriggers));
        _eventContext = eventContext ?? throw new ArgumentNullException(nameof(eventContext));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _registeredCommandTypes = codec.Descriptors
            .Select(descriptor => descriptor.Type)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> RegisteredCommandTypes => _registeredCommandTypes;

    public GameplayRuntime Create(GameplayRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PersistenceMode == GameplayPersistenceMode.Authoritative && options.CommitStore == null)
            throw new InvalidOperationException("Authoritative gameplay runtime requires a commit store");
        if (options.PersistenceMode == GameplayPersistenceMode.Ephemeral && options.CommitStore != null)
            throw new InvalidOperationException("Ephemeral gameplay runtime cannot write authoritative commits");

        var sessionGates = new RunSessionGateProvider();
        var activities = RunActivityRegistry.CreateDefault();
        var activityEffects = new RunActivityEffectExecutor(_effectTriggers);
        var runs = new RunManager(
            _configManager,
            _resourceLoader,
            _cardPools,
            _cards,
            _modifiers,
            options.OperationalTelemetry,
            options.CommitStore,
            _manifests,
            _relics,
            _upgrades,
            _modes,
            _modeResolver,
            _publications,
            _contentRuntimes,
            _resources,
            options.HistoryReader ?? options.CommitStore,
            sessionGates,
            activities,
            activityEffects);
        RunManager CreatePlanningEngine() => new(
            _configManager,
            _resourceLoader,
            _cardPools,
            _cards,
            _modifiers,
            eventBus: null,
            repository: null,
            _manifests,
            _relics,
            _upgrades,
            _modes,
            _modeResolver,
            _publications,
            _contentRuntimes,
            _resources,
            options.HistoryReader ?? options.CommitStore,
            activities: activities,
            activityEffects: activityEffects);
        var runCommands = new RunSessionCoordinator(
            runs,
            _codec,
            RunCommandHandlers.Create(_codec, CreatePlanningEngine, activities),
            state =>
            {
                var published = runs.HydrateForReplay(state);
                if (published.IsFailure)
                    throw new InvalidOperationException(published.Error);
            },
            options.CommitStore,
            options.CommitStore,
            runs,
            sessionGates);
        var combats = new CombatRunCoordinator(
            _combatFactory,
            runs,
            _flow,
            _decisions,
            _legalActions,
            options.OperationalTelemetry);
        var gateway = new GameplayCommandGateway(runCommands, runs, combats, _eventContext, _codec);
        return new GameplayRuntime(runs, runCommands, combats, gateway, _registeredCommandTypes);
    }
}
