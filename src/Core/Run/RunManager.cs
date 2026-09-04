using Core.Abstractions.Persistence;
using Core.Common;
using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Combat.Flow;
using Core.Config;
using Core.Content;
using Core.Events;
using Core.Events.Domain;
using Core.Determinism;
using Core.Run.Content;
using Core.Run.Sandbox;
using Core.StatusEffects;
using Core.Resources;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Run;

public sealed class RunManager : IRunManager, IRunCommandProcessor, IRunCombatResolutionCommitter
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ICardPoolResolver? _cardPoolResolver;
    private readonly ICardContentCatalog? _cardContentCatalog;
    private readonly IScriptModifierManager? _scriptModifierManager;
    private readonly IEventBus? _eventBus;
    private readonly IRunStateRepository? _repository;
    private readonly IContentManifestProvider? _contentManifestProvider;
    private readonly IContentPublicationService? _contentPublications;
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private readonly IResourceManager? _resources;
    private readonly IResourceCatalog<RelicDefinition>? _relicCatalog;
    private readonly IResourceCatalog<CardUpgradeDefinition>? _cardUpgradeCatalog;
    private readonly IResourceCatalog<GameModeDefinition>? _modeCatalog;
    private readonly IGameModeResolver? _gameModeResolver;
    private readonly Dictionary<Guid, RunState> _runs = new();
    private readonly Dictionary<(Guid RunId, Guid CommandId), RunCommandReceipt> _commandReceipts = new();
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly AsyncLocal<RunCommand?> _executingCommand = new();

    public RunManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ICardPoolResolver? cardPoolResolver = null,
        ICardContentCatalog? cardContentCatalog = null,
        IScriptModifierManager? scriptModifierManager = null,
        IEventBus? eventBus = null,
        IRunStateRepository? repository = null,
        IContentManifestProvider? contentManifestProvider = null,
        IResourceCatalog<RelicDefinition>? relicCatalog = null,
        IResourceCatalog<CardUpgradeDefinition>? cardUpgradeCatalog = null,
        IResourceCatalog<GameModeDefinition>? modeCatalog = null,
        IGameModeResolver? gameModeResolver = null,
        IContentPublicationService? contentPublications = null,
        IContentRuntimeResolver? contentRuntimes = null,
        IResourceManager? resources = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _cardPoolResolver = cardPoolResolver;
        _cardContentCatalog = cardContentCatalog;
        _scriptModifierManager = scriptModifierManager;
        _eventBus = eventBus;
        _repository = repository;
        _contentManifestProvider = contentManifestProvider;
        _contentPublications = contentPublications;
        _contentRuntimes = contentRuntimes;
        _resources = resources;
        _relicCatalog = relicCatalog;
        _cardUpgradeCatalog = cardUpgradeCatalog;
        _modeCatalog = modeCatalog;
        _gameModeResolver = gameModeResolver;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public Result<RunState> StartRun(string configName = "default", string runDefinitionId = "default_run", string playerEntityId = "player")
    {
        return StartRun(new RunStartOptions(configName, runDefinitionId, playerEntityId));
    }

    public Result<RunState> StartRun(RunStartOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConfigName))
            return Result<RunState>.Failure("Config name is required");
        if (string.IsNullOrWhiteSpace(options.RunDefinitionId))
            return Result<RunState>.Failure("Run definition id is required");
        if (string.IsNullOrWhiteSpace(options.PlayerEntityId))
            return Result<RunState>.Failure("Player entity id is required");

        ResolvedContentManifest? resolvedContent = null;
        if (_contentManifestProvider != null)
        {
            var earlyManifest = ResolveContentManifest(options, definition: null);
            if (earlyManifest.IsFailure)
                return Result<RunState>.Failure(earlyManifest.Error);
            resolvedContent = earlyManifest.Value;
        }

        var effectiveRunDefinitionId = options.RunDefinitionId;
        ResolvedGameMode? resolvedMode = null;
        if (!string.IsNullOrWhiteSpace(options.ModeId))
        {
            var mode = _gameModeResolver switch
            {
                IRevisionedGameModeResolver revisioned when resolvedContent != null =>
                    revisioned.Resolve(options.ModeId, options.ConfigName, resolvedContent.Revision),
                null => ResolveLegacyMode(options.ModeId, options.ConfigName),
                _ => _gameModeResolver.Resolve(options.ModeId, options.ConfigName)
            };
            if (mode.IsFailure)
                return Result<RunState>.Failure(mode.Error);
            resolvedMode = mode.Value;
            if (!resolvedMode.Definition.AllowCustomSeed && options.Seed.HasValue && string.IsNullOrWhiteSpace(options.ChallengeId))
                return Result<RunState>.Failure($"Game mode does not allow a custom seed: {options.ModeId}");
            if (!string.IsNullOrWhiteSpace(resolvedMode.Definition.RunDefinitionId))
                effectiveRunDefinitionId = resolvedMode.Definition.RunDefinitionId;
        }

        var definitionResult = LoadDefinition(
            options.ConfigName,
            effectiveRunDefinitionId,
            resolvedContent?.Revision);
        if (definitionResult.IsFailure)
            return Result<RunState>.Failure(definitionResult.Error);

        var definition = definitionResult.Value;
        var mapResult = RunMapTransitions.Create(definition.MapNodes);
        if (mapResult.IsFailure)
            return Result<RunState>.Failure(mapResult.Error);

        if (resolvedContent == null)
        {
            var manifestResult = ResolveContentManifest(options, definition);
            if (manifestResult.IsFailure)
                return Result<RunState>.Failure(manifestResult.Error);
            resolvedContent = manifestResult.Value;
        }

        var manifest = resolvedContent.Manifest;
        var contentRevision = resolvedContent.Revision;
        var seed = options.Seed ?? CreateSeed();
        var context = DeterministicContext.Create(seed, contentRevision!);
        var runId = context.AllocateId(
            $"run:{options.ConfigName}:{effectiveRunDefinitionId}:{options.PlayerEntityId}:" +
            $"{options.ModeId ?? "default"}:{options.ChallengeId ?? "none"}:{options.AttemptKey ?? "default"}");
        context = runId.Context;

        var startingDeck = options.StartingDeck ?? definition.StartingDeck
            .Select(cardId => new RunStartingCard { DefinitionId = cardId })
            .ToArray();
        if (startingDeck.Count == 0)
            return Result<RunState>.Failure("Starting deck cannot be empty");
        var deckResult = DeckTransitions.Create(startingDeck, context);
        if (deckResult.IsFailure)
            return Result<RunState>.Failure(deckResult.Error);
        context = deckResult.Value.Context;

        var state = new RunState
        {
            RunId = runId.Value,
            ConfigName = options.ConfigName,
            PlayerEntityId = options.PlayerEntityId,
            ModeId = options.ModeId,
            ResolvedMode = resolvedMode,
            ChallengeId = options.ChallengeId,
            Scenario = options.Scenario,
            ScenarioHash = options.ScenarioHash,
            AttemptKey = options.AttemptKey,
            ResourceState = null!,
            CurrentNodeId = definition.MapNodes.FirstOrDefault()?.NodeId,
            Map = mapResult.Value,
            Deck = deckResult.Value.State,
            Metadata = ToImmutableMetadata(definition.Metadata),
            ContentManifest = manifest,
            Determinism = context
        };
        var runResources = CreateRunResources(
            state.RunId,
            definition.StartingResources,
            contentRevision!,
            options.ConfigName);
        if (runResources.IsFailure)
            return Result<RunState>.Failure(runResources.Error);
        state = state with { ResourceState = runResources.Value };

        var startingHandSize = options.StartingHandSize ?? definition.StartingHandSize;
        if (startingHandSize < 0)
            return Result<RunState>.Failure("Starting hand size cannot be negative");
        var initialDraw = DeckTransitions.Draw(state.Deck, startingHandSize, state.Determinism);
        if (initialDraw.IsFailure)
            return Result<RunState>.Failure(initialDraw.Error);

        state = state with
        {
            Deck = initialDraw.Value.State,
            Determinism = initialDraw.Value.Context.AdvanceStep()
        };

        lock (_lock)
        {
            if (_runs.ContainsKey(state.RunId))
                return Result<RunState>.Failure($"Run already exists for the deterministic inputs: {state.RunId}");

            var persisted = Persist(
                state,
                "run.start",
                options with
                {
                    RunDefinitionId = effectiveRunDefinitionId,
                    Seed = seed,
                    ContentRevision = contentRevision,
                    StartingDeck = startingDeck,
                    StartingHandSize = startingHandSize,
                    Scenario = options.Scenario
                });
            if (persisted.IsFailure)
                return Result<RunState>.Failure(persisted.Error);
            state = persisted.Value;
        }

        _eventBus?.Publish(new RunStartedEvent(
            state.RunId,
            options.ConfigName,
            options.PlayerEntityId,
            state.ResourceState.Resources.ToDictionary(pair => pair.Key, pair => pair.Value.Current)));
        if (!initialDraw.Value.Cards.IsEmpty)
            _eventBus?.Publish(new CardDrawnEvent(state.RunId, initialDraw.Value.Cards));

        return Result<RunState>.Success(state);
    }

    private Result<ResolvedGameMode> ResolveLegacyMode(string modeId, string configName)
    {
        if (_modeCatalog == null)
            return Result<ResolvedGameMode>.Failure("Game mode content catalog is not configured");

        var mode = _modeCatalog.Get(modeId, configName);
        if (mode.IsFailure)
            return Result<ResolvedGameMode>.Failure(mode.Error);
        if (!string.Equals(mode.Value.ModeId, modeId, StringComparison.Ordinal))
            return Result<ResolvedGameMode>.Failure($"Game mode definition identity mismatch: {modeId}");

        // Isolated consumers that predate policy catalogs retain the historical
        // behavior. Production composition always uses IGameModeResolver.
        return Result<ResolvedGameMode>.Success(new ResolvedGameMode
        {
            Definition = mode.Value
        });
    }

    private Result<ResolvedContentManifest> ResolveContentManifest(
        RunStartOptions options,
        RunDefinition? definition)
    {
        if (_contentManifestProvider == null)
        {
            // Compatibility boundary for isolated callers that have not registered
            // the content catalog. The production composition always supplies it.
            if (definition == null)
                return Result<ResolvedContentManifest>.Failure("Run definition is required without a content manifest provider");
            var revision = string.IsNullOrWhiteSpace(options.ContentRevision)
                ? CanonicalJson.ComputeHash(definition, _jsonOptions)
                : options.ContentRevision;
            return Result<ResolvedContentManifest>.Success(
                new ResolvedContentManifest(revision!, null));
        }

        var manifestResult = _contentManifestProvider.GetManifest(options.ConfigName);
        if (manifestResult.IsFailure)
            return Result<ResolvedContentManifest>.Failure(manifestResult.Error);

        var manifest = manifestResult.Value;
        if (!string.IsNullOrWhiteSpace(options.ContentRevision) &&
            !string.Equals(options.ContentRevision, manifest.Revision, StringComparison.Ordinal))
        {
            var historical = ResolveKnownManifest(options.ContentRevision);
            if (historical.IsFailure ||
                !string.Equals(historical.Value.ConfigName, options.ConfigName, StringComparison.OrdinalIgnoreCase))
            {
                return Result<ResolvedContentManifest>.Failure(
                    $"Requested content revision '{options.ContentRevision}' is not active or published for configuration " +
                    $"'{options.ConfigName}'. Active revision: {manifest.Revision}");
            }

            manifest = historical.Value;
        }

        return Result<ResolvedContentManifest>.Success(
            new ResolvedContentManifest(manifest.Revision, manifest));
    }

    private Result<ContentManifest> ResolveKnownManifest(string revision)
    {
        if (_contentManifestProvider == null)
            return Result<ContentManifest>.Failure("Content manifest provider is not configured");

        var known = _contentManifestProvider.GetByRevision(revision);
        if (known is { IsSuccess: true })
            return known;

        if (_contentPublications == null)
            return known ?? Result<ContentManifest>.Failure($"Content revision not found: {revision}");

        var published = _contentPublications.GetPublishedAsync(revision).GetAwaiter().GetResult();
        return published.IsSuccess
            ? Result<ContentManifest>.Success(published.Value.Manifest)
            : Result<ContentManifest>.Failure(published.Error);
    }

    private sealed record ResolvedContentManifest(string Revision, ContentManifest? Manifest);

    public Result<RunState> GetRun(Guid runId)
    {
        lock (_lock)
        {
            if (_runs.TryGetValue(runId, out var state))
                return Result<RunState>.Success(state);
        }

        // Cache miss — try loading from repository
        if (_repository != null)
        {
            var loaded = _repository.LoadLatestAsync(runId).GetAwaiter().GetResult();
            if (loaded != null)
            {
                var compatibility = ValidateLoadedRunCompatibility(loaded);
                if (compatibility.IsFailure)
                    return Result<RunState>.Failure(compatibility.Error);

                lock (_lock) { _runs[runId] = loaded; }
                return Result<RunState>.Success(loaded);
            }
        }

        return Result<RunState>.Failure($"Run not found: {runId}");
    }

    public Result<IReadOnlyList<RunAvailableCommand>> GetAvailableCommands(Guid runId)
    {
        var run = GetRun(runId);
        return run.IsFailure
            ? Result<IReadOnlyList<RunAvailableCommand>>.Failure(run.Error)
            : Result<IReadOnlyList<RunAvailableCommand>>.Success(
                RunMapTransitions.GetAvailableCommands(run.Value));
    }

    public Result<RunCommandReceipt?> FindReceipt(Guid runId, Guid commandId)
    {
        if (commandId == Guid.Empty)
            return Result<RunCommandReceipt?>.Failure("Command id is required");

        lock (_lock)
        {
            if (_commandReceipts.TryGetValue((runId, commandId), out var cached))
                return Result<RunCommandReceipt?>.Success(cached with { Duplicate = true });

            if (_repository is not IRunCheckpointRepository checkpoints)
                return Result<RunCommandReceipt?>.Success(null);

            try
            {
                var checkpoint = checkpoints.LoadCheckpointsAsync(runId)
                    .GetAwaiter().GetResult()
                    .LastOrDefault(item => item.JournalEntry.CommandId == commandId);
                if (checkpoint == null)
                    return Result<RunCommandReceipt?>.Success(null);

                var receipt = CreateReceipt(checkpoint.State, checkpoint.JournalEntry, duplicate: true);
                _commandReceipts[(runId, commandId)] = receipt with { Duplicate = false };
                return Result<RunCommandReceipt?>.Success(receipt);
            }
            catch (Exception exception)
            {
                return Result<RunCommandReceipt?>.Failure(
                    $"Failed to read command receipt {commandId}: {exception.Message}",
                    exception);
            }
        }
    }

    public Result<RunCommandReceipt> Execute(Guid runId, RunCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Identity);

        var identity = command.Identity with
        {
            Type = command.Identity.Type.Trim().ToUpperInvariant()
        };
        command = command with
        {
            Identity = identity,
            Payload = command.Payload.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(new { })
                : command.Payload.Clone()
        };

        if (identity.CommandId == Guid.Empty)
            return Result<RunCommandReceipt>.Failure("Command id is required");
        if (string.IsNullOrWhiteSpace(identity.Type))
            return Result<RunCommandReceipt>.Failure("Command type is required");

        lock (_lock)
        {
            var existingResult = FindReceipt(runId, identity.CommandId);
            if (existingResult.IsFailure)
                return Result<RunCommandReceipt>.Failure(existingResult.Error);
            if (existingResult.Value is { } existing)
            {
                return IsSameCommand(existing.JournalEntry, command)
                    ? Result<RunCommandReceipt>.Success(existing with { Duplicate = true })
                    : Result<RunCommandReceipt>.Failure(
                        $"Command id {identity.CommandId} was already used with a different envelope");
            }

            var runResult = GetRun(runId);
            if (runResult.IsFailure)
                return Result<RunCommandReceipt>.Failure(runResult.Error);
            if (runResult.Value.Sequence != identity.ExpectedSequence ||
                runResult.Value.Determinism.Step != identity.ExpectedStep)
            {
                return Result<RunCommandReceipt>.Failure(
                    RunCommandErrors.VersionConflict(
                        identity.ExpectedSequence,
                        identity.ExpectedStep,
                        runResult.Value));
            }

            var previousCommand = _executingCommand.Value;
            _executingCommand.Value = command;
            try
            {
                var operation = ExecuteCommandTransition(runId, identity.Type, command.Payload);
                if (operation.IsFailure)
                    return Result<RunCommandReceipt>.Failure(operation.Error);

                var receiptResult = FindReceipt(runId, identity.CommandId);
                if (receiptResult.IsFailure)
                    return Result<RunCommandReceipt>.Failure(receiptResult.Error);

                // Some valid deck operations are no-ops. They still need a durable
                // receipt so that a retry remains idempotent.
                if (receiptResult.Value == null)
                {
                    var current = _runs[runId];
                    var persisted = Persist(
                        current with { Determinism = current.Determinism.AdvanceStep() },
                        identity.Type,
                        command.Payload);
                    if (persisted.IsFailure)
                        return Result<RunCommandReceipt>.Failure(persisted.Error);

                    receiptResult = FindReceipt(runId, identity.CommandId);
                }

                return receiptResult.IsSuccess && receiptResult.Value != null
                    ? Result<RunCommandReceipt>.Success(receiptResult.Value with { Duplicate = false })
                    : Result<RunCommandReceipt>.Failure("Command completed without a durable receipt");
            }
            catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
            {
                return Result<RunCommandReceipt>.Failure($"Invalid payload for {identity.Type}: {exception.Message}");
            }
            finally
            {
                _executingCommand.Value = previousCommand;
            }
        }
    }

    private Result ExecuteCommandTransition(Guid runId, string commandType, JsonElement payload)
    {
        return commandType switch
        {
            RunCommandTypes.AdvanceNode => ToResult(AdvanceNode(
                runId,
                DeserializePayload<AdvanceNodePayload>(payload).TargetNodeId)),
            RunCommandTypes.ResolveNode => ToResult(ResolveCurrentNode(
                runId,
                DeserializePayload<ResolveNodePayload>(payload).CurrentNodeId)),
            RunCommandTypes.DrawCards => ToResult(DrawCards(
                runId,
                DeserializePayload<CountPayload>(payload).Count)),
            RunCommandTypes.DiscardCards => ToResult(DiscardCards(
                runId,
                DeserializePayload<CardIdsPayload>(payload).CardIds)),
            RunCommandTypes.ShuffleDiscard => ShuffleDiscardIntoDrawPile(runId),
            RunCommandTypes.CreateCardSelection => ExecuteCreateCardSelection(runId, payload),
            RunCommandTypes.PickCardReward => ExecutePickCardReward(runId, payload),
            RunCommandTypes.RerollCardReward => ExecuteRerollCardReward(runId, payload),
            RunCommandTypes.DecomposeCardReward => ExecuteDecomposeCardReward(runId, payload),
            RunCommandTypes.CreateShop => ExecuteCreateShop(runId, payload),
            RunCommandTypes.BuyShopItem => ExecuteBuyShopItem(runId, payload),
            RunCommandTypes.RerollShop => ExecuteRerollShop(runId, payload),
            RunCommandTypes.CreatePreparation => ExecuteCreatePreparation(runId, payload),
            RunCommandTypes.ApplyPreparationOption => ExecutePreparationOption(runId, payload),
            RunCommandTypes.AcquireRelic => ExecuteAcquireRelic(runId, payload),
            RunCommandTypes.RemoveRelic => ExecuteRemoveRelic(runId, payload),
            RunCommandTypes.UpgradeCard => ExecuteUpgradeCard(runId, payload),
            RunCommandTypes.ActivateContentRevision => ExecuteActivateContentRevision(runId, payload),
            RunCommandTypes.ResolveCombat => Result.Failure(
                "RESOLVE_COMBAT must be executed through the run encounter coordinator"),
            RunCommandTypes.RestoreCheckpoint => ExecuteRestoreCheckpoint(runId, payload),
            RunCommandTypes.StartEncounter => Result.Failure(
                "START_ENCOUNTER must be executed through the run encounter coordinator"),
            _ => Result.Failure($"Unsupported run command type: {commandType}")
        };
    }

    private Result ExecuteCreateCardSelection(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<CardSelectionPayload>(payload);
        return ToResult(CreateCardSelection(runId, request.SelectionId));
    }

    private Result ExecutePickCardReward(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<CardSelectionCardsPayload>(payload);
        return ToResult(PickCards(runId, request.SelectionInstanceId, request.CardIds));
    }

    private Result ExecuteRerollCardReward(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<RerollCardSelectionPayload>(payload);
        return ToResult(RerollCardSelection(runId, request.SelectionInstanceId, request.LockedCardIds));
    }

    private Result ExecuteDecomposeCardReward(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<CardSelectionItemPayload>(payload);
        return ToResult(DecomposeCardSelectionOption(runId, request.SelectionInstanceId, request.CardId));
    }

    private Result ExecuteCreateShop(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<ShopDefinitionPayload>(payload);
        return ToResult(CreateShop(runId, request.ShopId));
    }

    private Result ExecuteBuyShopItem(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<ShopItemPayload>(payload);
        return ToResult(BuyShopItem(runId, request.ShopInstanceId, request.ItemId));
    }

    private Result ExecuteRerollShop(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<ShopPayload>(payload);
        return ToResult(RerollShop(runId, request.ShopInstanceId));
    }

    private Result ExecuteCreatePreparation(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<PreparationDefinitionPayload>(payload);
        return ToResult(CreatePreparation(runId, request.PreparationId));
    }

    private Result ExecutePreparationOption(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<PreparationPayload>(payload);
        return ToResult(ApplyPreparationOption(runId, request.PreparationInstanceId, request.OptionId));
    }

    private Result ExecuteAcquireRelic(Guid runId, JsonElement payload)
    {
        if (_relicCatalog == null)
            return Result.Failure("Relic content catalog is not configured");
        var request = DeserializePayload<RelicPayload>(payload);
        var run = _runs[runId];
        var definition = GetContentDefinition(
            run,
            "relics",
            request.RelicId,
            () => _relicCatalog.Get(request.RelicId, run.ConfigName));
        if (definition.IsFailure)
            return Result.Failure(definition.Error);
        if (!string.Equals(definition.Value.RelicId, request.RelicId, StringComparison.Ordinal))
            return Result.Failure($"Relic definition identity mismatch: {request.RelicId}");

        var transition = RelicTransitions.Acquire(run, definition.Value);
        if (transition.IsFailure)
            return Result.Failure(transition.Error);
        return ToResult(Persist(
            transition.Value.State,
            RunCommandTypes.AcquireRelic,
            new { relicId = request.RelicId }));
    }

    private Result ExecuteRemoveRelic(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<RelicInstancePayload>(payload);
        var transition = RelicTransitions.Remove(_runs[runId], request.RelicInstanceId);
        if (transition.IsFailure)
            return Result.Failure(transition.Error);
        return ToResult(Persist(
            transition.Value,
            RunCommandTypes.RemoveRelic,
            new { relicInstanceId = request.RelicInstanceId }));
    }

    private Result ExecuteUpgradeCard(Guid runId, JsonElement payload)
    {
        if (_cardUpgradeCatalog == null)
            return Result.Failure("Card upgrade content catalog is not configured");
        var request = DeserializePayload<CardUpgradePayload>(payload);
        var run = _runs[runId];
        var currentNode = run.Map.Nodes.FirstOrDefault(node =>
            string.Equals(node.NodeId, run.CurrentNodeId, StringComparison.Ordinal));
        var nodeType = currentNode?.NodeType.ToLowerInvariant();
        if (currentNode == null || nodeType is not ("upgrade" or "card_upgrade" or "rest" or "forge"))
            return Result.Failure("The current map node does not allow card upgrades");
        if (run.Map.ResolvedNodeIds.Contains(currentNode.NodeId, StringComparer.Ordinal))
            return Result.Failure($"Map node already resolved: {currentNode.NodeId}");
        var definition = GetContentDefinition(
            run,
            "card-upgrades",
            request.UpgradeId,
            () => _cardUpgradeCatalog.Get(request.UpgradeId, run.ConfigName));
        if (definition.IsFailure)
            return Result.Failure(definition.Error);
        if (!string.Equals(definition.Value.UpgradeId, request.UpgradeId, StringComparison.Ordinal))
            return Result.Failure($"Card upgrade definition identity mismatch: {request.UpgradeId}");

        var transition = DeckTransitions.ApplyUpgrade(
            run.Deck,
            request.CardInstanceId,
            definition.Value,
            run.Determinism);
        if (transition.IsFailure)
            return Result.Failure(transition.Error);
        var candidate = run with
        {
            Deck = transition.Value.State,
            Determinism = transition.Value.Context.AdvanceStep()
        };
        return ToResult(Persist(
            candidate,
            RunCommandTypes.UpgradeCard,
            new { request.CardInstanceId, request.UpgradeId }));
    }

    private Result ExecuteRestoreCheckpoint(Guid runId, JsonElement payload)
    {
        if (_repository == null)
            return Result.Failure("Run persistence is not configured");

        var request = DeserializePayload<CheckpointPayload>(payload);
        var state = _repository.LoadAsync(runId, request.Sequence).GetAwaiter().GetResult();
        return state == null
            ? Result.Failure($"Run checkpoint not found: {runId}/{request.Sequence}")
            : ToResult(RestoreState(state));
    }

    private Result ExecuteActivateContentRevision(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<ContentRevisionPayload>(payload);
        if (string.IsNullOrWhiteSpace(request.Revision))
            return Result.Failure("Content revision is required");
        if (!_runs.TryGetValue(runId, out var state))
            return Result.Failure($"Run not found: {runId}");

        var mode = state.ResolvedMode;
        if (mode == null)
            return Result.Failure("Content revision activation requires a resolved game mode");
        if (!mode.CapabilityPolicy.AllowHotReloadActivation)
            return Result.Failure($"Game mode does not allow content revision activation: {state.ModeId}");
        if (!string.Equals(
                mode.ContentBindingPolicy.ActiveRuns,
                "allow_versioned_activation",
                StringComparison.Ordinal))
        {
            return Result.Failure("Game mode pins content for active runs");
        }
        if (!string.Equals(
                mode.ContentBindingPolicy.ActivationBoundary,
                "next_command",
                StringComparison.Ordinal))
        {
            return Result.Failure("Unsupported content activation boundary");
        }

        var manifest = ResolveKnownManifest(request.Revision.Trim());
        if (manifest.IsFailure)
            return Result.Failure(manifest.Error);
        if (!string.Equals(manifest.Value.ConfigName, state.ConfigName, StringComparison.OrdinalIgnoreCase))
            return Result.Failure("Content revision belongs to a different configuration");
        if (string.Equals(state.Determinism.ContentRevision, manifest.Value.Revision, StringComparison.Ordinal))
            return Result.Success();

        if (_contentRuntimes != null)
        {
            var targetRuntime = _contentRuntimes.Resolve(manifest.Value.Revision, state.ConfigName);
            if (targetRuntime.IsFailure)
                return Result.Failure($"Target content revision is unavailable: {targetRuntime.Error}");
            var compatibility = RunContentCompatibilityValidator.ValidateForActivation(
                state,
                targetRuntime.Value);
            if (compatibility.IsFailure)
                return Result.Failure(compatibility.Error);
        }

        var activatedMode = state.ResolvedMode;
        if (!string.IsNullOrWhiteSpace(state.ModeId) &&
            _gameModeResolver is IRevisionedGameModeResolver revisionedModes)
        {
            var resolved = revisionedModes.Resolve(
                state.ModeId,
                state.ConfigName,
                manifest.Value.Revision);
            if (resolved.IsFailure)
                return Result.Failure($"Target content revision has an invalid game mode graph: {resolved.Error}");
            activatedMode = resolved.Value;
        }

        var encounters = state.Encounters
            .Select(encounter => encounter with
            {
                Combat = encounter.Combat with
                {
                    Determinism = encounter.Combat.Determinism.WithContentRevision(manifest.Value.Revision)
                }
            })
            .ToImmutableArray();
        var candidate = state with
        {
            ContentManifest = manifest.Value,
            ResolvedMode = activatedMode,
            Encounters = encounters,
            Determinism = state.Determinism
                .WithContentRevision(manifest.Value.Revision)
                .AdvanceStep()
        };
        return ToResult(Persist(
            candidate,
            RunCommandTypes.ActivateContentRevision,
            new { revision = manifest.Value.Revision }));
    }

    private T DeserializePayload<T>(JsonElement payload) where T : class
    {
        return payload.Deserialize<T>(_jsonOptions)
            ?? throw new JsonException($"Payload cannot be deserialized as {typeof(T).Name}");
    }

    private static Result ToResult<T>(Result<T> result) =>
        result.IsSuccess ? Result.Success() : Result.Failure(result.Error);

    public Result<RunState> GetRunByCombat(Guid combatId)
    {
        lock (_lock)
        {
            var cached = _runs.Values.FirstOrDefault(state => state.GetEncounter(combatId) != null);
            if (cached != null)
                return Result<RunState>.Success(cached);
        }

        if (_repository == null)
            return Result<RunState>.Failure($"Run-owned combat not found: {combatId}");

        try
        {
            var runIds = _repository.ListRunIdsAsync().GetAwaiter().GetResult();
            foreach (var runId in runIds.OrderBy(id => id))
            {
                var loaded = _repository.LoadLatestAsync(runId).GetAwaiter().GetResult();
                if (loaded?.GetEncounter(combatId) == null)
                    continue;

                var compatibility = ValidateLoadedRunCompatibility(loaded);
                if (compatibility.IsFailure)
                    return Result<RunState>.Failure(compatibility.Error);

                lock (_lock)
                {
                    _runs[runId] = loaded;
                }
                return Result<RunState>.Success(loaded);
            }

            return Result<RunState>.Failure($"Run-owned combat not found: {combatId}");
        }
        catch (Exception exception)
        {
            return Result<RunState>.Failure(
                $"Failed to recover run-owned combat {combatId}: {exception.Message}",
                exception);
        }
    }

    public Result<RunMapNodeState> ResolveCurrentNode(Guid runId, string currentNodeId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunMapNodeState>.Failure($"Run not found: {runId}");

            var current = state.Map.Nodes.FirstOrDefault(node =>
                string.Equals(node.NodeId, state.CurrentNodeId, StringComparison.Ordinal));
            if (current != null && RunMapTransitions.IsEncounterNode(current.NodeType))
            {
                return Result<RunMapNodeState>.Failure(
                    $"Encounter map nodes must be resolved through their combat: {current.NodeId}");
            }

            var transition = RunMapTransitions.Resolve(state, currentNodeId);
            return transition.IsFailure
                ? Result<RunMapNodeState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    RunCommandTypes.ResolveNode,
                    new { currentNodeId });
        }
    }

    public Result<RunMapNodeState> AdvanceNode(Guid runId, string targetNodeId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunMapNodeState>.Failure($"Run not found: {runId}");

            var transition = RunMapTransitions.Advance(state, targetNodeId);
            return transition.IsFailure
                ? Result<RunMapNodeState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    RunCommandTypes.AdvanceNode,
                    new { targetNodeId });
        }
    }

    public Result<RunState> AttachEncounter(
        Guid runId,
        int expectedSequence,
        ulong expectedStep,
        CombatState combatState,
        RunCommandIdentity? commandIdentity = null)
    {
        if (combatState == null)
            return Result<RunState>.Failure("Combat state is required");

        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunState>.Failure($"Run not found: {runId}");
            if (state.Sequence != expectedSequence || state.Determinism.Step != expectedStep)
            {
                return Result<RunState>.Failure(
                    $"Stale run encounter start: expected sequence/step {expectedSequence}/{expectedStep}, " +
                    $"current is {state.Sequence}/{state.Determinism.Step}");
            }
            if (state.GetActiveEncounter() != null)
                return Result<RunState>.Failure($"Run already has an active encounter: {state.ActiveEncounterId}");
            if (state.GetEncounter(combatState.CombatId) != null)
                return Result<RunState>.Failure($"Run encounter already exists: {combatState.CombatId}");
            if (state.CurrentNodeId == null)
                return Result<RunState>.Failure("Run has no current map node");

            var currentNode = state.Map.Nodes.FirstOrDefault(node =>
                string.Equals(node.NodeId, state.CurrentNodeId, StringComparison.Ordinal));
            if (currentNode == null)
                return Result<RunState>.Failure($"Map node not found: {state.CurrentNodeId}");
            if (!RunMapTransitions.IsEncounterNode(currentNode.NodeType))
                return Result<RunState>.Failure($"Current map node is not an encounter: {currentNode.NodeId}");
            if (state.Map.ResolvedNodeIds.Contains(currentNode.NodeId, StringComparer.Ordinal))
                return Result<RunState>.Failure($"Map node already resolved: {currentNode.NodeId}");

            var seed = state.Determinism.DrawUInt64();
            if (combatState.RunId != runId ||
                !string.Equals(combatState.RunNodeId, currentNode.NodeId, StringComparison.Ordinal))
            {
                return Result<RunState>.Failure("Combat ownership does not match the current run node");
            }
            if (combatState.Determinism.Seed != seed.Value)
                return Result<RunState>.Failure("Combat seed was not derived from the current run state");
            if (!string.Equals(
                    combatState.Determinism.ContentRevision,
                    state.Determinism.ContentRevision,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    combatState.Determinism.EngineVersion,
                    state.Determinism.EngineVersion,
                    StringComparison.Ordinal))
            {
                return Result<RunState>.Failure("Combat engine/content version does not match its run");
            }
            if (!combatState.IsActive)
                return Result<RunState>.Failure("A new run encounter must be active");

            var encounter = new RunEncounterState
            {
                NodeId = currentNode.NodeId,
                Combat = combatState
            };
            var encounterDeck = Result<DeckTransition>.Success(
                new DeckTransition(state.Deck, seed.Context, []));
            if (state.ResolvedMode?.CombatRules.Flow.DeckCycle is { } deckPolicy)
            {
                encounterDeck = DeckTransitions.BeginEncounter(
                    state.Deck,
                    deckPolicy,
                    seed.Context);
                if (encounterDeck.IsFailure)
                    return Result<RunState>.Failure(encounterDeck.Error);
            }
            var candidate = state with
            {
                ActiveEncounterId = combatState.CombatId,
                Encounters = state.Encounters.Add(encounter),
                Deck = encounterDeck.Value.State,
                Determinism = encounterDeck.Value.Context.AdvanceStep()
            };
            var initialEnergy = (int)(combatState.Hero.GetResource("energy")?.Current ?? 0f);
            var journalCommand = new RunEncounterStartCommand(
                combatState.Hero.EntityId,
                combatState.Enemies.Select(enemy => enemy.EntityId).ToArray(),
                initialEnergy,
                combatState.Hero,
                combatState.Enemies.ToArray(),
                combatState.StatusEffects.ToDictionary(
                    item => item.Key,
                    item => (IReadOnlyList<StatusEffectInstance>)item.Value.ToArray(),
                    StringComparer.Ordinal));
            return Persist(
                candidate,
                RunCommandTypes.StartEncounter,
                journalCommand,
                commandIdentity);
        }
    }

    public Result<RunState> CommitCombatAction(
        Guid runId,
        int expectedSequence,
        CombatState previousCombat,
        CombatState nextCombat,
        CombatActionCommand command,
        string? consumedCardId,
        CardConsumeDestination destination,
        RunCommandIdentity? commandIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(previousCombat);
        ArgumentNullException.ThrowIfNull(nextCombat);
        ArgumentNullException.ThrowIfNull(command);

        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunState>.Failure($"Run not found: {runId}");
            if (state.Sequence != expectedSequence)
            {
                return Result<RunState>.Failure(
                    $"Stale run combat action: expected sequence {expectedSequence}, current is {state.Sequence}");
            }

            var encounterIndex = FindEncounterIndex(state, previousCombat.CombatId);
            if (encounterIndex < 0 || state.ActiveEncounterId != previousCombat.CombatId)
                return Result<RunState>.Failure($"Active run encounter not found: {previousCombat.CombatId}");

            var encounter = state.Encounters[encounterIndex];
            if (!string.Equals(
                    CanonicalJson.ComputeHash(encounter.Combat),
                    CanonicalJson.ComputeHash(previousCombat),
                    StringComparison.Ordinal))
            {
                return Result<RunState>.Failure("Run-owned combat changed before the action could be committed");
            }
            if (nextCombat.CombatId != previousCombat.CombatId ||
                command.RunId != runId ||
                nextCombat.RunId != runId ||
                !string.Equals(nextCombat.RunNodeId, encounter.NodeId, StringComparison.Ordinal) ||
                nextCombat.Determinism.Seed != previousCombat.Determinism.Seed ||
                !string.Equals(
                    nextCombat.Determinism.ContentRevision,
                    state.Determinism.ContentRevision,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    nextCombat.Determinism.EngineVersion,
                    state.Determinism.EngineVersion,
                    StringComparison.Ordinal) ||
                nextCombat.Determinism.Step <= previousCombat.Determinism.Step)
            {
                return Result<RunState>.Failure("Invalid replacement combat state for run action");
            }

            var deck = state.Deck;
            if (destination != CardConsumeDestination.None)
            {
                if (string.IsNullOrWhiteSpace(consumedCardId))
                    return Result<RunState>.Failure("Consumed card id is required");

                var deckTransition = DeckTransitions.MoveFromHand(
                    state.Deck,
                    [consumedCardId],
                    destination,
                    state.Determinism);
                if (deckTransition.IsFailure)
                    return Result<RunState>.Failure(deckTransition.Error);
                deck = deckTransition.Value.State;
            }

            var rootPayload = JsonSerializer.SerializeToElement(new
            {
                combatId = nextCombat.CombatId,
                command,
                consumedCardId,
                destination = destination.ToString()
            }, _jsonOptions);
            return CommitCombatResolutionLocked(
                state,
                previousCombat,
                commandIdentity,
                [new CombatResolutionStep
                {
                    TransitionType = "combat.action.applied",
                    Combat = nextCombat,
                    Deck = deck,
                    Payload = rootPayload
                }],
                rootPayload);
        }
    }

    public Result<RunState> CommitCombatResolution(CombatResolutionCommit resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(resolution.PreviousCombat);
        ArgumentNullException.ThrowIfNull(resolution.RootCommand);
        if (resolution.Steps.Count == 0)
            return Result<RunState>.Failure("Combat resolution requires at least one transition");

        lock (_lock)
        {
            if (!_runs.TryGetValue(resolution.RunId, out var state))
                return Result<RunState>.Failure($"Run not found: {resolution.RunId}");
            if (state.Sequence != resolution.ExpectedSequence)
                return Result<RunState>.Failure(
                    $"Stale combat resolution: expected sequence {resolution.ExpectedSequence}, current is {state.Sequence}");
            return CommitCombatResolutionLocked(
                state,
                resolution.PreviousCombat,
                resolution.RootCommand,
                resolution.Steps,
                resolution.RootPayload);
        }
    }

    private Result<RunState> CommitCombatResolutionLocked(
        RunState state,
        CombatState previousCombat,
        RunCommandIdentity? rootCommand,
        IReadOnlyList<CombatResolutionStep> steps,
        JsonElement rootPayload = default)
    {
        var encounterIndex = FindEncounterIndex(state, previousCombat.CombatId);
        if (encounterIndex < 0 || state.ActiveEncounterId != previousCombat.CombatId)
            return Result<RunState>.Failure($"Active run encounter not found: {previousCombat.CombatId}");
        var encounter = state.Encounters[encounterIndex];
        if (!string.Equals(
                CanonicalJson.ComputeHash(encounter.Combat),
                CanonicalJson.ComputeHash(previousCombat),
                StringComparison.Ordinal))
            return Result<RunState>.Failure("Run-owned combat changed before the resolution could be committed");

        foreach (var step in steps)
        {
            if (step.Combat == null || step.Combat.CombatId != previousCombat.CombatId ||
                step.Combat.RunId != state.RunId ||
                !string.Equals(step.Combat.RunNodeId, encounter.NodeId, StringComparison.Ordinal) ||
                step.Combat.Determinism.Seed != previousCombat.Determinism.Seed ||
                !string.Equals(
                    step.Combat.Determinism.ContentRevision,
                    state.Determinism.ContentRevision,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(step.TransitionType))
                return Result<RunState>.Failure("Invalid transition in combat resolution");
            if (step.RunDeterminism != null &&
                (step.RunDeterminism.Seed != state.Determinism.Seed ||
                 !string.Equals(
                     step.RunDeterminism.ContentRevision,
                     state.Determinism.ContentRevision,
                     StringComparison.Ordinal) ||
                 step.RunDeterminism.Step < state.Determinism.Step))
                return Result<RunState>.Failure("Invalid run determinism in combat resolution");
        }

        var candidates = new List<(RunState State, CombatResolutionStep Step)>(steps.Count);
        var candidate = state;
        foreach (var step in steps)
        {
            candidate = candidate with
            {
                Deck = step.Deck,
                Encounters = candidate.Encounters.SetItem(
                    encounterIndex,
                    candidate.Encounters[encounterIndex] with { Combat = step.Combat }),
                Determinism = (step.RunDeterminism ?? candidate.Determinism).AdvanceStep()
            };
            candidates.Add((candidate, step));
        }

        if (rootCommand != null)
        {
            var mode = state.ResolvedMode?.CombatRules.Flow.Animation.Mode
                ?? AnimationFrameMode.FullSnapshots;
            var frames = candidates.Select((candidate, index) => new CombatAnimationFrame
            {
                FrameId = DeterministicId.Create(
                    state.Determinism.Seed,
                    (ulong)index,
                    $"combat-frame:{rootCommand.CommandId:N}"),
                Index = index,
                RunSequence = checked(state.Sequence + index + 1),
                CombatStep = candidate.Step.Combat.Determinism.Step,
                TransitionType = candidate.Step.TransitionType,
                Payload = candidate.Step.Payload.ValueKind == JsonValueKind.Undefined
                    ? JsonSerializer.SerializeToElement(new { }, _jsonOptions)
                    : candidate.Step.Payload.Clone(),
                StateAfter = mode == AnimationFrameMode.FullSnapshots
                    ? candidate.Step.Combat
                    : null,
                SnapshotSequence = checked(state.Sequence + index + 1)
            }).ToArray();
            var record = new CombatResolutionRecord
            {
                CommandId = rootCommand.CommandId,
                CombatId = previousCombat.CombatId,
                CommandType = rootCommand.Type,
                Mode = mode,
                FirstSequence = frames[0].RunSequence,
                FinalSequence = frames[^1].RunSequence,
                Frames = frames
            };
            var final = candidates[^1];
            candidates[^1] = (final.State with
            {
                CombatResolutions = final.State.CombatResolutions
                    .ToImmutableDictionary()
                    .SetItem(rootCommand.CommandId, record)
            }, final.Step);
        }

        return PersistBatch(state, candidates, rootCommand, rootPayload);
    }

    public Result<RunState> ResolveEncounter(
        Guid runId,
        int expectedSequence,
        Guid combatId,
        RunCommandIdentity? commandIdentity = null)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunState>.Failure($"Run not found: {runId}");
            if (state.Sequence != expectedSequence)
            {
                return Result<RunState>.Failure(
                    $"Stale combat resolution: expected sequence {expectedSequence}, current is {state.Sequence}");
            }

            var encounterIndex = FindEncounterIndex(state, combatId);
            if (encounterIndex < 0 || state.ActiveEncounterId != combatId)
                return Result<RunState>.Failure($"Active run encounter not found: {combatId}");

            var encounter = state.Encounters[encounterIndex];
            if (encounter.Combat.IsActive)
                return Result<RunState>.Failure($"Combat is still active: {combatId}");
            if (encounter.Resolved)
                return Result<RunState>.Failure($"Combat already resolved: {combatId}");
            if (!string.Equals(state.CurrentNodeId, encounter.NodeId, StringComparison.Ordinal))
                return Result<RunState>.Failure("Encounter no longer belongs to the current map node");

            var cleanedState = state;
            if (state.ResolvedMode?.CombatRules.Flow.DeckCycle is { } deckPolicy)
            {
                var cleanup = DeckTransitions.EndEncounter(
                    state.Deck,
                    deckPolicy,
                    state.Determinism);
                if (cleanup.IsFailure)
                    return Result<RunState>.Failure(cleanup.Error);
                cleanedState = state with
                {
                    Deck = cleanup.Value.State,
                    Determinism = cleanup.Value.Context
                };
            }

            var mapTransition = RunMapTransitions.Resolve(cleanedState, encounter.NodeId);
            if (mapTransition.IsFailure)
                return Result<RunState>.Failure(mapTransition.Error);

            var resolved = encounter with
            {
                Resolved = true,
                Outcome = encounter.Combat.Status.ToString()
            };
            var candidate = mapTransition.Value.State with
            {
                ActiveEncounterId = null,
                Encounters = state.Encounters.SetItem(encounterIndex, resolved)
            };
            return Persist(
                candidate,
                RunCommandTypes.ResolveCombat,
                new
                {
                    combatId,
                    nodeId = encounter.NodeId,
                    outcome = resolved.Outcome,
                    combatStateHash = CanonicalJson.ComputeHash(encounter.Combat)
                },
                commandIdentity);
        }
    }

    private Result ValidateLoadedRunCompatibility(RunState state)
    {
        var topology = DeckTransitions.ValidateTopology(state.Deck);
        if (topology.IsFailure)
            return topology;

        if (!string.Equals(
                state.Determinism.EngineVersion,
                DeterministicContext.CurrentEngineVersion,
                StringComparison.Ordinal))
        {
            return Result.Failure(
                $"Engine version unavailable for run {state.RunId}: " +
                $"{state.Determinism.EngineVersion}");
        }

        if (_contentManifestProvider == null)
            return Result.Success();

        var manifest = ResolveKnownManifest(state.Determinism.ContentRevision);
        if (manifest.IsFailure)
        {
            var current = _contentManifestProvider.GetManifest(state.ConfigName);
            if (current.IsSuccess &&
                string.Equals(current.Value.Revision, state.Determinism.ContentRevision, StringComparison.Ordinal))
            {
                manifest = current;
            }
        }
        if (manifest.IsFailure ||
            !string.Equals(manifest.Value.ConfigName, state.ConfigName, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(
                $"Content revision unavailable for run {state.RunId}: " +
                $"{state.Determinism.ContentRevision}");
        }

        return Result.Success();
    }

    public Result<RunState> ApplyRunResource(Guid runId, string resourceId, float amount)
    {
        float oldValue, newValue;
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunState>.Failure($"Run not found: {runId}");

            var pool = state.ResourceState.Get(resourceId);
            if (pool == null)
                return Result<RunState>.Failure($"Run resource not found: {resourceId}");
            oldValue = pool.Current;
            var changed = RunResourceTransitions.ApplyDelta(
                state.ResourceState,
                resourceId,
                amount,
                $"run-resource:{state.Sequence + 1}:{resourceId}");
            if (changed.IsFailure)
                return Result<RunState>.Failure(changed.Error);
            state = state with { ResourceState = changed.Value.State };
            newValue = state.ResourceState.Current(resourceId);

            state = state with { Determinism = state.Determinism.AdvanceStep() };
            var persisted = Persist(
                state,
                "run.resource.apply",
                new { resourceId, amount });
            if (persisted.IsFailure)
                return Result<RunState>.Failure(persisted.Error);
            state = persisted.Value;
            _eventBus?.Publish(new EconomyChangedEvent(runId, resourceId, oldValue, newValue));
            return Result<RunState>.Success(state);
        }
    }

    private Result<ResourceSet> CreateRunResources(
        Guid runId,
        IReadOnlyDictionary<string, float> startingResources,
        string contentRevision,
        string configName)
    {
        if (startingResources.Count == 0)
            return Result<ResourceSet>.Failure("Run definition requires at least one starting resource");
        if (_resources is not IRevisionedResourceManager revisioned)
            return Result<ResourceSet>.Failure("Revisioned resource manager is required to start a run");

        var pools = new Dictionary<string, ResourcePool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (resourceId, current) in startingResources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var pool = revisioned.CreatePool(resourceId, current, contentRevision, configName);
            if (pool.IsFailure)
                return Result<ResourceSet>.Failure(
                    $"Failed to create run resource '{resourceId}': {pool.Error}");
            pools.Add(resourceId, pool.Value);
        }
        return Result<ResourceSet>.Success(new ResourceSet
        {
            OwnerId = $"run:{runId}",
            Resources = pools
        });
    }

    public Result<IReadOnlyList<string>> DrawCards(Guid runId, int count)
    {
        ImmutableArray<string> drawn;
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.Draw(state.Deck, count, state.Determinism);
            if (transition.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(transition.Error);

            drawn = transition.Value.Cards;
            if (!drawn.IsEmpty)
            {
                state = state with
                {
                    Deck = transition.Value.State,
                    Determinism = transition.Value.Context.AdvanceStep()
                };
                var persisted = Persist(state, "run.deck.draw", new { count });
                if (persisted.IsFailure)
                    return Result<IReadOnlyList<string>>.Failure(persisted.Error);
            }
        }

        if (!drawn.IsEmpty)
            _eventBus?.Publish(new CardDrawnEvent(runId, drawn));

        return Result<IReadOnlyList<string>>.Success(drawn);
    }

    public Result<IReadOnlyList<string>> DiscardCards(Guid runId, IReadOnlyList<string> cardIds)
    {
        return MoveCards(runId, cardIds, CardConsumeDestination.Discard);
    }

    public Result<IReadOnlyList<string>> ExhaustCards(Guid runId, IReadOnlyList<string> cardIds)
    {
        return MoveCards(runId, cardIds, CardConsumeDestination.Exhaust);
    }

    public Result<IReadOnlyList<string>> AddCardsToHand(Guid runId, IReadOnlyList<string> cardIds)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.AddToHand(state.Deck, cardIds, state.Determinism);
            if (transition.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(transition.Error);

            if (!transition.Value.Cards.IsEmpty)
            {
                state = state with
                {
                    Deck = transition.Value.State,
                    Determinism = transition.Value.Context.AdvanceStep()
                };
                var persisted = Persist(
                    state,
                    "run.deck.add-to-hand",
                    new { cardIds });
                if (persisted.IsFailure)
                    return Result<IReadOnlyList<string>>.Failure(persisted.Error);
            }

            return Result<IReadOnlyList<string>>.Success(transition.Value.Cards);
        }
    }

    public Result<bool> HasCardInHand(Guid runId, string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return Result<bool>.Failure("Card id is required");

        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<bool>.Failure($"Run not found: {runId}");

            return Result<bool>.Success(
                Guid.TryParse(cardId, out var instanceId) && state.Deck.HandInstanceIds.Contains(instanceId));
        }
    }

    public Result<IReadOnlyList<string>> ConsumeCardsFromHand(Guid runId, IReadOnlyList<string> cardIds, CardConsumeDestination destination)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.MoveFromHand(state.Deck, cardIds, destination, state.Determinism);
            if (transition.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(transition.Error);

            if (!ReferenceEquals(transition.Value.State, state.Deck))
            {
                state = state with
                {
                    Deck = transition.Value.State,
                    Determinism = transition.Value.Context.AdvanceStep()
                };
                var persisted = Persist(
                    state,
                    "run.deck.consume",
                    new { cardIds, destination = destination.ToString() });
                if (persisted.IsFailure)
                    return Result<IReadOnlyList<string>>.Failure(persisted.Error);
            }

            return Result<IReadOnlyList<string>>.Success(transition.Value.Cards);
        }
    }

    public Result ShuffleDiscardIntoDrawPile(Guid runId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.ShuffleDiscardIntoDrawPile(state.Deck, state.Determinism);
            if (!transition.Cards.IsEmpty)
            {
                state = state with
                {
                    Deck = transition.State,
                    Determinism = transition.Context.AdvanceStep()
                };
                var persisted = Persist(state, "run.deck.shuffle-discard", new { });
                if (persisted.IsFailure)
                    return Result.Failure(persisted.Error);
            }
            return Result.Success();
        }
    }

    public Result<CardSelectionState> CreateCardSelection(Guid runId, string selectionId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var definitionResult = LoadCardSelectionDefinition(
                state.ConfigName,
                selectionId,
                state.Determinism.ContentRevision);
            if (definitionResult.IsFailure)
                return Result<CardSelectionState>.Failure(definitionResult.Error);

            var definition = definitionResult.Value;
            var generated = GenerateCardSelectionOptions(
                state,
                definition,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (generated.IsFailure)
                return Result<CardSelectionState>.Failure(generated.Error);
            var transition = CardSelectionTransitions.Create(
                state with { Determinism = generated.Value.Context },
                definition,
                generated.Value.Options,
                generated.Value.Fingerprint);
            return CommitTransition(
                transition,
                "run.card-selection.create",
                new { selectionId });
        }
    }

    public Result<CardSelectionState> PickCards(Guid runId, Guid selectionInstanceId, IReadOnlyList<string> cardIds)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var transition = CardSelectionTransitions.Pick(state, selectionInstanceId, cardIds);
            return transition.IsFailure
                ? Result<CardSelectionState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.card-selection.pick",
                    new { selectionInstanceId, cardIds });
        }
    }

    public Result<CardSelectionState> RerollCardSelection(Guid runId, Guid selectionInstanceId, IReadOnlyList<string>? lockedCardIds = null)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var selection = state.CardSelections.FirstOrDefault(item => item.SelectionInstanceId == selectionInstanceId);
            if (selection == null)
                return Result<CardSelectionState>.Failure($"Card selection not found: {selectionInstanceId}");

            var definitionResult = LoadCardSelectionDefinition(
                state.ConfigName,
                selection.SelectionId,
                state.Determinism.ContentRevision);
            if (definitionResult.IsFailure)
                return Result<CardSelectionState>.Failure(definitionResult.Error);

            var locked = new HashSet<string>(lockedCardIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var generated = GenerateCardSelectionOptions(state, definitionResult.Value, locked);
            if (generated.IsFailure)
                return Result<CardSelectionState>.Failure(generated.Error);
            var transition = CardSelectionTransitions.Reroll(
                state with { Determinism = generated.Value.Context },
                selectionInstanceId,
                generated.Value.Options,
                generated.Value.Fingerprint);
            return transition.IsFailure
                ? Result<CardSelectionState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.card-selection.reroll",
                    new { selectionInstanceId, lockedCardIds });
        }
    }

    public Result<CardSelectionState> DecomposeCardSelectionOption(Guid runId, Guid selectionInstanceId, string cardId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var transition = CardSelectionTransitions.Decompose(state, selectionInstanceId, cardId);
            return transition.IsFailure
                ? Result<CardSelectionState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.card-selection.decompose",
                    new { selectionInstanceId, cardId });
        }
    }

    public Result<ShopState> CreateShop(Guid runId, string shopId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<ShopState>.Failure($"Run not found: {runId}");

            var definitionResult = LoadShopDefinition(
                state.ConfigName,
                shopId,
                state.Determinism.ContentRevision);
            if (definitionResult.IsFailure)
                return Result<ShopState>.Failure(definitionResult.Error);

            var definition = definitionResult.Value;
            var generated = GenerateShopItems(state, definition);
            if (generated.IsFailure)
                return Result<ShopState>.Failure(generated.Error);
            var transition = ShopTransitions.Create(
                state with { Determinism = generated.Value.Context },
                definition,
                generated.Value.Items,
                generated.Value.Fingerprint);
            return CommitTransition(
                transition,
                "run.shop.create",
                new { shopId });
        }
    }

    public Result<ShopItemState> BuyShopItem(Guid runId, Guid shopInstanceId, string itemId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<ShopItemState>.Failure($"Run not found: {runId}");

            var transition = ShopTransitions.Buy(state, shopInstanceId, itemId);
            return transition.IsFailure
                ? Result<ShopItemState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.shop.buy",
                    new { shopInstanceId, itemId });
        }
    }

    public Result<ShopState> RerollShop(Guid runId, Guid shopInstanceId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<ShopState>.Failure($"Run not found: {runId}");

            var shop = state.Shops.FirstOrDefault(item => item.ShopInstanceId == shopInstanceId);
            if (shop == null)
                return Result<ShopState>.Failure($"Shop not found: {shopInstanceId}");

            var definitionResult = LoadShopDefinition(
                state.ConfigName,
                shop.ShopId,
                state.Determinism.ContentRevision);
            if (definitionResult.IsFailure)
                return Result<ShopState>.Failure(definitionResult.Error);

            var generated = GenerateShopItems(state, definitionResult.Value);
            if (generated.IsFailure)
                return Result<ShopState>.Failure(generated.Error);
            var transition = ShopTransitions.Reroll(
                state with { Determinism = generated.Value.Context },
                shopInstanceId,
                generated.Value.Items,
                generated.Value.Fingerprint);
            return transition.IsFailure
                ? Result<ShopState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.shop.reroll",
                    new { shopInstanceId });
        }
    }

    public Result<PreparationState> CreatePreparation(Guid runId, string preparationId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<PreparationState>.Failure($"Run not found: {runId}");

            var definitionResult = LoadPreparationDefinition(
                state.ConfigName,
                preparationId,
                state.Determinism.ContentRevision);
            if (definitionResult.IsFailure)
                return Result<PreparationState>.Failure(definitionResult.Error);

            var transition = PreparationTransitions.Create(state, definitionResult.Value);
            return CommitTransition(
                transition,
                "run.preparation.create",
                new { preparationId });
        }
    }

    public Result<PreparationOptionState> ApplyPreparationOption(Guid runId, Guid preparationInstanceId, string optionId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<PreparationOptionState>.Failure($"Run not found: {runId}");

            var plan = PreparationTransitions.PlanApply(state, preparationInstanceId, optionId);
            if (plan.IsFailure)
                return Result<PreparationOptionState>.Failure(plan.Error);
            if (!plan.Value.ModifierGrants.IsEmpty && _scriptModifierManager == null)
                return Result<PreparationOptionState>.Failure(
                    "Script modifier manager is not available for preparation modifier grants");

            var resolvedModifiers = new List<ScriptModifierInstance>();
            foreach (var grant in plan.Value.ModifierGrants)
            {
                var definition = _scriptModifierManager is IRevisionedScriptModifierManager revisionedModifiers
                    ? revisionedModifiers.GetDefinition(
                        grant.ModifierId,
                        state.Determinism.ContentRevision,
                        state.ConfigName)
                    : _scriptModifierManager!.GetDefinition(grant.ModifierId);
                if (definition.IsFailure)
                    return Result<PreparationOptionState>.Failure(definition.Error);
                if (grant.Stacks <= 0 || grant.Stacks > definition.Value.MaxStacks)
                    return Result<PreparationOptionState>.Failure(
                        $"Invalid stack count for modifier {grant.ModifierId}: {grant.Stacks}");
                resolvedModifiers.Add(new ScriptModifierInstance
                {
                    InstanceId = grant.InstanceId,
                    ModifierId = definition.Value.ModifierId,
                    Definition = definition.Value,
                    OwnerId = grant.OwnerId,
                    SourceId = grant.SourceId,
                    Stacks = grant.Stacks,
                    Duration = grant.Duration ?? definition.Value.DefaultDuration
                });
            }

            var transition = PreparationTransitions.CommitApply(
                state,
                plan.Value,
                resolvedModifiers);
            if (transition.IsFailure)
                return Result<PreparationOptionState>.Failure(transition.Error);

            return CommitTransition(
                transition.Value,
                "run.preparation.apply",
                new { preparationInstanceId, optionId });
        }
    }

    private Result<T> CommitTransition<T>(
        RunStateTransition<T> transition,
        string commandType,
        object command)
    {
        var persisted = Persist(transition.State, commandType, command);
        return persisted.IsSuccess
            ? Result<T>.Success(transition.Value)
            : Result<T>.Failure(persisted.Error);
    }

    private Result<CardSelectionOfferGeneration> GenerateCardSelectionOptions(
        RunState state,
        CardSelectionDefinition definition,
        IReadOnlySet<string> lockedCardIds)
    {
        var lockedOptions = lockedCardIds
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => CreateCardSelectionOption(state, cardId))
            .Where(option => option != null)
            .Cast<CardSelectionOptionState>()
            .ToList();
        var desiredCount = System.Math.Max(1, definition.OfferCount) - lockedOptions.Count;
        if (!string.IsNullOrWhiteSpace(definition.CardPoolId))
        {
            if (_cardPoolResolver == null)
            {
                return Result<CardSelectionOfferGeneration>.Failure(
                    $"Card pool resolver is unavailable for {definition.CardPoolId}");
            }
            var poolResult = _cardPoolResolver is IRevisionedCardPoolResolver revisionedPools
                ? revisionedPools.ResolvePool(
                    definition.CardPoolId,
                    state.Determinism.ContentRevision,
                    state.ConfigName)
                : _cardPoolResolver.ResolvePool(definition.CardPoolId, state.ConfigName);
            if (poolResult.IsFailure)
                return Result<CardSelectionOfferGeneration>.Failure(poolResult.Error);
            var offer = CardOfferResolver.Resolve(
                poolResult.Value,
                System.Math.Max(0, desiredCount),
                state.Determinism,
                lockedCardIds);
            if (offer.IsFailure)
                return Result<CardSelectionOfferGeneration>.Failure(offer.Error);
            lockedOptions.AddRange(offer.Value.Cards.Select(ToOption));
            return Result<CardSelectionOfferGeneration>.Success(new(
                lockedOptions,
                offer.Value.Context,
                offer.Value.Fingerprint));
        }

        var candidates = definition.CardPool
            .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
            .Where(cardId => !lockedCardIds.Contains(cardId))
            .Select(cardId => CreateCardSelectionOption(state, cardId) ?? new CardSelectionOptionState { CardId = cardId })
            .OrderBy(option => option.CardId, StringComparer.Ordinal)
            .ToList();
        lockedOptions.AddRange(candidates.Take(System.Math.Max(0, desiredCount)));
        return Result<CardSelectionOfferGeneration>.Success(new(
            lockedOptions,
            state.Determinism,
            CanonicalJson.ComputeHash(lockedOptions.Select(option => option.CardId).ToArray())));
    }

    private CardSelectionOptionState? CreateCardSelectionOption(RunState state, string cardId)
    {
        if (_cardContentCatalog == null)
            return null;

        var cardResult = _cardContentCatalog is IRevisionedCardContentCatalog revisionedCards
            ? revisionedCards.GetCard(
                cardId,
                state.Determinism.ContentRevision,
                state.ConfigName)
            : _cardContentCatalog.GetCard(cardId, state.ConfigName);
        return cardResult.IsSuccess ? ToOption(cardResult.Value) : null;
    }

    private static CardSelectionOptionState ToOption(CardContentDefinition card)
    {
        return new CardSelectionOptionState
        {
            CardId = card.CardId,
            Rarity = card.Rarity,
            Tags = card.Tags.ToList(),
            DecomposePowerPoints = card.DecomposePowerPoints
        };
    }

    private Result<ShopOfferGeneration> GenerateShopItems(RunState state, ShopDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.CardPoolId))
        {
            if (_cardPoolResolver == null)
            {
                return Result<ShopOfferGeneration>.Failure(
                    $"Card pool resolver is unavailable for {definition.CardPoolId}");
            }
            var poolResult = _cardPoolResolver is IRevisionedCardPoolResolver revisionedPools
                ? revisionedPools.ResolvePool(
                    definition.CardPoolId,
                    state.Determinism.ContentRevision,
                    state.ConfigName)
                : _cardPoolResolver.ResolvePool(definition.CardPoolId, state.ConfigName);
            if (poolResult.IsFailure)
                return Result<ShopOfferGeneration>.Failure(poolResult.Error);
            var offer = CardOfferResolver.Resolve(
                poolResult.Value,
                System.Math.Max(1, definition.OfferCount),
                state.Determinism);
            if (offer.IsFailure)
                return Result<ShopOfferGeneration>.Failure(offer.Error);
            return Result<ShopOfferGeneration>.Success(new(
                offer.Value.Cards
                    .Select((card, index) => ToShopItem(card, definition.Pricing, index))
                    .ToList(),
                offer.Value.Context,
                offer.Value.Fingerprint));
        }

        var items = definition.Items
            .Select((item, index) => ToShopItem(state, item, definition.Pricing, index))
            .ToList();
        return Result<ShopOfferGeneration>.Success(new(
            items,
            state.Determinism,
            CanonicalJson.ComputeHash(items.Select(item => item.ItemId).ToArray())));
    }

    private ShopItemState ToShopItem(RunState state, ShopItemDefinition item, ShopPricingRules pricing, int index)
    {
        if (!string.IsNullOrWhiteSpace(item.CardId) && _cardContentCatalog != null)
        {
            var cardResult = _cardContentCatalog is IRevisionedCardContentCatalog revisionedCards
                ? revisionedCards.GetCard(
                    item.CardId,
                    state.Determinism.ContentRevision,
                    state.ConfigName)
                : _cardContentCatalog.GetCard(item.CardId, state.ConfigName);
            if (cardResult.IsSuccess)
                return ToShopItem(cardResult.Value, pricing, index, item.ItemId, item.PowerPointCost, item.GoldCost);
        }

        return new ShopItemState
        {
            ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? $"item_{index + 1}" : item.ItemId,
            CardId = item.CardId,
            BaseGoldPrice = item.GoldCost,
            GoldCost = item.GoldCost,
            PowerPointCost = item.PowerPointCost
        };
    }

    private static ShopItemState ToShopItem(CardContentDefinition card, ShopPricingRules pricing, int index, string? itemId = null, int powerPointCost = 0, int? explicitGoldCost = null)
    {
        var breakdown = CalculateShopPrice(card, pricing, explicitGoldCost);
        return new ShopItemState
        {
            ItemId = string.IsNullOrWhiteSpace(itemId) ? $"buy_{card.CardId}_{index + 1}" : itemId,
            CardId = card.CardId,
            Rarity = card.Rarity,
            Tags = card.Tags.ToList(),
            BaseGoldPrice = card.BaseGoldPrice,
            GoldCost = (int)System.Math.Ceiling(breakdown["final"]),
            PowerPointCost = powerPointCost,
            PricingBreakdown = breakdown
        };
    }

    private sealed record CardSelectionOfferGeneration(
        IReadOnlyList<CardSelectionOptionState> Options,
        DeterministicContext Context,
        string Fingerprint);

    private sealed record ShopOfferGeneration(
        IReadOnlyList<ShopItemState> Items,
        DeterministicContext Context,
        string Fingerprint);

    private static Dictionary<string, double> CalculateShopPrice(CardContentDefinition card, ShopPricingRules pricing, int? explicitGoldCost)
    {
        var basePrice = explicitGoldCost.GetValueOrDefault(card.BaseGoldPrice);
        var rarityMultiplier = pricing.RarityMultipliers.TryGetValue(card.Rarity, out var rarityValue) ? rarityValue : 1.0;
        var tagMultiplier = card.Tags
            .Select(tag => pricing.TagMultipliers.TryGetValue(tag, out var value) ? value : 1.0)
            .Aggregate(1.0, (current, value) => current * value);
        var final = basePrice * pricing.BaseMultiplier * rarityMultiplier * tagMultiplier;

        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["base"] = basePrice,
            ["baseMultiplier"] = pricing.BaseMultiplier,
            ["rarityMultiplier"] = rarityMultiplier,
            ["tagMultiplier"] = tagMultiplier,
            ["final"] = final
        };
    }

    private Result<T> GetContentDefinition<T>(
        RunState run,
        string kind,
        string definitionId,
        Func<Result<T>> fallback)
    {
        if (_contentRuntimes == null)
            return fallback();

        var runtime = _contentRuntimes.Resolve(
            run.Determinism.ContentRevision,
            run.ConfigName);
        return runtime.IsFailure
            ? Result<T>.Failure(runtime.Error)
            : runtime.Value.GetDefinition<T>(kind, definitionId);
    }

    private Result<RunDefinition> LoadDefinition(
        string configName,
        string runDefinitionId,
        string? contentRevision = null)
    {
        if (!string.IsNullOrWhiteSpace(contentRevision) && _contentRuntimes != null)
        {
            var runtime = _contentRuntimes.Resolve(contentRevision, configName);
            return runtime.IsFailure
                ? Result<RunDefinition>.Failure(runtime.Error)
                : runtime.Value.GetDefinition<RunDefinition>("runs", runDefinitionId);
        }

        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName);
            var data = _resourceLoader.LoadResource($"runs/{runDefinitionId}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<RunDefinition>.Failure($"Run definition not found: {runDefinitionId}");

            var element = data.TryGetValue(runDefinitionId, out var exact) ? exact : data.Values.First();
            var definition = JsonSerializer.Deserialize<RunDefinition>(element.GetRawText(), _jsonOptions);
            return definition == null
                ? Result<RunDefinition>.Failure($"Failed to deserialize run definition: {runDefinitionId}")
                : Result<RunDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<RunDefinition>.Failure($"Could not load run definition '{runDefinitionId}': {ex.Message}", ex);
        }
    }

    private Result<CardSelectionDefinition> LoadCardSelectionDefinition(
        string configName,
        string selectionId,
        string? contentRevision = null)
    {
        if (!string.IsNullOrWhiteSpace(contentRevision) && _contentRuntimes != null)
        {
            var runtime = _contentRuntimes.Resolve(contentRevision, configName);
            return runtime.IsFailure
                ? Result<CardSelectionDefinition>.Failure(runtime.Error)
                : runtime.Value.GetDefinition<CardSelectionDefinition>("card-selections", selectionId);
        }

        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName);
            var data = _resourceLoader.LoadResource($"card-selections/{selectionId}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<CardSelectionDefinition>.Failure($"Card selection definition not found: {selectionId}");

            var element = data.TryGetValue(selectionId, out var exact) ? exact : data.Values.First();
            var definition = JsonSerializer.Deserialize<CardSelectionDefinition>(element.GetRawText(), _jsonOptions);
            return definition == null
                ? Result<CardSelectionDefinition>.Failure($"Failed to deserialize card selection definition: {selectionId}")
                : Result<CardSelectionDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<CardSelectionDefinition>.Failure($"Could not load card selection definition '{selectionId}': {ex.Message}", ex);
        }
    }

    private Result<ShopDefinition> LoadShopDefinition(
        string configName,
        string shopId,
        string? contentRevision = null)
    {
        if (!string.IsNullOrWhiteSpace(contentRevision) && _contentRuntimes != null)
        {
            var runtime = _contentRuntimes.Resolve(contentRevision, configName);
            return runtime.IsFailure
                ? Result<ShopDefinition>.Failure(runtime.Error)
                : runtime.Value.GetDefinition<ShopDefinition>("shops", shopId);
        }

        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName);
            var data = _resourceLoader.LoadResource($"shops/{shopId}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<ShopDefinition>.Failure($"Shop definition not found: {shopId}");

            var element = data.TryGetValue(shopId, out var exact) ? exact : data.Values.First();
            var definition = JsonSerializer.Deserialize<ShopDefinition>(element.GetRawText(), _jsonOptions);
            return definition == null
                ? Result<ShopDefinition>.Failure($"Failed to deserialize shop definition: {shopId}")
                : Result<ShopDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<ShopDefinition>.Failure($"Could not load shop definition '{shopId}': {ex.Message}", ex);
        }
    }

    private Result<PreparationDefinition> LoadPreparationDefinition(
        string configName,
        string preparationId,
        string? contentRevision = null)
    {
        if (!string.IsNullOrWhiteSpace(contentRevision) && _contentRuntimes != null)
        {
            var runtime = _contentRuntimes.Resolve(contentRevision, configName);
            return runtime.IsFailure
                ? Result<PreparationDefinition>.Failure(runtime.Error)
                : runtime.Value.GetDefinition<PreparationDefinition>("preparations", preparationId);
        }

        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName);
            var data = _resourceLoader.LoadResource($"preparations/{preparationId}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<PreparationDefinition>.Failure($"Preparation definition not found: {preparationId}");

            var element = data.TryGetValue(preparationId, out var exact) ? exact : data.Values.First();
            var definition = JsonSerializer.Deserialize<PreparationDefinition>(element.GetRawText(), _jsonOptions);
            return definition == null
                ? Result<PreparationDefinition>.Failure($"Failed to deserialize preparation definition: {preparationId}")
                : Result<PreparationDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<PreparationDefinition>.Failure($"Could not load preparation definition '{preparationId}': {ex.Message}", ex);
        }
    }

    private Result<IReadOnlyList<string>> MoveCards(
        Guid runId,
        IReadOnlyList<string> cardIds,
        CardConsumeDestination destination)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.MoveFromHand(state.Deck, cardIds, destination, state.Determinism);
            if (transition.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(transition.Error);

            state = state with
            {
                Deck = transition.Value.State,
                Determinism = transition.Value.Context.AdvanceStep()
            };
            var persisted = Persist(
                state,
                "run.deck.move",
                new { cardIds, destination = destination.ToString() });
            if (persisted.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(persisted.Error);

            return Result<IReadOnlyList<string>>.Success(transition.Value.Cards);
        }
    }

    /// <summary>
    /// Restores a run state without incrementing sequence or triggering persistence.
    /// Used for time-travel operations (undo).
    /// </summary>
    public Result<RunState> RestoreState(RunState state)
    {
        if (state == null)
            return Result<RunState>.Failure("State cannot be null");

        lock (_lock)
        {
            var candidate = _runs.TryGetValue(state.RunId, out var current)
                ? state with
                {
                    Sequence = current.Sequence,
                    Determinism = current.Determinism.AdvanceStep()
                }
                : state with { Determinism = state.Determinism.AdvanceStep() };
            var restored = Persist(
                candidate,
                "run.restore",
                new { targetSequence = state.Sequence });
            return restored.IsSuccess
                ? restored
                : Result<RunState>.Failure(restored.Error);
        }
    }

    internal Result<RunState> HydrateForReplay(RunState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var compatibility = ValidateLoadedRunCompatibility(state);
        if (compatibility.IsFailure)
            return Result<RunState>.Failure(compatibility.Error);
        lock (_lock)
        {
            _runs[state.RunId] = state;
        }
        return Result<RunState>.Success(state);
    }

    /// <summary>
    /// Cria e grava um checkpoint antes de publicar o novo snapshot em memória.
    /// Falhas de durabilidade são devolvidas ao chamador e não alteram a run ativa.
    /// </summary>
    private Result<RunState> Persist(
        RunState state,
        string commandType,
        object command,
        RunCommandIdentity? commandIdentity = null)
    {
        _runs.TryGetValue(state.RunId, out var previous);
        try
        {
            var activeCommand = commandIdentity == null ? _executingCommand.Value : null;
            var identity = commandIdentity ?? activeCommand?.Identity;
            var effectiveType = identity?.Type ?? commandType;
            var effectiveCommand = activeCommand?.Payload
                ?? JsonSerializer.SerializeToElement(command, _jsonOptions).Clone();
            var nextSequence = checked((previous?.Sequence ?? 0) + 1);
            state = state with { Sequence = nextSequence };
            var snapshot = CreateSnapshot(state);
            var stateHash = CanonicalJson.ComputeHash(snapshot);
            var previousHash = previous == null
                ? string.Empty
                : CanonicalJson.ComputeHash(previous);
            var entry = new RunJournalEntry
            {
                RunId = snapshot.RunId,
                CommandId = identity?.CommandId,
                Sequence = snapshot.Sequence,
                Step = snapshot.Determinism.Step,
                ExpectedSequence = identity?.ExpectedSequence,
                ExpectedStep = identity?.ExpectedStep,
                CommandPayloadHash = identity?.PayloadHash ?? string.Empty,
                CommandType = effectiveType,
                Command = effectiveCommand,
                PreviousStateHash = previousHash,
                StateHash = stateHash,
                LogicalTimestamp = snapshot.Determinism.LogicalTimestamp.UtcDateTime
            };

            if (_repository is IRunCheckpointRepository checkpoints)
            {
                checkpoints.SaveCheckpointAsync(new RunCheckpoint(snapshot, entry))
                    .GetAwaiter().GetResult();
            }
            else if (_repository != null)
            {
                _repository.SaveAsync(snapshot).GetAwaiter().GetResult();
            }

            _runs[state.RunId] = state;
            if (entry.CommandId is { } commandId)
            {
                _commandReceipts[(state.RunId, commandId)] =
                    CreateReceipt(state, entry, duplicate: false);
            }
            return Result<RunState>.Success(state);
        }
        catch (Exception exception)
        {
            if (previous == null)
                _runs.Remove(state.RunId);
            else
                _runs[state.RunId] = previous;
            return Result<RunState>.Failure(
                $"Failed to persist run transition '{commandType}': {exception.Message}",
                exception);
        }
    }

    private Result<RunState> PersistBatch(
        RunState previous,
        IReadOnlyList<(RunState State, CombatResolutionStep Step)> candidates,
        RunCommandIdentity? rootCommand,
        JsonElement rootPayload = default)
    {
        try
        {
            var checkpoints = new List<RunCheckpoint>(candidates.Count);
            var priorState = previous;
            for (var index = 0; index < candidates.Count; index++)
            {
                var isFinal = index == candidates.Count - 1;
                var candidate = candidates[index];
                var state = candidate.State with
                {
                    Sequence = checked(previous.Sequence + index + 1)
                };
                var snapshot = CreateSnapshot(state);
                var stateHash = CanonicalJson.ComputeHash(snapshot);
                var previousHash = CanonicalJson.ComputeHash(priorState);
                var internalId = rootCommand == null
                    ? (Guid?)null
                    : DeterministicId.Create(
                        previous.Determinism.Seed,
                        (ulong)index,
                        $"combat-resolution:{rootCommand.CommandId:N}:{candidate.Step.TransitionType}");
                var commandId = isFinal && rootCommand != null
                    ? rootCommand.CommandId
                    : internalId;
                var entry = new RunJournalEntry
                {
                    RunId = state.RunId,
                    CommandId = commandId,
                    RootCommandId = rootCommand?.CommandId,
                    CausationId = rootCommand?.CommandId,
                    TransitionIndex = index,
                    TransitionCount = candidates.Count,
                    Sequence = snapshot.Sequence,
                    Step = snapshot.Determinism.Step,
                    ExpectedSequence = isFinal ? rootCommand?.ExpectedSequence : null,
                    ExpectedStep = isFinal ? rootCommand?.ExpectedStep : null,
                    CommandPayloadHash = isFinal ? rootCommand?.PayloadHash ?? string.Empty : string.Empty,
                    CommandType = isFinal && rootCommand != null
                        ? rootCommand.Type
                        : candidate.Step.TransitionType,
                    Command = isFinal && rootPayload.ValueKind != JsonValueKind.Undefined
                        ? rootPayload.Clone()
                        : candidate.Step.Payload.ValueKind == JsonValueKind.Undefined
                        ? JsonSerializer.SerializeToElement(new { }, _jsonOptions)
                        : candidate.Step.Payload.Clone(),
                    PreviousStateHash = previousHash,
                    StateHash = stateHash,
                    LogicalTimestamp = snapshot.Determinism.LogicalTimestamp.UtcDateTime
                };
                checkpoints.Add(new RunCheckpoint(snapshot, entry));
                priorState = state;
            }

            var batch = new RunCheckpointBatch
            {
                RunId = previous.RunId,
                RootCommandId = rootCommand?.CommandId,
                Checkpoints = checkpoints
            };
            if (_repository is IRunCheckpointRepository checkpointRepository)
            {
                checkpointRepository.SaveCheckpointBatchAsync(batch).GetAwaiter().GetResult();
            }
            else if (_repository != null)
            {
                _repository.SaveAsync(checkpoints[^1].State).GetAwaiter().GetResult();
            }

            var finalState = candidates[^1].State with { Sequence = checkpoints[^1].State.Sequence };
            _runs[previous.RunId] = finalState;
            foreach (var checkpoint in checkpoints)
            {
                if (checkpoint.JournalEntry.CommandId is { } commandId)
                {
                    _commandReceipts[(previous.RunId, commandId)] = CreateReceipt(
                        checkpoint.State,
                        checkpoint.JournalEntry,
                        duplicate: false);
                }
            }
            return Result<RunState>.Success(finalState);
        }
        catch (Exception exception)
        {
            _runs[previous.RunId] = previous;
            return Result<RunState>.Failure(
                $"Failed to persist combat resolution: {exception.Message}",
                exception);
        }
    }

    private static RunCommandReceipt CreateReceipt(
        RunState state,
        RunJournalEntry entry,
        bool duplicate)
    {
        return new RunCommandReceipt
        {
            CommandId = entry.CommandId ?? Guid.Empty,
            CommandType = entry.CommandType,
            Sequence = entry.Sequence,
            Step = entry.Step,
            PreviousStateHash = entry.PreviousStateHash,
            StateHash = entry.StateHash,
            State = state,
            JournalEntry = entry,
            Duplicate = duplicate
        };
    }

    private static bool IsSameCommand(RunJournalEntry entry, RunCommand command)
    {
        return string.Equals(entry.CommandType, command.Identity.Type, StringComparison.Ordinal) &&
               entry.ExpectedSequence == command.Identity.ExpectedSequence &&
               entry.ExpectedStep == command.Identity.ExpectedStep &&
               string.Equals(
                   string.IsNullOrWhiteSpace(entry.CommandPayloadHash)
                       ? CanonicalJson.ComputeHash(entry.Command)
                       : entry.CommandPayloadHash,
                   string.IsNullOrWhiteSpace(command.Identity.PayloadHash)
                       ? CanonicalJson.ComputeHash(command.Payload)
                       : command.Identity.PayloadHash,
                   StringComparison.Ordinal);
    }

    private RunState CreateSnapshot(RunState state)
    {
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        return JsonSerializer.Deserialize<RunState>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to create a run-state snapshot");
    }

    private static int SaturatingEconomyChange(int current, int amount)
    {
        return (int)System.Math.Clamp((long)current + amount, 0, int.MaxValue);
    }

    private static int FindEncounterIndex(RunState state, Guid combatId)
    {
        for (var index = 0; index < state.Encounters.Length; index++)
        {
            if (state.Encounters[index].Combat.CombatId == combatId)
                return index;
        }

        return -1;
    }

    private static ulong CreateSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }

    private static ImmutableDictionary<string, JsonElement> ToImmutableMetadata(
        IReadOnlyDictionary<string, object> metadata)
    {
        return metadata.ToImmutableDictionary(
            entry => entry.Key,
            entry => JsonSerializer.SerializeToElement(entry.Value).Clone(),
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed record AdvanceNodePayload(string TargetNodeId);
    private sealed record ResolveNodePayload(string CurrentNodeId);
    private sealed record CountPayload(int Count);
    private sealed record CardIdsPayload(IReadOnlyList<string> CardIds);
    private sealed record CardSelectionPayload(string SelectionId);
    private sealed record CardSelectionCardsPayload(
        Guid SelectionInstanceId,
        IReadOnlyList<string> CardIds);
    private sealed record RerollCardSelectionPayload(
        Guid SelectionInstanceId,
        IReadOnlyList<string>? LockedCardIds);
    private sealed record CardSelectionItemPayload(
        Guid SelectionInstanceId,
        string CardId);
    private sealed record ShopItemPayload(Guid ShopInstanceId, string ItemId);
    private sealed record ShopPayload(Guid ShopInstanceId);
    private sealed record ShopDefinitionPayload(string ShopId);
    private sealed record PreparationPayload(Guid PreparationInstanceId, string OptionId);
    private sealed record PreparationDefinitionPayload(string PreparationId);
    private sealed record RelicPayload(string RelicId);
    private sealed record RelicInstancePayload(Guid RelicInstanceId);
    private sealed record CardUpgradePayload(Guid CardInstanceId, string UpgradeId);
    private sealed record CheckpointPayload(int Sequence);
    private sealed record ContentRevisionPayload(string Revision);
}
