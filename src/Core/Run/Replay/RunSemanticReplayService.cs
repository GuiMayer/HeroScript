using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Abstractions.Persistence;
using Core.Calculations;
using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.Intents;
using Core.Combat.Modifiers;
using Core.Combat.TurnOrder;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Entity.Definitions;
using Core.Events;
using Core.Math;
using Core.Resources;
using Core.Run.Content;
using Core.Run.Branching;
using Core.StatusEffects;

namespace Core.Run.Replay;

public sealed record RunSemanticReplayVerification
{
    public Guid RunId { get; init; }
    public bool IsValid { get; init; }
    public bool Reexecuted { get; init; }
    public int CommandsReplayed { get; init; }
    public string ExpectedFinalHash { get; init; } = string.Empty;
    public string ActualFinalHash { get; init; } = string.Empty;
    public RunState? FinalState { get; init; }
    public ImmutableArray<string> Errors { get; init; } = [];
}

public interface IRunReplayService
{
    Task<RunSemanticReplayVerification> VerifyAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reexecutes the durable command journal in an isolated runtime and compares
/// every resulting canonical state hash. No persisted snapshot is accepted as
/// the replay result.
/// </summary>
public sealed class RunSemanticReplayService : IRunReplayService
{
    private readonly IRunCommitStore _repository;
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ICardPoolResolver _cardPoolResolver;
    private readonly ICardContentCatalog _cardContentCatalog;
    private readonly IContentManifestProvider _contentManifestProvider;
    private readonly IResourceManager _resourceManager;
    private readonly ITurnOrderCalculator _turnOrderCalculator;
    private readonly IActionManager _actionManager;
    private readonly EntityDefinitionLoader _entityDefinitionLoader;
    private readonly IActionCostEvaluator _actionCostEvaluator;
    private readonly IRuntimeFormulaEvaluator _formulaEvaluator;
    private readonly IResourceCatalog<RelicDefinition>? _relicCatalog;
    private readonly IResourceCatalog<CardUpgradeDefinition>? _cardUpgradeCatalog;
    private readonly IResourceCatalog<GameModeDefinition>? _modeCatalog;
    private readonly IGameModeResolver? _gameModeResolver;
    private readonly IContentPublicationService? _contentPublications;
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private readonly JsonSerializerOptions _jsonOptions;

    public RunSemanticReplayService(
        IRunCommitStore repository,
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ICardPoolResolver cardPoolResolver,
        ICardContentCatalog cardContentCatalog,
        IContentManifestProvider contentManifestProvider,
        IResourceManager resourceManager,
        ITurnOrderCalculator turnOrderCalculator,
        IActionManager actionManager,
        EntityDefinitionLoader entityDefinitionLoader,
        IActionCostEvaluator actionCostEvaluator,
        IRuntimeFormulaEvaluator formulaEvaluator,
        IResourceCatalog<RelicDefinition>? relicCatalog = null,
        IResourceCatalog<CardUpgradeDefinition>? cardUpgradeCatalog = null,
        IResourceCatalog<GameModeDefinition>? modeCatalog = null,
        IGameModeResolver? gameModeResolver = null,
        IContentPublicationService? contentPublications = null,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _repository = repository;
        _configManager = configManager;
        _resourceLoader = resourceLoader;
        _cardPoolResolver = cardPoolResolver;
        _cardContentCatalog = cardContentCatalog;
        _contentManifestProvider = contentManifestProvider;
        _resourceManager = resourceManager;
        _turnOrderCalculator = turnOrderCalculator;
        _actionManager = actionManager;
        _entityDefinitionLoader = entityDefinitionLoader;
        _actionCostEvaluator = actionCostEvaluator;
        _formulaEvaluator = formulaEvaluator;
        _relicCatalog = relicCatalog;
        _cardUpgradeCatalog = cardUpgradeCatalog;
        _modeCatalog = modeCatalog;
        _gameModeResolver = gameModeResolver;
        _contentPublications = contentPublications;
        _contentRuntimes = contentRuntimes;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<RunSemanticReplayVerification> VerifyAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var commits = (await _repository.LoadCommitsAsync(runId, cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(item => item.Sequence)
            .ToArray();
        if (commits.Length == 0)
            return Failure(runId, $"Run journal not found: {runId}");
        var structural = RunReplayVerifier.Verify(commits);
        if (!structural.IsValid)
            return Failure(runId, string.Join("; ", structural.Errors));

        var errors = ImmutableArray.CreateBuilder<string>();
        RunStartOptions? startOptions = null;
        RunState? branchInitialState = null;
        try
        {
            if (string.Equals(commits[0].RootCommand.Type, "run.start", StringComparison.Ordinal))
            {
                startOptions = Deserialize<RunStartOptions>(commits[0].Command);
            }
            else if (string.Equals(
                         commits[0].RootCommand.Type,
                         "run.branch.start",
                         StringComparison.Ordinal))
            {
                var branchCommand = Deserialize<RunBranchStartCommand>(commits[0].Command);
                var source = await _repository.LoadStateAsync(
                        branchCommand.ParentRunId,
                        branchCommand.SourceSequence,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (source == null)
                    return Failure(runId, "Branch source checkpoint is unavailable");
                var branch = RunBranchTransitions.Create(source, branchCommand);
                if (branch.IsFailure)
                    return Failure(runId, branch.Error);
                branchInitialState = branch.Value;
            }
            else
            {
                return Failure(runId, "Journal does not begin with run.start or run.branch.start");
            }
        }
        catch (Exception exception)
        {
            return Failure(runId, $"Invalid initial journal payload: {exception.Message}");
        }

        var runtime = CreateRuntime(startOptions?.ConfigName ?? branchInitialState!.ConfigName);
        var statesBySequence = new Dictionary<int, RunState>();
        RunState? current = null;
        var commandsReplayed = 0;

        for (var commitIndex = 0; commitIndex < commits.Length; commitIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commit = commits[commitIndex];
            var entry = commit.ToJournalEntry();
            if (entry.RunId != runId)
            {
                errors.Add($"Journal run id mismatch at sequence {entry.Sequence}");
                break;
            }

            var previousHash = current == null ? string.Empty : CanonicalJson.ComputeHash(current);
            if (!string.Equals(entry.PreviousStateHash, previousHash, StringComparison.Ordinal))
            {
                errors.Add($"Previous hash mismatch before sequence {entry.Sequence}");
                break;
            }

            var transition = commitIndex == 0
                ? startOptions != null
                    ? runtime.Runs.StartRun(startOptions)
                    : runtime.Runs.HydrateForReplay(branchInitialState!)
                : ReplayCommit(runtime, commit, statesBySequence);
            if (transition.IsFailure)
            {
                errors.Add($"Sequence {entry.Sequence} ({entry.CommandType}) failed: {transition.Error}");
                break;
            }

            current = transition.Value;
            runtime.SetRunId(current.RunId);
            commandsReplayed++;
            statesBySequence[current.Sequence] = current;
            var actualHash = CanonicalJson.ComputeHash(current);
            if (current.Sequence != entry.Sequence)
                errors.Add($"Sequence mismatch: journal {entry.Sequence}, replay {current.Sequence}");
            if (current.Determinism.Step != entry.Step)
                errors.Add($"Step mismatch at sequence {entry.Sequence}: journal {entry.Step}, replay {current.Determinism.Step}");
            if (!string.Equals(entry.StateHash, actualHash, StringComparison.Ordinal))
                errors.Add($"State hash mismatch at sequence {entry.Sequence}");
            if (errors.Count > 0)
                break;
        }

        var expectedFinalHash = commits[^1].StateHash;
        var actualFinalHash = current == null ? string.Empty : CanonicalJson.ComputeHash(current);
        return new RunSemanticReplayVerification
        {
            RunId = runId,
            IsValid = errors.Count == 0 && commandsReplayed == commits.Length,
            Reexecuted = true,
            CommandsReplayed = commandsReplayed,
            ExpectedFinalHash = expectedFinalHash,
            ActualFinalHash = actualFinalHash,
            FinalState = current,
            Errors = errors.ToImmutable()
        };
    }

    private ReplayRuntime CreateRuntime(string configName)
    {
        var contentRuntimes = _contentRuntimes ??
            (_contentPublications == null ? null : new ContentRuntimeResolver(_contentPublications));
        var modifierCatalog = contentRuntimes == null
            ? null
            : new PinnedContentCatalog<ScriptModifierDefinition>(
                contentRuntimes,
                "modifiers",
                (id, definition) => definition with
                {
                    ModifierId = string.IsNullOrWhiteSpace(definition.ModifierId) ? id : definition.ModifierId
                });
        var runs = new RunManager(
            _configManager,
            _resourceLoader,
            _cardPoolResolver,
            _cardContentCatalog,
            modifierCatalog,
            eventBus: null,
            repository: null,
            _contentManifestProvider,
            _relicCatalog,
            _cardUpgradeCatalog,
            _modeCatalog,
            _gameModeResolver,
            _contentPublications,
            contentRuntimes,
            _resourceManager);
        var combatFactory = new CombatFactory(
            _resourceManager,
            _turnOrderCalculator,
            _entityDefinitionLoader);
        var gambits = new GambitEngine(
            _configManager,
            _resourceLoader,
            contentRuntimes: contentRuntimes,
            formulas: _formulaEvaluator);
        var triggers = new EffectTriggerExecutor(_formulaEvaluator, new ImmutableEffectProcessor(), contentRuntimes,
            new CalculationEngine(), new CompositeCalculationInfluenceProvider(
            [
                new CardComponentInfluenceProvider(_formulaEvaluator),
                new EntityResourceInfluenceProvider(),
                new RunModifierInfluenceProvider(_formulaEvaluator),
                new StatusCalculationInfluenceProvider(_formulaEvaluator),
                new RelicCalculationInfluenceProvider(_formulaEvaluator),
                new GameModeCalculationInfluenceProvider(_formulaEvaluator),
                new EncounterCalculationInfluenceProvider(_formulaEvaluator)
            ]));
        var flowPlanner = contentRuntimes == null ? null : new CombatFlowPlanner(
            contentRuntimes, _actionManager, new IntentResolver(gambits, _actionManager),
            new CombatStatusLifecycle(triggers), new CombatRelicLifecycle(triggers),
            new CombatResourceLifecycle(triggers));
        var cardPlay = contentRuntimes == null ? null : new CardPlayExecutor(
            contentRuntimes, new CardContentCompiler(), new EffectiveCardResolver(),
            new CardPlayEvaluator(_actionCostEvaluator, _formulaEvaluator), triggers);
        var ability = contentRuntimes == null ? null : new AbilityExecutor(
            _actionManager, new CardPlayEvaluator(_actionCostEvaluator, _formulaEvaluator), triggers);
        var combats = new CombatRunCoordinator(
            combatFactory,
            runs,
            cardPlay,
            flowPlanner,
            gambits,
            abilityExecutor: ability);
        var gateway = new GameplayCommandGateway(
            runs,
            runs,
            combats,
            new GameEventContextAccessor(),
            GameplayCommandCodec.CreateDefault());
        return new ReplayRuntime(runs, combats, gateway);
    }

    private Result<RunState> ReplayCommit(
        ReplayRuntime runtime,
        RunCommit commit,
        IReadOnlyDictionary<int, RunState> statesBySequence)
    {
        var descriptor = GameplayCommandDescriptors.All.FirstOrDefault(item =>
            string.Equals(item.Type, commit.RootCommand.Type, StringComparison.Ordinal));
        if (descriptor != null)
        {
            // Commands issued through the public gateway carry its strict public
            // payload. Internal engine transactions created before all callers
            // reach that boundary carry a typed domain payload and are replayed
            // by the corresponding domain handler below.
            var envelope = new GameplayCommandEnvelope(commit.RootCommand, commit.Command);
            var decoded = GameplayCommandCodec.CreateDefault().Decode(envelope);
            if (decoded.IsFailure)
                return ReplayEntry(runtime, commit.ToJournalEntry(), statesBySequence);

            var combatId = commit.Frames
                .Select(frame => frame.CombatId)
                .FirstOrDefault(id => id.HasValue)
                ?? runtime.Runs.GetRun(commit.RunId).ValueOr(new RunState()).ActiveEncounterId;
            var executed = runtime.Gateway.Execute(
                commit.RunId,
                envelope,
                descriptor.Route is GameplayCommandRoute.Combat or GameplayCommandRoute.ResolveEncounter
                    ? combatId
                    : null);
            return executed.IsSuccess
                ? Result<RunState>.Success(executed.Value.Receipt.State)
                : Result<RunState>.Failure(executed.Error);
        }

        return ReplayEntry(runtime, commit.ToJournalEntry(), statesBySequence);
    }

    private Result<RunState> ReplayEntry(
        ReplayRuntime runtime,
        RunJournalEntry entry,
        IReadOnlyDictionary<int, RunState> statesBySequence)
    {
        try
        {
            var result = entry.CommandType switch
            {
                RunCommandTypes.StartEncounter => ReplayStartEncounter(runtime, entry),
                RunCommandTypes.ResolveCombat => ReplayResolveCombat(runtime, entry),
                RunCommandTypes.ResolveNode => ReplayResolveNode(runtime, entry),
                RunCommandTypes.AdvanceNode => ReplayAdvanceNode(runtime, entry),
                "COMBAT_ACTION" or "PLAY_CARD" or "EXECUTE_ACTION" or "END_TURN" =>
                    ReplayCombatAction(runtime, entry),
                "run.resource.apply" => ReplayRunResource(runtime, entry),
                "run.deck.draw" => ReplayDraw(runtime, entry),
                "run.deck.add-to-hand" => ReplayAddToHand(runtime, entry),
                "run.deck.consume" or "run.deck.move" => ReplayMoveCards(runtime, entry),
                "run.deck.shuffle-discard" => ToRun(runtime, runtime.Runs.ShuffleDiscardIntoDrawPile(entry.RunId)),
                "run.card-selection.create" => ReplayCreateSelection(runtime, entry),
                "run.card-selection.pick" => ReplayPickSelection(runtime, entry),
                "run.card-selection.reroll" => ReplayRerollSelection(runtime, entry),
                "run.card-selection.decompose" => ReplayDecomposeSelection(runtime, entry),
                "run.shop.create" => ReplayCreateShop(runtime, entry),
                "run.shop.buy" => ReplayBuyShop(runtime, entry),
                "run.shop.reroll" => ReplayRerollShop(runtime, entry),
                "run.preparation.create" => ReplayCreatePreparation(runtime, entry),
                "run.preparation.apply" => ReplayApplyPreparation(runtime, entry),
                "run.restore" or RunCommandTypes.RestoreCheckpoint =>
                    ReplayRestore(runtime, entry, statesBySequence),
                _ when entry.CommandId.HasValue => ReplayGatewayRunCommand(runtime, entry),
                _ => Result<RunState>.Failure($"Unsupported journal command: {entry.CommandType}")
            };
            return result;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return Result<RunState>.Failure($"Invalid journal payload: {exception.Message}");
        }
    }

    private Result<RunState> ReplayGatewayRunCommand(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var command = new GameplayCommandEnvelope(CreateIdentity(entry), entry.Command);
        var result = runtime.Runs.Execute(entry.RunId, command);
        return result.IsSuccess
            ? Result<RunState>.Success(result.Value.State)
            : Result<RunState>.Failure(result.Error);
    }

    private Result<RunState> ReplayStartEncounter(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<RunEncounterStartCommand>(entry.Command);
        var result = runtime.Combats.StartEncounter(
            entry.RunId,
            payload.InitialHero,
            payload.InitialEnemies,
            entry.CommandId.HasValue ? CreateIdentity(entry) : null,
            payload.InitialStatusEffects);
        return result.IsSuccess
            ? Result<RunState>.Success(result.Value.RunState)
            : Result<RunState>.Failure(result.Error);
    }

    private Result<RunState> ReplayCombatAction(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<CombatActionJournalPayload>(entry.Command);
        var result = runtime.Combats.ExecuteAction(
            payload.CombatId,
            payload.Command,
            entry.CommandId.HasValue ? CreateIdentity(entry) : null);
        return result.IsSuccess
            ? Result<RunState>.Success(result.Value.RunState)
            : Result<RunState>.Failure(result.Error);
    }

    private Result<RunState> ReplayResolveCombat(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<CombatJournalPayload>(entry.Command);
        var result = runtime.Combats.ResolveEncounter(
            entry.RunId,
            payload.CombatId,
            entry.CommandId.HasValue ? CreateIdentity(entry) : null);
        return result.IsSuccess
            ? Result<RunState>.Success(result.Value.RunState)
            : Result<RunState>.Failure(result.Error);
    }

    private Result<RunState> ReplayRunResource(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<RunResourcePayload>(entry.Command);
        return runtime.Runs.ApplyRunResource(
            entry.RunId,
            payload.ResourceId,
            payload.Value,
            payload.Operation,
            payload.Field);
    }

    private Result<RunState> ReplayResolveNode(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<ResolveNodePayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.ResolveCurrentNode(entry.RunId, payload.CurrentNodeId));
    }

    private Result<RunState> ReplayAdvanceNode(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<AdvanceNodePayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.AdvanceNode(entry.RunId, payload.TargetNodeId));
    }

    private Result<RunState> ReplayDraw(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<CountPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.DrawCards(entry.RunId, payload.Count));
    }

    private Result<RunState> ReplayAddToHand(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<CardIdsPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.AddCardsToHand(entry.RunId, payload.CardIds));
    }

    private Result<RunState> ReplayMoveCards(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<MoveCardsPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.ConsumeCardsFromHand(
            entry.RunId,
            payload.CardIds,
            payload.Destination));
    }

    private Result<RunState> ReplayCreateSelection(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<SelectionDefinitionPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.CreateCardSelection(entry.RunId, payload.SelectionId));
    }

    private Result<RunState> ReplayPickSelection(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<SelectionCardsPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.PickCards(entry.RunId, payload.SelectionInstanceId, payload.CardIds));
    }

    private Result<RunState> ReplayRerollSelection(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<RerollSelectionPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.RerollCardSelection(
            entry.RunId,
            payload.SelectionInstanceId,
            payload.LockedCardIds));
    }

    private Result<RunState> ReplayDecomposeSelection(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<SelectionItemPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.DecomposeCardSelectionOption(
            entry.RunId,
            payload.SelectionInstanceId,
            payload.CardId));
    }

    private Result<RunState> ReplayCreateShop(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<ShopDefinitionPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.CreateShop(entry.RunId, payload.ShopId));
    }

    private Result<RunState> ReplayBuyShop(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<ShopItemPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.BuyShopItem(entry.RunId, payload.ShopInstanceId, payload.ItemId));
    }

    private Result<RunState> ReplayRerollShop(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<ShopPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.RerollShop(entry.RunId, payload.ShopInstanceId));
    }

    private Result<RunState> ReplayCreatePreparation(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<PreparationDefinitionPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.CreatePreparation(entry.RunId, payload.PreparationId));
    }

    private Result<RunState> ReplayApplyPreparation(ReplayRuntime runtime, RunJournalEntry entry)
    {
        var payload = Deserialize<PreparationPayload>(entry.Command);
        return ToRun(runtime, runtime.Runs.ApplyPreparationOption(
            entry.RunId,
            payload.PreparationInstanceId,
            payload.OptionId));
    }

    private Result<RunState> ReplayRestore(
        ReplayRuntime runtime,
        RunJournalEntry entry,
        IReadOnlyDictionary<int, RunState> statesBySequence)
    {
        var payload = Deserialize<CheckpointPayload>(entry.Command);
        var sequence = payload.Sequence ?? payload.TargetSequence;
        return sequence.HasValue && statesBySequence.TryGetValue(sequence.Value, out var target)
            ? runtime.Runs.RestoreState(target)
            : Result<RunState>.Failure($"Replay checkpoint not found: {sequence}");
    }

    private Result<RunState> ToRun<T>(ReplayRuntime runtime, Result<T> operation)
    {
        if (operation.IsFailure)
            return Result<RunState>.Failure(operation.Error);
        return runtime.Runs.GetRun(runtime.RunId);
    }

    private Result<RunState> ToRun(ReplayRuntime runtime, Result operation)
    {
        if (operation.IsFailure)
            return Result<RunState>.Failure(operation.Error);
        return runtime.Runs.GetRun(runtime.RunId);
    }

    private T Deserialize<T>(JsonElement element)
    {
        return element.Deserialize<T>(_jsonOptions)
            ?? throw new JsonException($"Could not deserialize {typeof(T).Name}");
    }

    private static RunCommandIdentity CreateIdentity(RunJournalEntry entry) => new(
        entry.CommandId!.Value,
        entry.CommandType,
        entry.ExpectedSequence ?? checked(entry.Sequence - 1),
        entry.ExpectedStep ?? 0,
        entry.CommandPayloadHash);

    private static RunSemanticReplayVerification Failure(Guid runId, string error) => new()
    {
        RunId = runId,
        IsValid = false,
        Reexecuted = false,
        Errors = [error]
    };

    private sealed class ReplayRuntime
    {
        public ReplayRuntime(
            RunManager runs,
            CombatRunCoordinator combats,
            IGameplayCommandGateway gateway)
        {
            Runs = runs;
            Combats = combats;
            Gateway = gateway;
        }

        public RunManager Runs { get; }
        public CombatRunCoordinator Combats { get; }
        public IGameplayCommandGateway Gateway { get; }
        public Guid RunId { get; private set; }
        public void SetRunId(Guid runId) => RunId = runId;
    }

    private sealed record CombatActionJournalPayload(Guid CombatId, CombatActionCommand Command);
    private sealed record CombatJournalPayload(Guid CombatId);
    private sealed record RunResourcePayload(
        string ResourceId,
        float Value,
        ResourceEffectOperation Operation,
        ResourceValueField Field);
    private sealed record ResolveNodePayload(string CurrentNodeId);
    private sealed record AdvanceNodePayload(string TargetNodeId);
    private sealed record CountPayload(int Count);
    private sealed record CardIdsPayload(IReadOnlyList<string> CardIds);
    private sealed record MoveCardsPayload(
        IReadOnlyList<string> CardIds,
        CardConsumeDestination Destination);
    private sealed record SelectionDefinitionPayload(string SelectionId);
    private sealed record SelectionCardsPayload(Guid SelectionInstanceId, IReadOnlyList<string> CardIds);
    private sealed record RerollSelectionPayload(
        Guid SelectionInstanceId,
        IReadOnlyList<string>? LockedCardIds);
    private sealed record SelectionItemPayload(Guid SelectionInstanceId, string CardId);
    private sealed record ShopDefinitionPayload(string ShopId);
    private sealed record ShopItemPayload(Guid ShopInstanceId, string ItemId);
    private sealed record ShopPayload(Guid ShopInstanceId);
    private sealed record PreparationDefinitionPayload(string PreparationId);
    private sealed record PreparationPayload(Guid PreparationInstanceId, string OptionId);
    private sealed record CheckpointPayload
    {
        public int? Sequence { get; init; }
        public int? TargetSequence { get; init; }
    }
}
