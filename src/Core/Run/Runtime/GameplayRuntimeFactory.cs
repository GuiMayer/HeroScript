using Core.Abstractions.Persistence;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.Modifiers;
using Core.Config;
using Core.Content;
using Core.Events;
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
    IOperationalEventBus? OperationalTelemetry = null);

public sealed record GameplayRuntime(
    RunManager Runs,
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
    private readonly ICardPlayExecutor _cardPlay;
    private readonly ICombatFlowPlanner _flow;
    private readonly IGambitEngine _gambits;
    private readonly IAbilityExecutor _abilities;
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
        ICardPlayExecutor cardPlay,
        ICombatFlowPlanner flow,
        IGambitEngine gambits,
        IAbilityExecutor abilities,
        IGameEventContextAccessor eventContext,
        IGameplayCommandCodec codec)
    {
        _configManager = configManager;
        _resourceLoader = resourceLoader;
        _cardPools = cardPools;
        _cards = cards;
        _modifiers = modifiers;
        _manifests = manifests;
        _relics = relics;
        _upgrades = upgrades;
        _modes = modes;
        _modeResolver = modeResolver;
        _publications = publications;
        _contentRuntimes = contentRuntimes;
        _resources = resources;
        _combatFactory = combatFactory;
        _cardPlay = cardPlay;
        _flow = flow;
        _gambits = gambits;
        _abilities = abilities;
        _eventContext = eventContext;
        _codec = codec;
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
            _resources);
        var combats = new CombatRunCoordinator(
            _combatFactory,
            runs,
            _cardPlay,
            _flow,
            _gambits,
            options.OperationalTelemetry,
            _abilities);
        var gateway = new GameplayCommandGateway(runs, runs, combats, _eventContext, _codec);
        return new GameplayRuntime(runs, combats, gateway, _registeredCommandTypes);
    }
}
