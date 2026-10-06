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
using Core.Effects;
using Core.Determinism;
using Core.Run.Content;
using Core.Run.Branching;
using Core.Run.Sandbox;
using Core.StatusEffects;
using Core.Resources;
using Core.CardZones;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Run;

public sealed class RunManager : IRunManager, IRunEncounterRuntime, IContentRevisionActivationPreviewService
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly IPinnedContentCatalog<ScriptModifierDefinition>? _scriptModifierCatalog;
    private readonly IOperationalEventBus? _eventBus;
    private readonly IRunCommitStore? _repository;
    private readonly IRunCommitReader? _history;
    private readonly IContentManifestProvider? _contentManifestProvider;
    private readonly IContentPublicationService? _contentPublications;
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private readonly IResourceManager? _resources;
    private readonly IResourceCatalog<GameModeDefinition>? _modeCatalog;
    private readonly IGameModeResolver? _gameModeResolver;
    private readonly RunContentDefinitionResolver? _contentDefinitions;
    private readonly RunOfferGenerator _offerGenerator;
    private readonly RunActivityRegistry _activities;
    private readonly RunProgressionService _progression;
    private readonly IRunActivityEffectExecutor? _activityEffects;
    private readonly ICardZoneFlowExecutor? _cardZoneFlows;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, RunState> _runs = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid RunId, Guid CommandId), RunCommandReceipt> _commandReceipts = new();
    // Only used by the direct test harness. Production command execution is
    // owned by RunSessionCoordinator and never consults ambient state.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, GameplayCommandEnvelope> _directCommandScopes = new();
    private readonly RunSessionGateProvider _sessionGates;
    private readonly JsonSerializerOptions _jsonOptions;

    public RunManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ICardPoolResolver? cardPoolResolver = null,
        ICardContentCatalog? cardContentCatalog = null,
        IPinnedContentCatalog<ScriptModifierDefinition>? scriptModifierCatalog = null,
        IOperationalEventBus? eventBus = null,
        IRunCommitStore? repository = null,
        IContentManifestProvider? contentManifestProvider = null,
        IResourceCatalog<GameModeDefinition>? modeCatalog = null,
        IGameModeResolver? gameModeResolver = null,
        IContentPublicationService? contentPublications = null,
        IContentRuntimeResolver? contentRuntimes = null,
        IResourceManager? resources = null,
        IRunCommitReader? history = null,
        RunSessionGateProvider? sessionGates = null,
        RunActivityRegistry? activities = null,
        IRunActivityEffectExecutor? activityEffects = null,
        ICardZoneFlowExecutor? cardZoneFlows = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _scriptModifierCatalog = scriptModifierCatalog;
        _eventBus = eventBus;
        _repository = repository;
        _history = history ?? repository;
        _sessionGates = sessionGates ?? new RunSessionGateProvider();
        _contentManifestProvider = contentManifestProvider;
        _contentPublications = contentPublications;
        _contentRuntimes = contentRuntimes;
        _resources = resources;
        _modeCatalog = modeCatalog;
        _gameModeResolver = gameModeResolver;
        _contentDefinitions = contentRuntimes == null
            ? null
            : new RunContentDefinitionResolver(contentRuntimes);
        _offerGenerator = new RunOfferGenerator(
            cardPoolResolver,
            cardContentCatalog,
            useRevisionedContent: contentRuntimes != null);
        _activities = activities ?? RunActivityRegistry.CreateDefault();
        _progression = new RunProgressionService(_activities);
        _activityEffects = activityEffects;
        _cardZoneFlows = cardZoneFlows;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
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
        if (string.IsNullOrWhiteSpace(options.ModeId))
            return Result<RunState>.Failure("Game mode id is required");
        var publishedRuntimeRequired = _contentPublications != null && _contentRuntimes != null;
        if (publishedRuntimeRequired && string.IsNullOrWhiteSpace(options.SettingId))
            return Result<RunState>.Failure("Setting id is required");
        if (publishedRuntimeRequired && !string.Equals(options.SettingId, options.ConfigName, StringComparison.Ordinal))
            return Result<RunState>.Failure("Setting id must match the content configuration name");
        if (publishedRuntimeRequired && string.IsNullOrWhiteSpace(options.ContentRevision))
            return Result<RunState>.Failure("Published content revision is required");

        ResolvedContentManifest? resolvedContent = null;
        if (_contentManifestProvider != null)
        {
            var earlyManifest = ResolveContentManifest(options, definition: null);
            if (earlyManifest.IsFailure)
                return Result<RunState>.Failure(earlyManifest.Error);
            resolvedContent = earlyManifest.Value;
        }

        var effectiveRunDefinitionId = options.RunDefinitionId;
        var mode = _gameModeResolver switch
        {
            IRevisionedGameModeResolver revisioned when resolvedContent != null =>
                revisioned.Resolve(options.ModeId, options.ConfigName, resolvedContent.Revision),
            null => ResolveLegacyMode(options.ModeId, options.ConfigName),
            _ => _gameModeResolver.Resolve(options.ModeId, options.ConfigName)
        };
        if (mode.IsFailure)
            return Result<RunState>.Failure(mode.Error);
        var resolvedMode = mode.Value;
        if (!resolvedMode.Definition.AllowCustomSeed && options.Seed.HasValue && string.IsNullOrWhiteSpace(options.ChallengeId))
            return Result<RunState>.Failure($"Game mode does not allow a custom seed: {options.ModeId}");
        if (!string.IsNullOrWhiteSpace(resolvedMode.Definition.RunDefinitionId))
            effectiveRunDefinitionId = resolvedMode.Definition.RunDefinitionId;
        if (resolvedMode.CardZoneSystem == null)
            return Result<RunState>.Failure($"Game mode requires a card-zone system: {options.ModeId}");

        var definitionResult = LoadDefinition(
            options.ConfigName,
            effectiveRunDefinitionId,
            resolvedContent?.Revision);
        if (definitionResult.IsFailure)
            return Result<RunState>.Failure(definitionResult.Error);

        var definition = definitionResult.Value;
        var mapResult = RunMapTransitions.Create(definition.MapNodes, _activities);
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

        var startingCards = options.StartingCards ?? definition.StartingCards
            .Select(cardId => new RunStartingCard { DefinitionId = cardId })
            .ToArray();
        if (startingCards.Count == 0)
            return Result<RunState>.Failure("Starting cards cannot be empty");
        var initialPlayableCardCount = options.InitialPlayableCardCount ?? definition.InitialPlayableCardCount;
        if (initialPlayableCardCount < 0)
            return Result<RunState>.Failure("Initial playable card count cannot be negative");

        if (_cardZoneFlows == null)
            return Result<RunState>.Failure("Card-zone flow executor is required for the selected game mode");
        var compiled = CardZoneSystemCompiler.Compile(resolvedMode.CardZoneSystem);
        if (compiled.IsFailure)
            return Result<RunState>.Failure(compiled.Error);
        var initialized = CardZoneRunInitializer.Initialize(
            compiled.Value, definition, startingCards, "$run", options.PlayerEntityId,
            [], initialPlayableCardCount, contentRevision!, options.ConfigName, context, _cardZoneFlows);
        if (initialized.IsFailure)
            return Result<RunState>.Failure(initialized.Error);
        var initialDeck = new DeckState { Topology = initialized.Value.State };
        context = initialized.Value.Context;
        var initialZoneSteps = initialized.Value.InitialFlowSteps;

        var state = new RunState
        {
            RunId = runId.Value,
            ConfigName = options.ConfigName,
            SettingId = options.SettingId ?? options.ConfigName,
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
            Deck = initialDeck,
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

        if (_contentRuntimes != null)
        {
            var runtime = _contentRuntimes.Resolve(contentRevision!, options.ConfigName);
            if (runtime.IsFailure) return Result<RunState>.Failure(runtime.Error);
            var cards = RunContentCompatibilityValidator.ValidateForActivation(state, runtime.Value);
            if (cards.IsFailure) return Result<RunState>.Failure(cards.Error);
        }

        state = state with
        {
            Determinism = state.Determinism.AdvanceStep()
        };
        var initialNode = state.Map.Nodes.FirstOrDefault(node => node.NodeId == state.CurrentNodeId);
        if (initialNode != null)
        {
            var entry = ApplyActivityBoundary(state, initialNode, RunActivityBoundary.Entry);
            if (entry.IsFailure)
                return Result<RunState>.Failure(entry.Error);
            state = entry.Value;
        }

        using (_sessionGates.Enter(state.RunId))
        {
            if (_runs.ContainsKey(state.RunId))
                return Result<RunState>.Failure($"Run already exists for the deterministic inputs: {state.RunId}");

            var persisted = Persist(
                state,
                RunCommandTypes.StartRun,
                options with
                {
                    RunDefinitionId = effectiveRunDefinitionId,
                    Seed = seed,
                    ContentRevision = contentRevision,
                    StartingCards = startingCards,
                    InitialPlayableCardCount = initialPlayableCardCount,
                    Scenario = options.Scenario,
                    SettingId = options.SettingId ?? options.ConfigName
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
        if (initialZoneSteps.Length > 0)
            _eventBus?.Publish(new CardZonesTransitionedEvent(
                state.RunId,
                "run.started",
                CanonicalJson.ComputeHash(state.Deck.Topology),
                initialZoneSteps));
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
        if (_contentPublications != null && _contentRuntimes != null)
        {
            if (string.IsNullOrWhiteSpace(options.ContentRevision))
                return Result<ResolvedContentManifest>.Failure("Published content revision is required");
            var published = _contentPublications.GetPublishedAsync(options.ContentRevision).GetAwaiter().GetResult();
            if (published.IsFailure)
                return Result<ResolvedContentManifest>.Failure(published.Error);
            var settingId = options.SettingId ?? options.ConfigName;
            if (!string.Equals(published.Value.Manifest.ConfigName, settingId, StringComparison.Ordinal))
            {
                return Result<ResolvedContentManifest>.Failure(
                    $"Content revision '{options.ContentRevision}' belongs to setting " +
                    $"'{published.Value.Manifest.ConfigName}', not '{settingId}'");
            }
            var registration = _contentManifestProvider?.RegisterPublishedManifest(published.Value.Manifest);
            if (registration is { IsFailure: true })
                return Result<ResolvedContentManifest>.Failure(registration.Error);
            return Result<ResolvedContentManifest>.Success(new(
                published.Value.Manifest.Revision,
                published.Value.Manifest));
        }

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
        if (_runs.TryGetValue(runId, out var state))
            return Result<RunState>.Success(state);

        // Cache miss — try loading from repository
        if (_repository != null)
        {
            var loaded = _repository.LoadLatestStateAsync(runId).GetAwaiter().GetResult();
            if (loaded != null)
            {
                var compatibility = ValidateLoadedRunCompatibility(loaded);
                if (compatibility.IsFailure)
                    return Result<RunState>.Failure(compatibility.Error);

                _runs[runId] = loaded;
                return Result<RunState>.Success(loaded);
            }
        }

        return Result<RunState>.Failure($"Run not found: {runId}");
    }

    public Result<IReadOnlyList<RunAvailableCommand>> GetAvailableCommands(Guid runId)
    {
        var run = GetRun(runId);
        if (run.IsFailure)
            return Result<IReadOnlyList<RunAvailableCommand>>.Failure(run.Error);
        var commands = _progression.GetAvailableCommands(run.Value);
        if (commands.IsFailure)
            return commands;
        if (!commands.Value.Any(command => CardTransformationAccess.IsCommand(command.Type)))
            return commands;
        var options = GetCardTransformationOptions(run.Value, null);
        if (options.IsFailure) return Result<IReadOnlyList<RunAvailableCommand>>.Failure(options.Error);
        var enriched = commands.Value.Select(command =>
            CardTransformationAccess.IsCommand(command.Type)
                ? command with
                {
                    ValidPayload = JsonSerializer.SerializeToElement(new
                    {
                        options = options.Value.Where(option => CardTransformationAccess.CommandType(option.Operation) == command.Type).Select(option => new
                        {
                            cardInstanceId = option.CardInstanceId,
                            cardDefinitionId = option.CardDefinitionId,
                            upgradeId = option.UpgradeId,
                            transformationId = option.TargetTransformationId,
                            category = option.Category.ToString(),
                            slotId = option.SlotId
                        }).ToArray()
                    })
                }
                : command).ToArray();
        return Result<IReadOnlyList<RunAvailableCommand>>.Success(enriched);
    }

    public Result<IReadOnlyList<CardTransformationOption>> GetCardTransformationOptions(Guid runId, Guid? cardInstanceId = null)
    {
        var run = GetRun(runId);
        if (run.IsFailure) return Result<IReadOnlyList<CardTransformationOption>>.Failure(run.Error);
        return GetCardTransformationOptions(run.Value, cardInstanceId);
    }

    private Result<IReadOnlyList<CardTransformationOption>> GetCardTransformationOptions(RunState run, Guid? cardInstanceId)
    {
        if (_contentRuntimes == null) return Result<IReadOnlyList<CardTransformationOption>>.Failure("Pinned content runtime is not configured");
        var runtime = _contentRuntimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        return runtime.IsFailure ? Result<IReadOnlyList<CardTransformationOption>>.Failure(runtime.Error)
            : new CardTransformationPlanner(runtime.Value).Options(run, cardInstanceId);
    }

    public Result<RunCommandReceipt?> FindReceipt(Guid runId, Guid commandId)
    {
        if (commandId == Guid.Empty)
            return Result<RunCommandReceipt?>.Failure("Command id is required");

        {
            if (_commandReceipts.TryGetValue((runId, commandId), out var cached))
                return Result<RunCommandReceipt?>.Success(cached with { Duplicate = true });

            if (_repository == null)
                return Result<RunCommandReceipt?>.Success(null);

            try
            {
                var commit = _repository.FindCommandAsync(runId, commandId)
                    .GetAwaiter().GetResult();
                if (commit == null)
                    return Result<RunCommandReceipt?>.Success(null);

                var receipt = CreateReceipt(commit, duplicate: true);
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

    public Result<RunCommandReceipt> Execute(Guid runId, GameplayCommandEnvelope command)
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

            _directCommandScopes[runId] = command;
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
                _directCommandScopes.TryRemove(runId, out _);
            }
        }
    }

    internal Result ExecuteCommandTransition(Guid runId, string commandType, JsonElement payload)
    {
        return commandType switch
        {
            RunCommandTypes.AdvanceNode => ToResult(AdvanceNode(
                runId,
                DeserializePayload<AdvanceNodeCommand>(payload).TargetNodeId)),
            RunCommandTypes.ResolveNode => ToResult(ResolveCurrentNode(
                runId,
                DeserializePayload<ResolveNodeCommand>(payload).CurrentNodeId)),
            RunCommandTypes.CreateCardSelection => ExecuteCreateCardSelection(runId, payload),
            RunCommandTypes.PickCardReward => ExecutePickCardReward(runId, payload),
            RunCommandTypes.RerollCardReward => ExecuteRerollCardReward(runId, payload),
            RunCommandTypes.DecomposeCardReward => ExecuteDecomposeCardReward(runId, payload),
            RunCommandTypes.CreateShop => ExecuteCreateShop(runId, payload),
            RunCommandTypes.BuyShopItem => ExecuteBuyShopItem(runId, payload),
            RunCommandTypes.RerollShop => ExecuteRerollShop(runId, payload),
            RunCommandTypes.CreatePreparation => ExecuteCreatePreparation(runId, payload),
            RunCommandTypes.StartDialogue => ExecuteDialogue(runId, payload, start: true),
            RunCommandTypes.ChooseDialogueOption => ExecuteDialogue(runId, payload, start: false),
            RunCommandTypes.ApplyPreparationOption => ExecutePreparationOption(runId, payload),
            RunCommandTypes.AcquireRelic => ExecuteAcquireRelic(runId, payload),
            RunCommandTypes.RemoveRelic => ExecuteRemoveRelic(runId, payload),
            RunCommandTypes.UpgradeCard => ExecuteUpgradeCard(runId, payload),
            RunCommandTypes.RemoveCardTransformation => ExecuteRemoveCardTransformation(runId, payload),
            RunCommandTypes.ReplaceCardTransformation => ExecuteReplaceCardTransformation(runId, payload),
            RunCommandTypes.ActivateContentRevision => ExecuteActivateContentRevision(runId, payload),
            RunCommandTypes.ApplyRunResource => ExecuteApplyRunResource(runId, payload),
            RunCommandTypes.InvokeCardZoneFlow => ExecuteInvokeCardZoneFlow(runId, payload),
            RunCommandTypes.InvokeCardZoneGameplayFlow => ExecuteInvokeCardZoneGameplayFlow(runId, payload),
            RunCommandTypes.AbandonRun => ExecuteAbandonRun(runId),
            RunCommandTypes.ResolveCombat => Result.Failure(
                "RESOLVE_COMBAT must be executed through the run encounter coordinator"),
            RunCommandTypes.RestoreHeadFromHistory => ExecuteRestoreHeadFromHistory(runId, payload),
            RunCommandTypes.StartEncounter => Result.Failure(
                "START_ENCOUNTER must be executed through the run encounter coordinator"),
            _ => Result.Failure($"Unsupported run command type: {commandType}")
        };
    }

    private Result ExecuteDialogue(Guid runId, JsonElement payload, bool start)
    {
        var run = _runs[runId];
        Result<RunState> transition;
        if (start)
        {
            var request = DeserializePayload<Dialogue.StartDialogueCommand>(payload);
            if (_contentDefinitions == null || string.IsNullOrWhiteSpace(request.DialogueId))
                return Result.Failure("A published dialogue definition is required");
            var definition = _contentDefinitions.Resolve<Dialogue.DialogueDefinition>(run, "dialogues", request.DialogueId);
            if (definition.IsFailure) return Result.Failure(definition.Error);
            transition = Dialogue.DialogueTransitions.Start(run, definition.Value, _activityEffects);
        }
        else
            transition = Dialogue.DialogueTransitions.Choose(run, DeserializePayload<Dialogue.ChooseDialogueOptionCommand>(payload), _activityEffects);
        return transition.IsFailure ? Result.Failure(transition.Error) : ToResult(Persist(transition.Value,
            start ? RunCommandTypes.StartDialogue : RunCommandTypes.ChooseDialogueOption, payload));
    }

    private Result ExecuteAbandonRun(Guid runId)
    {
        var transition = _progression.Abandon(_runs[runId]);
        return transition.IsFailure
            ? Result.Failure(transition.Error)
            : ToResult(Persist(transition.Value, RunCommandTypes.AbandonRun, new { }));
    }

    private Result ExecuteCreateCardSelection(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<CardSelectionCommand>(payload);
        return ToResult(CreateCardSelection(runId, request.SelectionId));
    }

    private Result ExecuteApplyRunResource(Guid runId, JsonElement payload)
    {
        var capability = RequireCapability(
            runId,
            policy => policy.AllowRunResourceCheats,
            "run resource cheats");
        if (capability.IsFailure)
            return capability;
        var request = DeserializePayload<RunResourceCommand>(payload);
        return ToResult(ApplyRunResource(
            runId,
            request.ResourceId,
            request.Value,
            request.Operation,
            request.Field));
    }

    private Result ExecuteInvokeCardZoneFlow(Guid runId, JsonElement payload)
    {
        var capability = RequireCapability(runId,
            policy => policy.AllowCardZoneCheats, "card zone tools");
        if (capability.IsFailure) return capability;
        var request = DeserializePayload<CardZoneFlowCommand>(payload);
        if (string.IsNullOrWhiteSpace(request.FlowId))
            return Result.Failure("Card-zone flow id is required");
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result.Failure($"Run not found: {runId}");
            if (request.CardDefinitionIds is { Count: > 0 } definitions)
            {
                if (_contentDefinitions == null)
                    return Result.Failure("Pinned card content is required for zone creation");
                foreach (var definitionId in definitions)
                {
                    if (string.IsNullOrWhiteSpace(definitionId))
                        return Result.Failure("Card definition id cannot be empty");
                    var card = _contentDefinitions.Resolve<CardContentDefinition>(
                        state, "cards", definitionId);
                    if (card.IsFailure)
                        return Result.Failure(card.Error);
                }
            }
            var flowed = CardZoneRunFlowDispatcher.InvokeTool(_cardZoneFlows, state,
                request.FlowId, request.CardInstanceIds ?? [],
                request.CardDefinitionIds ?? [], request.ActorId);
            if (flowed.IsFailure) return Result.Failure(flowed.Error);
            if (flowed.Value.Steps.IsEmpty)
                return Result.Failure("Card-zone tool flow produced no transition");
            var candidate = state with
            {
                Deck = new DeckState { Topology = flowed.Value.State },
                Determinism = flowed.Value.Context.AdvanceStep()
            };
            return ToResult(Persist(candidate, RunCommandTypes.InvokeCardZoneFlow, payload));
        }
    }

    private Result ExecuteInvokeCardZoneGameplayFlow(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<GameplayCardZoneFlowCommand>(payload);
        if (string.IsNullOrWhiteSpace(request.FlowId))
            return Result.Failure("Card-zone gameplay flow id is required");
        if (request.RequestedCount is < 0)
            return Result.Failure("Requested card count cannot be negative");
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result.Failure($"Run not found: {runId}");
            var selected = request.CardInstanceIds ?? [];
            if (selected.Count != selected.Distinct().Count())
                return Result.Failure("Card instance selected more than once");
            var visible = CardZoneReadModel.Project(state).Zones
                .SelectMany(zone => zone.Cards)
                .Select(card => card.CardInstanceId)
                .ToHashSet();
            if (selected.Any(id => !visible.Contains(id)))
                return Result.Failure("Selected card instance is not visible in this run");
            var flowed = CardZoneRunFlowDispatcher.InvokeGameplay(_cardZoneFlows,
                state, request.FlowId, selected, request.RequestedCount ?? selected.Count);
            if (flowed.IsFailure) return Result.Failure(flowed.Error);
            if (flowed.Value.Steps.IsEmpty)
                return Result.Failure("Card-zone gameplay flow produced no transition");
            var candidate = state with
            {
                Deck = new DeckState { Topology = flowed.Value.State },
                Determinism = flowed.Value.Context.AdvanceStep()
            };
            return ToResult(Persist(candidate, RunCommandTypes.InvokeCardZoneGameplayFlow, payload));
        }
    }

    private Result RequireCapability(
        Guid runId,
        Func<CapabilityPolicyDefinition, bool> allows,
        string capabilityName)
    {
        if (!_runs.TryGetValue(runId, out var state))
            return Result.Failure($"Run not found: {runId}");
        var policy = state.ResolvedMode?.CapabilityPolicy;
        return policy != null && allows(policy)
            ? Result.Success()
            : Result.Failure($"Game mode does not allow {capabilityName}: {state.ModeId ?? "unresolved"}");
    }

    private Result ExecutePickCardReward(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<CardSelectionCardsCommand>(payload);
        return ToResult(PickCards(runId, request.SelectionInstanceId, request.CardIds));
    }

    private Result ExecuteRerollCardReward(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<RerollCardSelectionCommand>(payload);
        return ToResult(RerollCardSelection(runId, request.SelectionInstanceId, request.LockedCardIds));
    }

    private Result ExecuteDecomposeCardReward(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<CardSelectionItemCommand>(payload);
        return ToResult(DecomposeCardSelectionOption(runId, request.SelectionInstanceId, request.CardId));
    }

    private Result ExecuteCreateShop(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<ShopDefinitionCommand>(payload);
        return ToResult(CreateShop(runId, request.ShopId));
    }

    private Result ExecuteBuyShopItem(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<ShopItemCommand>(payload);
        return ToResult(BuyShopItem(runId, request.ShopInstanceId, request.ItemId));
    }

    private Result ExecuteRerollShop(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<ShopCommand>(payload);
        return ToResult(RerollShop(runId, request.ShopInstanceId));
    }

    private Result ExecuteCreatePreparation(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<PreparationDefinitionCommand>(payload);
        return ToResult(CreatePreparation(runId, request.PreparationId));
    }

    private Result ExecutePreparationOption(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<PreparationCommand>(payload);
        return ToResult(ApplyPreparationOption(runId, request.PreparationInstanceId, request.OptionId));
    }

    private Result ExecuteAcquireRelic(Guid runId, JsonElement payload)
    {
        if (_contentDefinitions == null)
            return Result.Failure("Pinned content runtime is not configured");
        var request = DeserializePayload<RelicCommand>(payload);
        var run = _runs[runId];
        var definition = _contentDefinitions.Resolve<RelicDefinition>(
            run,
            "relics",
            request.RelicId);
        if (definition.IsFailure)
            return Result.Failure(definition.Error);
        if (!string.Equals(definition.Value.RelicId, request.RelicId, StringComparison.Ordinal))
            return Result.Failure($"Relic definition identity mismatch: {request.RelicId}");

        var transition = RelicTransitions.Acquire(run, definition.Value);
        if (transition.IsFailure)
            return Result.Failure(transition.Error);
        var currentNode = run.Map.Nodes.FirstOrDefault(node =>
            string.Equals(node.NodeId, run.CurrentNodeId, StringComparison.Ordinal));
        var next = transition.Value.State;
        if (currentNode?.Activity.Type == RunActivityType.RelicReward)
        {
            next = next with
            {
                CompletedActivityNodeIds = next.CompletedActivityNodeIds
                    .Append(currentNode.NodeId)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            };
        }
        return ToResult(Persist(
            next,
            RunCommandTypes.AcquireRelic,
            new { relicId = request.RelicId }));
    }

    private Result ExecuteRemoveRelic(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<RelicInstanceCommand>(payload);
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
        var request = DeserializePayload<CardUpgradeCommand>(payload);
        return ExecuteCardTransformation(runId, payload, request.CardInstanceId,
            CardTransformationOperation.Apply, null, request.UpgradeId);
    }

    private Result ExecuteRemoveCardTransformation(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<CardTransformationRemoveCommand>(payload);
        return ExecuteCardTransformation(runId, payload, request.CardInstanceId,
            CardTransformationOperation.Remove, request.TransformationId, null);
    }

    private Result ExecuteReplaceCardTransformation(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<CardTransformationReplaceCommand>(payload);
        return ExecuteCardTransformation(runId, payload, request.CardInstanceId,
            CardTransformationOperation.Replace, request.TransformationId, request.UpgradeId);
    }

    private Result ExecuteCardTransformation(Guid runId, JsonElement payload, Guid cardInstanceId,
        CardTransformationOperation operation, ulong? target, string? upgradeId)
    {
        if (_contentRuntimes == null) return Result.Failure("Pinned content runtime is not configured");
        var run = _runs[runId];
        var currentNode = run.Map.Nodes.FirstOrDefault(node =>
            string.Equals(node.NodeId, run.CurrentNodeId, StringComparison.Ordinal));
        var runtime = _contentRuntimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure) return Result.Failure(runtime.Error);
        var planned = new CardTransformationPlanner(runtime.Value).Plan(run, cardInstanceId, operation, target, upgradeId);
        if (planned.IsFailure) return Result.Failure(planned.Error);
        var topology = run.Deck.Topology with { Instances = run.Deck.Topology.InstanceItems.SetItem(cardInstanceId, planned.Value) };
        var valid = CardZoneTopologyValidator.Validate(topology);
        if (valid.IsFailure) return valid;
        var candidate = run with
        {
            Deck = new DeckState { Topology = topology },
            Determinism = run.Determinism.AdvanceStep(),
            CompletedActivityNodeIds = currentNode?.Activity.Type == RunActivityType.CardUpgrade
                ? run.CompletedActivityNodeIds
                    .Append(currentNode.NodeId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
                    .ToArray()
                : run.CompletedActivityNodeIds
        };
        return ToResult(Persist(
            candidate,
            CardTransformationAccess.CommandType(operation), payload));
    }

    private Result ExecuteRestoreHeadFromHistory(Guid runId, JsonElement payload)
    {
        if (_history == null)
            return Result.Failure("Run persistence is not configured");

        var current = GetRun(runId);
        if (current.IsFailure)
            return Result.Failure(current.Error);
        if (current.Value.ResolvedMode?.ReplayPolicy.AllowHeadRestore != true)
            return Result.Failure($"Game mode does not allow restoring history: {current.Value.ModeId}");

        var request = DeserializePayload<RestoreHeadFromHistoryCommand>(payload);
        if (request.SourceSequence < 1 || request.SourceSequence >= current.Value.Sequence)
            return Result.Failure("Restore source sequence must reference an earlier commit");
        var state = _history.LoadStateAsync(runId, request.SourceSequence).GetAwaiter().GetResult();
        return state == null
            ? Result.Failure($"Run commit not found: {runId}/{request.SourceSequence}")
            : ToResult(RestoreHeadFromHistory(current.Value, state, request.SourceSequence));
    }

    private Result ExecuteActivateContentRevision(Guid runId, JsonElement payload)
    {
        var request = DeserializePayload<ContentRevisionCommand>(payload);
        if (string.IsNullOrWhiteSpace(request.Revision))
            return Result.Failure("Content revision is required");
        if (!_runs.TryGetValue(runId, out var state))
            return Result.Failure($"Run not found: {runId}");

        var manifest = ResolveKnownManifest(request.Revision.Trim());
        if (manifest.IsFailure)
            return Result.Failure(manifest.Error);
        if (!string.Equals(manifest.Value.ConfigName, state.ConfigName, StringComparison.OrdinalIgnoreCase))
            return Result.Failure("Content revision belongs to a different configuration");
        if (string.Equals(state.Determinism.ContentRevision, manifest.Value.Revision, StringComparison.Ordinal))
            return Result.Success();

        var prepared = PrepareContentRevisionActivation(state, manifest.Value);
        if (prepared.IsFailure)
            return Result.Failure(prepared.Error);
        return ToResult(Persist(
            prepared.Value,
            RunCommandTypes.ActivateContentRevision,
            new { revision = manifest.Value.Revision }));
    }

    public Result<ContentRevisionActivationPreview> Preview(Guid runId, string targetRevision)
    {
        if (string.IsNullOrWhiteSpace(targetRevision))
            return Result<ContentRevisionActivationPreview>.Failure("Content revision is required");
        var current = GetRun(runId);
        if (current.IsFailure)
            return Result<ContentRevisionActivationPreview>.Failure(current.Error);
        var manifest = ResolveKnownManifest(targetRevision.Trim());
        if (manifest.IsFailure)
            return Result<ContentRevisionActivationPreview>.Failure(manifest.Error);
        if (!string.Equals(manifest.Value.ConfigName, current.Value.ConfigName, StringComparison.OrdinalIgnoreCase))
            return Result<ContentRevisionActivationPreview>.Failure(
                "Content revision belongs to a different configuration");

        var prepared = string.Equals(
                current.Value.Determinism.ContentRevision,
                manifest.Value.Revision,
                StringComparison.Ordinal)
            ? Result<RunState>.Success(current.Value)
            : PrepareContentRevisionActivation(current.Value, manifest.Value);
        var boundary = current.Value.ResolvedMode?.ContentBindingPolicy.ActivationBoundary.ToString()
            ?? "unavailable";
        return Result<ContentRevisionActivationPreview>.Success(new ContentRevisionActivationPreview(
            runId,
            current.Value.Determinism.ContentRevision,
            manifest.Value.Revision,
            boundary,
            prepared.IsSuccess,
            prepared.IsFailure ? [prepared.Error] : [],
            [],
            CompareArtifacts(current.Value.ContentManifest, manifest.Value)));
    }

    private Result<RunState> PrepareContentRevisionActivation(RunState state, ContentManifest manifest)
    {
        var mode = state.ResolvedMode;
        if (mode == null)
            return Result<RunState>.Failure("Content revision activation requires a resolved game mode");
        if (!mode.CapabilityPolicy.AllowHotReloadActivation)
            return Result<RunState>.Failure($"Game mode does not allow content revision activation: {state.ModeId}");
        if (mode.ContentBindingPolicy.ActiveRuns != ActiveRunContentBinding.Versioned)
        {
            return Result<RunState>.Failure("Game mode pins content for active runs");
        }
        if (mode.ContentBindingPolicy.ActivationBoundary != ContentActivationBoundary.OutsideCombat)
        {
            return Result<RunState>.Failure("Unsupported content activation boundary");
        }
        if (state.GetActiveEncounter()?.Combat.IsActive == true)
        {
            return Result<RunState>.Failure(
                "Content revision activation is only safe outside an active combat");
        }

        var activationState = state;
        if (_contentRuntimes != null)
        {
            var targetRuntime = _contentRuntimes.Resolve(manifest.Revision, state.ConfigName);
            if (targetRuntime.IsFailure)
                return Result<RunState>.Failure($"Target content revision is unavailable: {targetRuntime.Error}");
            var compatibility = RunContentCompatibilityValidator.PrepareForActivation(
                state,
                targetRuntime.Value);
            if (compatibility.IsFailure)
                return Result<RunState>.Failure(compatibility.Error);
            activationState = compatibility.Value;
        }
        else if (RunContentCompatibilityValidator.RequiresRuntime(state))
        {
            return Result<RunState>.Failure(
                "Content runtime resolver is required to activate a revision for content-bound state");
        }

        var activatedMode = state.ResolvedMode;
        if (!string.IsNullOrWhiteSpace(state.ModeId) &&
            _gameModeResolver is IRevisionedGameModeResolver revisionedModes)
        {
            var resolved = revisionedModes.Resolve(
                state.ModeId,
                state.ConfigName,
                manifest.Revision);
            if (resolved.IsFailure)
                return Result<RunState>.Failure(
                    $"Target content revision has an invalid game mode graph: {resolved.Error}");
            activatedMode = resolved.Value;
        }

        var candidate = activationState with
        {
            ContentManifest = manifest,
            ResolvedMode = activatedMode,
            Determinism = state.Determinism
                .WithContentRevision(manifest.Revision)
                .AdvanceStep()
        };
        return Result<RunState>.Success(candidate);
    }

    private static IReadOnlyList<ContentArtifactChange> CompareArtifacts(
        ContentManifest? current,
        ContentManifest target)
    {
        var previous = (current?.Artifacts ?? [])
            .ToDictionary(item => item.Path, StringComparer.Ordinal);
        var next = target.Artifacts.ToDictionary(item => item.Path, StringComparer.Ordinal);
        return previous.Keys
            .Union(next.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                previous.TryGetValue(path, out var before);
                next.TryGetValue(path, out var after);
                if (before == null)
                    return new ContentArtifactChange(path, after!.Kind, "added", null, after.Hash);
                if (after == null)
                    return new ContentArtifactChange(path, before.Kind, "removed", before.Hash, null);
                return string.Equals(before.Hash, after.Hash, StringComparison.Ordinal)
                    ? null
                    : new ContentArtifactChange(path, after.Kind, "modified", before.Hash, after.Hash);
            })
            .Where(change => change != null)
            .Cast<ContentArtifactChange>()
            .ToArray();
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
                var loaded = _repository.LoadLatestStateAsync(runId).GetAwaiter().GetResult();
                if (loaded?.GetEncounter(combatId) == null)
                    continue;

                var compatibility = ValidateLoadedRunCompatibility(loaded);
                if (compatibility.IsFailure)
                    return Result<RunState>.Failure(compatibility.Error);

                _runs[runId] = loaded;
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
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunMapNodeState>.Failure($"Run not found: {runId}");

            var current = state.Map.Nodes.FirstOrDefault(node =>
                string.Equals(node.NodeId, state.CurrentNodeId, StringComparison.Ordinal));
            if (current?.Activity.Type == RunActivityType.Encounter)
            {
                return Result<RunMapNodeState>.Failure(
                    $"Encounter map nodes must be resolved through their combat: {current.NodeId}");
            }

            if (current == null)
                return Result<RunMapNodeState>.Failure($"Map node not found: {currentNodeId}");
            var canResolve = _progression.CanResolve(state, current);
            if (canResolve.IsFailure)
                return Result<RunMapNodeState>.Failure(canResolve.Error);
            var exit = ApplyActivityBoundary(state, current, RunActivityBoundary.Exit);
            if (exit.IsFailure)
                return Result<RunMapNodeState>.Failure(exit.Error);
            var transition = RunMapTransitions.Resolve(exit.Value, currentNodeId);
            if (transition.IsSuccess)
            {
                transition = Result<RunStateTransition<RunMapNodeState>>.Success(
                    transition.Value with
                    {
                        State = _progression.ApplyNodeExit(transition.Value.State, current)
                    });
            }
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
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunMapNodeState>.Failure($"Run not found: {runId}");

            var transition = RunMapTransitions.Advance(state, targetNodeId);
            if (transition.IsSuccess)
            {
                var entry = ApplyActivityBoundary(
                    transition.Value.State,
                    transition.Value.Value,
                    RunActivityBoundary.Entry);
                if (entry.IsFailure)
                    return Result<RunMapNodeState>.Failure(entry.Error);
                transition = Result<RunStateTransition<RunMapNodeState>>.Success(
                    transition.Value with { State = entry.Value });
            }
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
        RunCommandIdentity? commandIdentity = null,
        RunState? initializedRun = null,
        RunEncounterStartCommand? initialCommand = null,
        CombatState? stateBeforeInitialization = null,
        CombatResolutionStep? initializationStep = null,
        JsonElement rootPayload = default)
    {
        if (combatState == null)
            return Result<RunState>.Failure("Combat state is required");

        using (_sessionGates.Enter(runId))
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
            if (currentNode.Activity.Type != RunActivityType.Encounter)
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
            if (stateBeforeInitialization != null &&
                (stateBeforeInitialization.CombatId != combatState.CombatId ||
                 stateBeforeInitialization.RunId != runId ||
                 stateBeforeInitialization.Determinism.Seed != combatState.Determinism.Seed))
                return Result<RunState>.Failure("Pre-initialization combat state does not match the encounter");

            var encounter = new RunEncounterState
            {
                NodeId = currentNode.NodeId,
                Combat = combatState
            };
            var encounterDeck = Result<CardZoneRunTransition>.Success(
                new CardZoneRunTransition(initializedRun?.Deck ?? state.Deck,
                    initializedRun?.Determinism ?? seed.Context, []));
            if (initializedRun != null && (initializedRun.RunId != state.RunId ||
                initializedRun.Determinism.ContentRevision != state.Determinism.ContentRevision ||
                initializedRun.Determinism.Seed != state.Determinism.Seed))
                return Result<RunState>.Failure("Initialized run snapshot does not match encounter owner");
            if (initializedRun == null)
            {
                var flowed = CardZoneRunFlowDispatcher.Execute(_cardZoneFlows, state,
                    state.Deck, seed.Context, "encounter.started");
                if (flowed.IsFailure)
                    return Result<RunState>.Failure(flowed.Error);
                encounterDeck = Result<CardZoneRunTransition>.Success(new CardZoneRunTransition(
                    new DeckState { Topology = flowed.Value.State }, flowed.Value.Context, []));
            }
            var candidate = state with
            {
                ActiveEncounterId = combatState.CombatId,
                Modifiers = initializedRun?.Modifiers ?? state.Modifiers,
                Relics = initializedRun?.Relics ?? state.Relics,
                ResourceState = initializedRun?.ResourceState ?? state.ResourceState,
                Encounters = state.Encounters.Add(encounter),
                Deck = encounterDeck.Value.State,
                Determinism = encounterDeck.Value.Context.AdvanceStep()
            };
            var journalCommand = initialCommand ?? new RunEncounterStartCommand(
                combatState.GetAllActors().ToArray(),
                combatState.StatusEffects.ToDictionary(
                    item => item.Key,
                    item => (IReadOnlyList<StatusEffectInstance>)item.Value.ToArray(),
                    StringComparer.Ordinal));
            CombatResolutionRecord? combatResolution = null;
            if (commandIdentity != null && initializationStep != null)
            {
                if (initializationStep.Combat.CombatId != combatState.CombatId ||
                    !string.Equals(
                        CanonicalJson.ComputeHash(initializationStep.Combat),
                        CanonicalJson.ComputeHash(combatState),
                        StringComparison.Ordinal))
                    return Result<RunState>.Failure("Initialization trace does not match the encounter state");
                var normalizedStep = initializationStep with
                {
                    Combat = combatState,
                    Deck = candidate.Deck,
                    RunSnapshot = initializedRun ?? candidate,
                    RunDeterminism = initializedRun?.Determinism ?? candidate.Determinism
                };
                combatResolution = CreateCombatResolutionRecord(
                    state,
                    stateBeforeInitialization ?? combatState,
                    commandIdentity,
                    [(candidate, normalizedStep)]);
            }
            return Persist(
                candidate,
                RunCommandTypes.StartEncounter,
                rootPayload.ValueKind == JsonValueKind.Undefined ? journalCommand : rootPayload,
                commandIdentity,
                scope: "combat",
                combatId: combatState.CombatId,
                combatResolution: combatResolution);
        }
    }

    public Result<RunState> CommitCombatResolution(CombatResolutionCommit resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(resolution.PreviousCombat);
        ArgumentNullException.ThrowIfNull(resolution.RootCommand);
        if (resolution.Steps.Count == 0)
            return Result<RunState>.Failure("Combat resolution requires at least one transition");

        using (_sessionGates.Enter(resolution.RunId))
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
            if (step.RunSnapshot is { } gameplay && (gameplay.RunId != state.RunId ||
                gameplay.Determinism.ContentRevision != state.Determinism.ContentRevision))
                return Result<RunState>.Failure("Invalid run gameplay snapshot in combat resolution");
            candidate = candidate with
            {
                Deck = step.Deck,
                ResourceState = step.RunSnapshot?.ResourceState ?? candidate.ResourceState,
                Modifiers = step.RunSnapshot?.Modifiers ?? candidate.Modifiers,
                Relics = step.RunSnapshot?.Relics ?? candidate.Relics,
                Encounters = candidate.Encounters.SetItem(
                    encounterIndex,
                    candidate.Encounters[encounterIndex] with { Combat = step.Combat }),
                Determinism = (step.RunDeterminism ?? candidate.Determinism).AdvanceStep()
            };
            candidates.Add((candidate, step));
        }

        CombatResolutionRecord? combatResolution = null;
        if (rootCommand != null)
            combatResolution = CreateCombatResolutionRecord(state, previousCombat, rootCommand, candidates);

        return PersistBatch(state, candidates, rootCommand, rootPayload, combatResolution);
    }

    private CombatResolutionRecord CreateCombatResolutionRecord(
        RunState state,
        CombatState previousCombat,
        RunCommandIdentity rootCommand,
        IReadOnlyList<(RunState State, CombatResolutionStep Step)> candidates)
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
            RunSequence = checked(state.Sequence + 1),
            CombatStep = candidate.Step.Combat.Determinism.Step,
            TransitionType = candidate.Step.TransitionType,
            Payload = candidate.Step.Payload.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(new { }, _jsonOptions)
                : candidate.Step.Payload.Clone(),
            EffectSteps = candidate.Step.EffectSteps,
            Calculations = candidate.Step.Calculations,
            Applications = candidate.Step.Applications,
            CardZoneSteps = candidate.Step.CardZoneSteps,
            StateAfter = mode == AnimationFrameMode.FullSnapshots
                ? candidate.Step.Combat
                : null,
            SnapshotSequence = checked(state.Sequence + 1)
        }).ToArray();
        return new CombatResolutionRecord
        {
            CommandId = rootCommand.CommandId,
            CombatId = previousCombat.CombatId,
            CommandType = rootCommand.Type,
            Mode = mode,
            RootSequence = frames[0].RunSequence,
            InitialCombatStateHash = CanonicalJson.ComputeHash(previousCombat),
            FinalCombatStateHash = CanonicalJson.ComputeHash(candidates[^1].Step.Combat),
            ResolutionFingerprint = CanonicalJson.ComputeHash(new
            {
                rootCommand.CommandId,
                previousCombat.CombatId,
                rootCommand.Type,
                mode,
                frames
            }),
            Frames = frames
        };
    }

    public Result<RunState> ResolveEncounter(
        Guid runId,
        int expectedSequence,
        Guid combatId,
        RunCommandIdentity? commandIdentity = null,
        JsonElement rootPayload = default)
    {
        using (_sessionGates.Enter(runId))
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

            var flowed = ExecuteCardZoneBoundary(state, "encounter.ended");
            if (flowed.IsFailure)
                return Result<RunState>.Failure(flowed.Error);
            var cleanedState = state with
            {
                Deck = new DeckState { Topology = flowed.Value.State },
                Determinism = flowed.Value.Context
            };

            var currentNode = cleanedState.Map.Nodes.First(node => node.NodeId == encounter.NodeId);
            var resolved = encounter with
            {
                Resolved = true,
                Outcome = encounter.Combat.Status.ToString()
            };
            if (_progression.ShouldRestartEncounterActivity(cleanedState, encounter.Combat.Status))
            {
                var retryable = cleanedState with
                {
                    ActiveEncounterId = null,
                    Encounters = state.Encounters.SetItem(encounterIndex, resolved),
                    Determinism = cleanedState.Determinism.AdvanceStep()
                };
                return Persist(
                    retryable,
                    RunCommandTypes.ResolveCombat,
                    rootPayload.ValueKind == JsonValueKind.Undefined
                        ? JsonSerializer.SerializeToElement(new
                    {
                        combatId,
                        nodeId = encounter.NodeId,
                        outcome = resolved.Outcome,
                        retryActivity = true,
                        combatStateHash = CanonicalJson.ComputeHash(encounter.Combat)
                    }, _jsonOptions)
                        : rootPayload,
                    commandIdentity,
                    scope: "combat",
                    combatId: combatId);
            }

            var exit = ApplyActivityBoundary(cleanedState, currentNode, RunActivityBoundary.Exit);
            if (exit.IsFailure)
                return Result<RunState>.Failure(exit.Error);
            cleanedState = exit.Value;
            var mapTransition = RunMapTransitions.Resolve(cleanedState, encounter.NodeId);
            if (mapTransition.IsFailure)
                return Result<RunState>.Failure(mapTransition.Error);

            var candidate = mapTransition.Value.State with
            {
                ActiveEncounterId = null,
                Encounters = state.Encounters.SetItem(encounterIndex, resolved)
            };
            candidate = _progression.ApplyNodeExit(candidate, mapTransition.Value.Value);
            candidate = _progression.ApplyEncounterOutcome(candidate, encounter.Combat.Status);
            return Persist(
                candidate,
                RunCommandTypes.ResolveCombat,
                rootPayload.ValueKind == JsonValueKind.Undefined
                    ? JsonSerializer.SerializeToElement(new
                {
                    combatId,
                    nodeId = encounter.NodeId,
                    outcome = resolved.Outcome,
                    combatStateHash = CanonicalJson.ComputeHash(encounter.Combat)
                }, _jsonOptions)
                    : rootPayload,
                commandIdentity,
                scope: "combat",
                combatId: combatId);
        }
    }

    private Result<RunState> ApplyActivityBoundary(
        RunState state,
        RunMapNodeState node,
        RunActivityBoundary boundary)
    {
        var configured = boundary == RunActivityBoundary.Entry ? node.EntryEffects : node.ExitEffects;
        if (configured.Count == 0)
            return Result<RunState>.Success(state);
        if (_activityEffects == null)
            return Result<RunState>.Failure("Run activity effect executor is not configured");
        var result = _activityEffects.Execute(state, node, boundary);
        return result.IsFailure
            ? Result<RunState>.Failure(result.Error)
            : Result<RunState>.Success(result.Value.State);
    }

    private Result ValidateLoadedRunCompatibility(RunState state)
    {
        if (!string.Equals(
                state.Determinism.EngineVersion,
                DeterministicContext.CurrentEngineVersion,
                StringComparison.Ordinal))
        {
            return Result.Failure(
                $"Engine version unavailable for run {state.RunId}: " +
                $"{state.Determinism.EngineVersion}");
        }

        var topology = CardZoneTopologyValidator.Validate(state.Deck.Topology);
        if (topology.IsFailure)
            return topology;

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

    public Result<RunState> ApplyRunResource(
        Guid runId,
        string resourceId,
        float value,
        ResourceEffectOperation operation,
        ResourceValueField field = ResourceValueField.Current)
    {
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunState>.Failure($"Run not found: {runId}");

            var pool = state.ResourceState.Get(resourceId);
            if (pool == null)
                return Result<RunState>.Failure($"Run resource not found: {resourceId}");
            var changed = RunResourceTransitions.Apply(
                state.ResourceState,
                resourceId,
                value,
                operation,
                field,
                $"run-resource:{state.Sequence + 1}:{resourceId}");
            if (changed.IsFailure)
                return Result<RunState>.Failure(changed.Error);
            state = state with { ResourceState = changed.Value.State };
            var mutation = changed.Value.Records.Single();

            state = state with { Determinism = state.Determinism.AdvanceStep() };
            var persisted = Persist(
                state,
                RunCommandTypes.ApplyRunResource,
                new { resourceId, value, operation, field });
            if (persisted.IsFailure)
                return Result<RunState>.Failure(persisted.Error);
            state = persisted.Value;
            _eventBus?.Publish(new RunResourceChangedEvent(
                runId,
                resourceId,
                mutation.Field,
                mutation.Operation,
                mutation.PreviousValue,
                mutation.CurrentValue));
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

    public Result<CardSelectionState> CreateCardSelection(Guid runId, string selectionId)
    {
        using (_sessionGates.Enter(runId))
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
            var generated = _offerGenerator.GenerateCardSelection(
                state,
                definition,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (generated.IsFailure)
                return Result<CardSelectionState>.Failure(generated.Error);
            var transition = CardSelectionTransitions.Create(
                state with { Determinism = generated.Value.Context },
                definition,
                generated.Value.Options,
                generated.Value.Fingerprint,
                state.CurrentNodeId ?? string.Empty);
            return CommitTransition(
                transition,
                RunCommandTypes.CreateCardSelection,
                new { selectionId });
        }
    }

    public Result<CardSelectionState> PickCards(Guid runId, Guid selectionInstanceId, IReadOnlyList<string> cardIds)
    {
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var transition = CardSelectionTransitions.Pick(state, selectionInstanceId, cardIds, _cardZoneFlows);
            return transition.IsFailure
                ? Result<CardSelectionState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    RunCommandTypes.PickCardReward,
                    new { selectionInstanceId, cardIds });
        }
    }

    public Result<CardSelectionState> RerollCardSelection(Guid runId, Guid selectionInstanceId, IReadOnlyList<string>? lockedCardIds = null)
    {
        using (_sessionGates.Enter(runId))
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
            var generated = _offerGenerator.GenerateCardSelection(state, definitionResult.Value, locked);
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
                    RunCommandTypes.RerollCardReward,
                    new { selectionInstanceId, lockedCardIds });
        }
    }

    public Result<CardSelectionState> DecomposeCardSelectionOption(Guid runId, Guid selectionInstanceId, string cardId)
    {
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var transition = CardSelectionTransitions.Decompose(state, selectionInstanceId, cardId);
            return transition.IsFailure
                ? Result<CardSelectionState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    RunCommandTypes.DecomposeCardReward,
                    new { selectionInstanceId, cardId });
        }
    }

    public Result<ShopState> CreateShop(Guid runId, string shopId)
    {
        using (_sessionGates.Enter(runId))
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
            var generated = _offerGenerator.GenerateShop(state, definition);
            if (generated.IsFailure)
                return Result<ShopState>.Failure(generated.Error);
            var transition = ShopTransitions.Create(
                state with { Determinism = generated.Value.Context },
                definition,
                generated.Value.Items,
                generated.Value.Fingerprint,
                state.CurrentNodeId ?? string.Empty);
            return CommitTransition(
                transition,
                RunCommandTypes.CreateShop,
                new { shopId });
        }
    }

    public Result<ShopItemState> BuyShopItem(Guid runId, Guid shopInstanceId, string itemId)
    {
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<ShopItemState>.Failure($"Run not found: {runId}");

            var transition = ShopTransitions.Buy(state, shopInstanceId, itemId, _cardZoneFlows);
            return transition.IsFailure
                ? Result<ShopItemState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    RunCommandTypes.BuyShopItem,
                    new { shopInstanceId, itemId });
        }
    }

    public Result<ShopState> RerollShop(Guid runId, Guid shopInstanceId)
    {
        using (_sessionGates.Enter(runId))
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

            var generated = _offerGenerator.GenerateShop(state, definitionResult.Value);
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
                    RunCommandTypes.RerollShop,
                    new { shopInstanceId });
        }
    }

    public Result<PreparationState> CreatePreparation(Guid runId, string preparationId)
    {
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<PreparationState>.Failure($"Run not found: {runId}");

            var definitionResult = LoadPreparationDefinition(
                state.ConfigName,
                preparationId,
                state.Determinism.ContentRevision);
            if (definitionResult.IsFailure)
                return Result<PreparationState>.Failure(definitionResult.Error);

            var transition = PreparationTransitions.Create(
                state,
                definitionResult.Value,
                state.CurrentNodeId ?? string.Empty);
            return CommitTransition(
                transition,
                RunCommandTypes.CreatePreparation,
                new { preparationId });
        }
    }

    public Result<PreparationOptionState> ApplyPreparationOption(Guid runId, Guid preparationInstanceId, string optionId)
    {
        using (_sessionGates.Enter(runId))
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<PreparationOptionState>.Failure($"Run not found: {runId}");

            var plan = PreparationTransitions.PlanApply(state, preparationInstanceId, optionId);
            if (plan.IsFailure)
                return Result<PreparationOptionState>.Failure(plan.Error);
            if (!plan.Value.ModifierGrants.IsEmpty && _scriptModifierCatalog == null)
                return Result<PreparationOptionState>.Failure(
                    "Script modifier catalog is not available for preparation modifier grants");

            var resolvedModifiers = new List<ScriptModifierInstance>();
            foreach (var grant in plan.Value.ModifierGrants)
            {
                var definition = _scriptModifierCatalog!.Get(
                    grant.ModifierId,
                    state.Determinism.ContentRevision,
                    state.ConfigName);
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
                resolvedModifiers,
                _cardZoneFlows);
            if (transition.IsFailure)
                return Result<PreparationOptionState>.Failure(transition.Error);

            return CommitTransition(
                transition.Value,
                RunCommandTypes.ApplyPreparationOption,
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
            var data = _resourceLoader.LoadResource($"runs/{runDefinitionId}.json", chain, strictMode: true);
            if (data.Count == 0)
                return Result<RunDefinition>.Failure($"Run definition not found: {runDefinitionId}");

            if (!data.TryGetValue(runDefinitionId, out var element))
                return Result<RunDefinition>.Failure(
                    $"Run file must declare definition id: {runDefinitionId}");
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
            var data = _resourceLoader.LoadResource($"card-selections/{selectionId}.json", chain, strictMode: true);
            if (data.Count == 0)
                return Result<CardSelectionDefinition>.Failure($"Card selection definition not found: {selectionId}");

            if (!data.TryGetValue(selectionId, out var element))
                return Result<CardSelectionDefinition>.Failure(
                    $"Card selection file must declare definition id: {selectionId}");
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
            var data = _resourceLoader.LoadResource($"shops/{shopId}.json", chain, strictMode: true);
            if (data.Count == 0)
                return Result<ShopDefinition>.Failure($"Shop definition not found: {shopId}");

            if (!data.TryGetValue(shopId, out var element))
                return Result<ShopDefinition>.Failure(
                    $"Shop file must declare definition id: {shopId}");
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
            var data = _resourceLoader.LoadResource($"preparations/{preparationId}.json", chain, strictMode: true);
            if (data.Count == 0)
                return Result<PreparationDefinition>.Failure($"Preparation definition not found: {preparationId}");

            if (!data.TryGetValue(preparationId, out var element))
                return Result<PreparationDefinition>.Failure(
                    $"Preparation file must declare definition id: {preparationId}");
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

    private Result<CardZoneFlowResult> ExecuteCardZoneBoundary(
        RunState state,
        string trigger)
        => CardZoneRunFlowDispatcher.Execute(_cardZoneFlows, state, state.Deck,
            state.Determinism, trigger);

    private Result<RunState> RestoreHeadFromHistory(
        RunState current,
        RunState source,
        int sourceSequence)
    {
        using (_sessionGates.Enter(current.RunId))
        {
            var candidate = source with
            {
                RunId = current.RunId,
                Sequence = current.Sequence,
                Lineage = current.Lineage,
                Determinism = current.Determinism.AdvanceStep()
            };
            var restored = Persist(
                candidate,
                RunCommandTypes.RestoreHeadFromHistory,
                new { sourceSequence });
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
        _runs[state.RunId] = state;
        return Result<RunState>.Success(state);
    }

    /// <summary>
    /// Appends one authoritative command commit before publishing the candidate
    /// in memory. A durability failure leaves the active aggregate untouched.
    /// </summary>
    private Result<RunState> Persist(
        RunState state,
        string commandType,
        object command,
        RunCommandIdentity? commandIdentity = null,
        string scope = "run",
        Guid? combatId = null,
        CombatResolutionRecord? combatResolution = null)
    {
        _runs.TryGetValue(state.RunId, out var previous);
        try
        {
            var activeCommand = commandIdentity == null && _directCommandScopes.TryGetValue(state.RunId, out var scoped)
                ? scoped
                : null;
            var identity = commandIdentity ?? activeCommand?.Identity;
            var effectiveType = identity?.Type ?? commandType;
            var effectiveCommand = activeCommand?.Payload
                ?? JsonSerializer.SerializeToElement(command, _jsonOptions).Clone();
            var nextSequence = checked((previous?.Sequence ?? 0) + 1);
            state = state with
            {
                Sequence = nextSequence,
                Lineage = previous == null
                    ? state.Lineage ?? RunLineage.Root(state.RunId)
                    : previous.Lineage
            };
            var snapshot = state;
            var previousHash = previous == null
                ? string.Empty
                : CanonicalJson.ComputeHash(previous);
            var stateHash = CanonicalJson.ComputeHash(snapshot);
            var effectiveIdentity = NormalizeCommitIdentity(
                identity,
                snapshot,
                previous,
                effectiveType,
                effectiveCommand);
            var scopedCombat = combatId.HasValue
                ? snapshot.GetEncounter(combatId.Value)?.Combat
                : null;
            var animationFrame = combatResolution?.Frames.SingleOrDefault();
            var frames = new[]
            {
                new RunCommitFrame
                {
                    FrameIndex = 0,
                    Step = snapshot.Determinism.Step,
                    Scope = scope,
                    Kind = animationFrame?.TransitionType ?? effectiveType,
                    CombatId = combatId,
                    ActorId = scopedCombat?.ActivationState?.ActiveActorId,
                    PhaseId = scopedCombat?.PhaseState?.Cursor,
                    Round = scopedCombat?.ActivationState?.Round ?? scopedCombat?.CurrentTurn,
                    Activation = scopedCombat?.ActivationState?.ActivationNumber,
                    ResultHash = scopedCombat == null
                        ? stateHash
                        : CanonicalJson.ComputeHash(scopedCombat),
                    Resolution = animationFrame?.Payload ?? effectiveCommand,
                    ResolutionFrameId = animationFrame?.FrameId,
                    CombatStep = animationFrame?.CombatStep,
                    SnapshotSequence = animationFrame?.SnapshotSequence,
                    CombatStateAfter = animationFrame?.StateAfter,
                    EffectSteps = animationFrame?.EffectSteps ?? [],
                    Calculations = animationFrame?.Calculations ?? [],
                    Applications = animationFrame?.Applications ?? [],
                    CardZoneSteps = animationFrame?.CardZoneSteps ?? []
                }
            };
            var commit = new RunCommit
            {
                RunId = state.RunId,
                Sequence = nextSequence,
                RootCommand = effectiveIdentity,
                Command = effectiveCommand,
                PreviousStateHash = previousHash,
                StateHash = stateHash,
                BeforeStep = previous?.Determinism.Step ?? 0,
                AfterStep = snapshot.Determinism.Step,
                LogicalTimestamp = snapshot.Determinism.LogicalTimestamp.UtcDateTime,
                StateAfter = snapshot,
                Lineage = nextSequence == 1 ? snapshot.Lineage : null,
                CombatResolution = ToCommitResolution(combatResolution),
                Frames = frames,
                Facts = RunCommitFacts.FromFrames(frames)
            };
            var prepared = PreparedRunCommit.CreateVerified(commit, stateHash, previous);

            if (_repository is IPreparedRunCommitStore preparedStore)
                preparedStore.AppendPreparedAsync(prepared).GetAwaiter().GetResult();
            else
                _repository?.AppendAsync(commit).GetAwaiter().GetResult();

            _runs[state.RunId] = state;
            _commandReceipts[(state.RunId, effectiveIdentity.CommandId)] =
                CreateReceipt(prepared.Commit, duplicate: false, snapshot);
            return Result<RunState>.Success(state);
        }
        catch (Exception exception)
        {
            if (previous == null)
                _runs.TryRemove(state.RunId, out _);
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
        JsonElement rootPayload = default,
        CombatResolutionRecord? combatResolution = null)
    {
        try
        {
            if (candidates.Count == 0)
                return Result<RunState>.Failure("A run commit requires at least one transition frame");
            var sequence = checked(previous.Sequence + 1);
            var finalState = candidates[^1].State with { Sequence = sequence };
            var previousStateHash = CanonicalJson.ComputeHash(previous);
            var stateHash = CanonicalJson.ComputeHash(finalState);
            var commandPayload = rootPayload.ValueKind == JsonValueKind.Undefined
                ? candidates[^1].Step.Payload.ValueKind == JsonValueKind.Undefined
                    ? JsonSerializer.SerializeToElement(new { }, _jsonOptions)
                    : candidates[^1].Step.Payload.Clone()
                : rootPayload.Clone();
            var identity = NormalizeCommitIdentity(
                rootCommand,
                finalState,
                previous,
                rootCommand?.Type ?? candidates[^1].Step.TransitionType,
                commandPayload);
            var frames = candidates.Select((candidate, index) =>
            {
                var animationFrame = combatResolution?.Frames.ElementAtOrDefault(index);
                return new RunCommitFrame
                {
                    FrameIndex = index,
                    Step = candidate.State.Determinism.Step,
                    Scope = "combat",
                    Kind = candidate.Step.TransitionType,
                    CombatId = candidate.Step.Combat.CombatId,
                    ActorId = candidate.Step.Combat.ActivationState?.ActiveActorId,
                    PhaseId = candidate.Step.Combat.PhaseState?.Cursor,
                    Round = candidate.Step.Combat.ActivationState?.Round ?? candidate.Step.Combat.CurrentTurn,
                    Activation = candidate.Step.Combat.ActivationState?.ActivationNumber,
                    ResultHash = CanonicalJson.ComputeHash(candidate.Step.Combat),
                    Resolution = candidate.Step.Payload.ValueKind == JsonValueKind.Undefined
                        ? JsonSerializer.SerializeToElement(new { }, _jsonOptions)
                        : candidate.Step.Payload.Clone(),
                    ResolutionFrameId = animationFrame?.FrameId,
                    CombatStep = animationFrame?.CombatStep,
                    SnapshotSequence = animationFrame?.SnapshotSequence,
                    CombatStateAfter = animationFrame?.StateAfter,
                    EffectSteps = animationFrame?.EffectSteps ?? candidate.Step.EffectSteps,
                    Calculations = animationFrame?.Calculations ?? candidate.Step.Calculations,
                    Applications = animationFrame?.Applications ?? candidate.Step.Applications,
                    CardZoneSteps = animationFrame?.CardZoneSteps ?? candidate.Step.CardZoneSteps
                };
            }).ToArray();
            var commit = new RunCommit
            {
                RunId = previous.RunId,
                Sequence = sequence,
                RootCommand = identity,
                Command = commandPayload,
                PreviousStateHash = previousStateHash,
                StateHash = stateHash,
                BeforeStep = previous.Determinism.Step,
                AfterStep = finalState.Determinism.Step,
                LogicalTimestamp = finalState.Determinism.LogicalTimestamp.UtcDateTime,
                StateAfter = finalState,
                CombatResolution = ToCommitResolution(combatResolution),
                Frames = frames,
                Facts = RunCommitFacts.FromFrames(frames)
            };
            var prepared = PreparedRunCommit.CreateVerified(commit, stateHash, previous);

            if (_repository is IPreparedRunCommitStore preparedStore)
                preparedStore.AppendPreparedAsync(prepared).GetAwaiter().GetResult();
            else
                _repository?.AppendAsync(commit).GetAwaiter().GetResult();

            _runs[previous.RunId] = finalState;
            _commandReceipts[(previous.RunId, identity.CommandId)] =
                CreateReceipt(prepared.Commit, duplicate: false, finalState);
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
        RunCommit commit,
        bool duplicate,
        RunState? liveState = null)
    {
        var entry = commit.ToJournalEntry();
        return new RunCommandReceipt
        {
            CommandId = commit.RootCommand.CommandId,
            CommandType = entry.CommandType,
            Sequence = entry.Sequence,
            Step = entry.Step,
            PreviousStateHash = entry.PreviousStateHash,
            StateHash = entry.StateHash,
            State = liveState ?? commit.StateAfter
                ?? throw new InvalidOperationException("Materialized run state is required for a receipt"),
            CombatResolution = Projections.CombatResolutionProjection.FromCommit(commit),
            JournalEntry = entry,
            Frames = commit.Frames,
            Duplicate = duplicate
        };
    }

    private static RunCommandIdentity NormalizeCommitIdentity(
        RunCommandIdentity? identity,
        RunState next,
        RunState? previous,
        string commandType,
        JsonElement payload)
    {
        var payloadHash = CanonicalJson.ComputeHash(payload);
        if (identity != null && !string.IsNullOrWhiteSpace(identity.PayloadHash) &&
            !string.Equals(identity.PayloadHash, payloadHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Root command payload differs from its canonical envelope");
        if (identity != null)
            return identity with { Type = commandType, PayloadHash = payloadHash };

        return new RunCommandIdentity(
            DeterministicId.Create(
                next.Determinism.Seed,
                checked((ulong)next.Sequence),
                $"run-command:{next.RunId:N}:{commandType}:{payloadHash}"),
            commandType,
            previous?.Sequence ?? 0,
            previous?.Determinism.Step ?? 0,
            payloadHash);
    }

    private static RunCommitCombatResolution? ToCommitResolution(CombatResolutionRecord? resolution) =>
        resolution == null
            ? null
            : new RunCommitCombatResolution
            {
                CommandId = resolution.CommandId,
                CombatId = resolution.CombatId,
                CommandType = resolution.CommandType,
                Mode = resolution.Mode,
                RootSequence = resolution.RootSequence,
                InitialCombatStateHash = resolution.InitialCombatStateHash,
                FinalCombatStateHash = resolution.FinalCombatStateHash,
                ResolutionFingerprint = resolution.ResolutionFingerprint
            };

    private static bool IsSameCommand(RunJournalEntry entry, GameplayCommandEnvelope command)
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

}
