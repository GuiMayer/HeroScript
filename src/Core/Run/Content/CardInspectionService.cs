using System.Collections.Immutable;
using Core.Calculations;
using Core.CardZones;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.StatusEffects;

namespace Core.Run.Content;

public sealed record CardInspectionRequest
{
    private ImmutableArray<string> _selectedTargetIds = [];

    public Guid CombatId { get; init; }
    public Guid CardInstanceId { get; init; }
    public string? ActorId { get; init; }
    public IReadOnlyList<string> SelectedTargetIds
    {
        get => _selectedTargetIds;
        init => _selectedTargetIds = value?.ToImmutableArray() ?? [];
    }
    public string? CostOptionId { get; init; }
    public InspectionDetailLevel MaximumDetail { get; init; } = InspectionDetailLevel.Full;
}

public sealed record CardInspectionVersion
{
    public Guid RunId { get; init; }
    public Guid CombatId { get; init; }
    public int RunSequence { get; init; }
    public ulong RunStep { get; init; }
    public ulong CombatStep { get; init; }
    public string ConfigName { get; init; } = string.Empty;
    public string? ModeId { get; init; }
    public string ContentRevision { get; init; } = string.Empty;
    public string EngineVersion { get; init; } = string.Empty;
}

public sealed record CardInspectionContext
{
    private ImmutableArray<CombatActorState> _candidateTargets = [];
    private ImmutableArray<RunRelicState> _relics = [];
    private ImmutableArray<ScriptModifierInstance> _modifiers = [];
    private ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>> _statuses =
        ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableArray<string> _calculationPipelineIds = [];

    public CombatActorState Actor { get; init; } = null!;
    public EntityState? PersistentActor { get; init; }
    public IReadOnlyList<CombatActorState> CandidateTargets
    {
        get => _candidateTargets;
        init => _candidateTargets = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<RunRelicState> Relics
    {
        get => _relics;
        init => _relics = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ScriptModifierInstance> Modifiers
    {
        get => _modifiers;
        init => _modifiers = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyDictionary<string, ImmutableArray<StatusEffectInstance>> Statuses
    {
        get => _statuses;
        init => _statuses = value?.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal)
            ?? ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>>.Empty.WithComparers(StringComparer.Ordinal);
    }
    public IReadOnlyList<string> CalculationPipelineIds
    {
        get => _calculationPipelineIds;
        init => _calculationPipelineIds = value?.ToImmutableArray() ?? [];
    }
    public ResolvedGameMode? GameMode { get; init; }
}

public sealed record CardInspectionResult
{
    private ImmutableArray<CardUpgradeState> _appliedUpgrades = [];
    private ImmutableArray<CalculationResult> _calculations = [];
    private ImmutableArray<EffectApplicationRecord> _previewApplications = [];
    private ImmutableArray<EffectExecutionStep> _previewSteps = [];

    public CardInspectionVersion Version { get; init; } = new();
    public CardPreviewScope PreviewScope { get; init; } = new();
    public ImmutableArray<CardProcPreview> Procs { get; init; } = [];
    public RandomOutcomePreview RandomOutcomes { get; init; } = new();
    public InspectionDetailLevel Detail { get; init; }
    public string Zone { get; init; } = string.Empty;
    public bool IsInPlayableZone { get; init; }
    public bool IsPlayable { get; init; }
    public CardContentDefinition BaseContainer { get; init; } = null!;
    public CompiledCardDefinition CompiledContainer { get; init; } = null!;
    public IReadOnlyList<CardUpgradeState> AppliedUpgrades
    {
        get => _appliedUpgrades;
        init => _appliedUpgrades = value?.ToImmutableArray() ?? [];
    }
    public EffectiveCardDefinition EffectiveBase { get; init; } = null!;
    public CardInspectionContext? ContextSources { get; init; }
    public CardPlayEvaluation Evaluation { get; init; } = null!;
    public IReadOnlyList<CalculationResult> Calculations
    {
        get => _calculations;
        init => _calculations = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectApplicationRecord> PreviewApplications
    {
        get => _previewApplications;
        init => _previewApplications = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectExecutionStep> PreviewSteps
    {
        get => _previewSteps;
        init => _previewSteps = value?.ToImmutableArray() ?? [];
    }
    public string ResolutionFingerprint { get; init; } = string.Empty;
}

public interface ICardInspectionService
{
    Result<CardInspectionResult> Inspect(CardInspectionRequest request);
    Result<IReadOnlyList<CardInspectionResult>> InspectPlayableCards(
        Guid combatId,
        string? actorId = null,
        IReadOnlyList<string>? selectedTargetIds = null,
        string? costOptionId = null,
        InspectionDetailLevel maximumDetail = InspectionDetailLevel.Full);
}

/// <summary>
/// Read-only card projection. Compilation, upgrade resolution, legality and
/// preview execution are delegated to the same services used by PLAY_CARD.
/// </summary>
public sealed class CardInspectionService : ICardInspectionService
{
    private readonly IRunQueryService _runs;
    private readonly IContentRuntimeResolver _runtimes;
    private readonly ICardContentCompiler _compiler;
    private readonly IEffectiveCardResolver _effectiveCards;
    private readonly ILegalActionResolver _legalActions;
    private readonly ICalculationEngine? _calculations;

    public CardInspectionService(
        IRunQueryService runs,
        IContentRuntimeResolver runtimes,
        ICardContentCompiler compiler,
        IEffectiveCardResolver effectiveCards,
        ILegalActionResolver legalActions,
        ICalculationEngine? calculations = null)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _effectiveCards = effectiveCards ?? throw new ArgumentNullException(nameof(effectiveCards));
        _legalActions = legalActions ?? throw new ArgumentNullException(nameof(legalActions));
        _calculations = calculations;
    }

    public Result<CardInspectionResult> Inspect(CardInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resolved = ResolveCombat(request.CombatId);
        return resolved.IsFailure
            ? Result<CardInspectionResult>.Failure(resolved.Error)
            : Inspect(resolved.Value.Run, resolved.Value.Combat, request);
    }

    public Result<IReadOnlyList<CardInspectionResult>> InspectPlayableCards(
        Guid combatId,
        string? actorId = null,
        IReadOnlyList<string>? selectedTargetIds = null,
        string? costOptionId = null,
        InspectionDetailLevel maximumDetail = InspectionDetailLevel.Full)
    {
        var resolved = ResolveCombat(combatId);
        if (resolved.IsFailure)
            return Result<IReadOnlyList<CardInspectionResult>>.Failure(resolved.Error);
        var results = ImmutableArray.CreateBuilder<CardInspectionResult>();
        foreach (var cardInstanceId in CardZonePlaySource.CardsForActor(resolved.Value.Run,
            actorId ?? resolved.Value.Run.PlayerEntityId))
        {
            var inspected = Inspect(resolved.Value.Run, resolved.Value.Combat, new CardInspectionRequest
            {
                CombatId = combatId,
                CardInstanceId = cardInstanceId,
                ActorId = actorId,
                SelectedTargetIds = selectedTargetIds ?? [],
                CostOptionId = costOptionId,
                MaximumDetail = maximumDetail
            });
            if (inspected.IsFailure)
                return Result<IReadOnlyList<CardInspectionResult>>.Failure(inspected.Error);
            results.Add(inspected.Value);
        }
        return Result<IReadOnlyList<CardInspectionResult>>.Success(results.ToImmutable());
    }

    private Result<CardInspectionResult> Inspect(
        RunState run,
        CombatState combat,
        CardInspectionRequest request)
    {
        var detail = run.ResolvedMode?.CapabilityPolicy.CardInspectionDetail
            ?? InspectionDetailLevel.Full;
        detail = (InspectionDetailLevel)System.Math.Min((int)detail, (int)request.MaximumDetail);
        if (detail == InspectionDetailLevel.Disabled)
            return Result<CardInspectionResult>.Failure("Card inspection is disabled by the game mode");
        if (!CardZoneReadModel.Project(run).Zones.SelectMany(zone => zone.Cards)
                .Any(card => card.CardInstanceId == request.CardInstanceId))
            return Result<CardInspectionResult>.Failure("Card is not visible under the current zone policy");
        var instance = run.Deck.GetCard(request.CardInstanceId);
        if (instance == null)
            return Result<CardInspectionResult>.Failure($"Card instance not found: {request.CardInstanceId}");
        var actorId = string.IsNullOrWhiteSpace(request.ActorId)
            ? combat.ActivationState?.ActiveActorId
                ?? combat.GetAllActors().FirstOrDefault(actor => actor.ControllerBinding.Kind == ControllerKind.Player)?.InstanceId
                ?? combat.GetAllActors().First().InstanceId
            : request.ActorId;
        var actor = combat.GetActor(actorId);
        if (actor == null)
            return Result<CardInspectionResult>.Failure($"Actor not found: {actorId}");

        var runtime = _runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure)
            return Result<CardInspectionResult>.Failure(runtime.Error);
        var authored = runtime.Value.GetDefinition<CardContentDefinition>("cards", instance.DefinitionId);
        if (authored.IsFailure)
            return Result<CardInspectionResult>.Failure(authored.Error);
        var compiled = _compiler.Compile(instance.DefinitionId, runtime.Value);
        if (compiled.IsFailure)
            return Result<CardInspectionResult>.Failure(compiled.Error);
        var effective = _effectiveCards.Resolve(compiled.Value, instance);
        if (effective.IsFailure)
            return Result<CardInspectionResult>.Failure(effective.Error);
        var command = new CombatActionCommand
        {
            RunId = run.RunId,
            ActorId = actorId,
            ActionType = ActionType.PLAY_CARD,
            CardInstanceId = request.CardInstanceId,
            TargetIds = request.SelectedTargetIds,
            CostOptionId = request.CostOptionId
        };
        var legal = _legalActions.Evaluate(run, combat, command, CombatCommandOrigin.PlayerInput);
        if (legal.IsFailure)
            return Result<CardInspectionResult>.Failure(legal.Error);
        var resolvedEvaluation = legal.Value.CardEvaluation ?? new CardPlayEvaluation
        {
            CardInstanceId = request.CardInstanceId,
            ActorId = actorId,
            IsLegal = false,
            FailureReasons = legal.Value.FailureReasons
        };
        if (!legal.Value.IsLegal && resolvedEvaluation.IsLegal)
            resolvedEvaluation = resolvedEvaluation with
            {
                IsLegal = false,
                FailureReasons = legal.Value.FailureReasons
            };

        var isInPlayableZone = CardZonePlaySource.Contains(run, actorId, request.CardInstanceId);
        var preview = legal.Value.Candidate?.CardPlay;
        var stochastic = RandomOutcomePreviewProjector.HasStochasticInput(preview?.Steps ?? []);

        var context = detail == InspectionDetailLevel.Full
            ? BuildContext(run, combat, actor, resolvedEvaluation)
            : null;
        var fingerprint = preview?.ResolutionFingerprint ?? CanonicalJson.ComputeHash(new
        {
            version = new
            {
                run.RunId,
                request.CombatId,
                run.Sequence,
                runStep = run.Determinism.Step,
                combatStep = combat.Determinism.Step,
                run.Determinism.ContentRevision,
                run.Determinism.EngineVersion
            },
            effective = effective.Value.Fingerprint,
            evaluation = resolvedEvaluation,
            isInPlayableZone,
            context
        });
        return Result<CardInspectionResult>.Success(new CardInspectionResult
        {
            Version = new CardInspectionVersion
            {
                RunId = run.RunId,
                CombatId = combat.CombatId,
                RunSequence = run.Sequence,
                RunStep = run.Determinism.Step,
                CombatStep = combat.Determinism.Step,
                ConfigName = run.ConfigName,
                ModeId = run.ModeId,
                ContentRevision = run.Determinism.ContentRevision,
                EngineVersion = run.Determinism.EngineVersion
            },
            Detail = detail,
            PreviewScope = new() { HasExecutablePreview = preview != null,
                DependsOnRandomInputs = stochastic,
                Validity = stochastic ? "SampledPathNotGuaranteedOutcome" : "ExactForCapturedSnapshotAndInput",
                SelectedTargetIds = request.SelectedTargetIds.ToImmutableArray(), CostOptionId = request.CostOptionId,
                SnapshotHash = CanonicalJson.ComputeHash(run), CombatSnapshotHash = CanonicalJson.ComputeHash(combat) },
            Procs = CardProcPreviewProjector.Project(preview?.Steps ?? []),
            RandomOutcomes = RandomOutcomePreviewProjector.Project(preview?.Steps ?? [], runtime.Value, _calculations),
            Zone = ResolveZone(run.Deck, request.CardInstanceId),
            IsInPlayableZone = isInPlayableZone,
            IsPlayable = isInPlayableZone && resolvedEvaluation.IsLegal,
            BaseContainer = authored.Value,
            CompiledContainer = compiled.Value,
            AppliedUpgrades = effective.Value.AppliedUpgrades,
            EffectiveBase = effective.Value,
            ContextSources = context,
            Evaluation = resolvedEvaluation,
            Calculations = preview?.Calculations ?? [],
            PreviewApplications = preview?.Applications ?? [],
            PreviewSteps = preview?.Steps ?? [],
            ResolutionFingerprint = fingerprint
        });
    }

    private Result<(RunState Run, CombatState Combat)> ResolveCombat(Guid combatId)
    {
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return Result<(RunState, CombatState)>.Failure(run.Error);
        var encounter = run.Value.GetEncounter(combatId);
        return encounter == null
            ? Result<(RunState, CombatState)>.Failure($"Combat not found in run: {combatId}")
            : Result<(RunState, CombatState)>.Success((run.Value, encounter.Combat));
    }

    private static CardInspectionContext BuildContext(
        RunState run,
        CombatState combat,
        CombatActorState actor,
        CardPlayEvaluation evaluation)
    {
        var targetIds = evaluation.ResolvedTargetIds.Count > 0
            ? evaluation.ResolvedTargetIds
            : evaluation.LegalTargetIds;
        return new CardInspectionContext
        {
            Actor = actor,
            PersistentActor = actor.InstanceId == run.PlayerEntityId ? run.PlayerEntity : null,
            CandidateTargets = targetIds
                .Distinct(StringComparer.Ordinal)
                .Select(combat.GetActor)
                .Where(entity => entity != null)
                .Cast<CombatActorState>()
                .ToArray(),
            Relics = run.Relics.OrderBy(relic => relic.RelicInstanceId).ToArray(),
            Modifiers = run.Modifiers.OrderBy(modifier => modifier.InstanceId).ToArray(),
            Statuses = combat.StatusEffects
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToImmutableDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Where(status => status.IsActive)
                        .OrderBy(status => status.InstanceId)
                        .ToImmutableArray(),
                    StringComparer.Ordinal),
            CalculationPipelineIds = run.ResolvedMode?.Definition.CalculationPipelineIds ?? [],
            GameMode = run.ResolvedMode
        };
    }

    private static string ResolveZone(DeckState deck, Guid cardInstanceId)
    {
        return deck.Topology.FindZone(cardInstanceId)?.Address.ZoneId ?? "collection";
    }
}
