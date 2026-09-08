using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Run;

namespace Core.Combat.TurnPhase;

public sealed record PhaseTransitionRecord
{
    public string EdgeId { get; init; } = string.Empty;
    public string FromPhaseId { get; init; } = string.Empty;
    public string ToPhaseId { get; init; } = string.Empty;
    public PhaseEdgeTrigger Trigger { get; init; }
    public int Priority { get; init; }
    public string StateBeforeHash { get; init; } = string.Empty;
    public string StateAfterHash { get; init; } = string.Empty;
}

public sealed record PhaseGraphResult
{
    public CombatState Combat { get; init; } = null!;
    public RunState Run { get; init; } = null!;
    public ImmutableArray<PhaseTransitionRecord> Transitions { get; init; } = [];
    public ImmutableArray<EffectExecutionStep> Steps { get; init; } = [];
    public ImmutableArray<CalculationResult> Calculations { get; init; } = [];
    public ImmutableArray<EffectApplicationRecord> Applications { get; init; } = [];
    public string Fingerprint { get; init; } = string.Empty;
}

public interface IPhaseGraphReducer
{
    Result<PhaseGraphResult> Enter(RunState run, CombatState combat, PhaseSequenceDefinition sequence);
    Result<PhaseGraphResult> HandleCommand(RunState run, CombatState combat,
        PhaseSequenceDefinition sequence, CombatActionCommand command, IReadOnlySet<string> commandTags);
    Result<PhaseGraphResult> Exit(RunState run, CombatState combat, PhaseSequenceDefinition sequence);
    Result<PhaseGraphResult> AdvanceAutomatic(RunState run, CombatState combat, PhaseSequenceDefinition sequence);
    Result ValidateCommand(CombatState combat, PhaseSequenceDefinition sequence,
        CombatActionCommand command, IReadOnlySet<string> commandTags);
}

/// <summary>
/// Deterministic immutable interpreter for phase graphs. Content definitions
/// are supplied from the run's pinned revision and never embedded in state.
/// </summary>
public sealed class PhaseGraphReducer : IPhaseGraphReducer
{
    private readonly IRuntimeFormulaEvaluator _formulas;
    private readonly IEffectTriggerExecutor _effects;

    public PhaseGraphReducer(IRuntimeFormulaEvaluator formulas, IEffectTriggerExecutor effects)
    {
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    }

    public Result<PhaseGraphResult> Enter(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence)
    {
        var valid = ValidateInputs(run, combat, sequence, requireState: false);
        if (valid.IsFailure) return Result<PhaseGraphResult>.Failure(valid.Error);
        var entry = sequence.Find(sequence.EntryPhaseId)!;
        combat = combat with
        {
            PhaseState = new PhaseState
            {
                SequenceId = sequence.SequenceId,
                ContentRevision = run.Determinism.ContentRevision,
                Cursor = entry.PhaseId
            }
        };
        var accumulator = Accumulator.Create(run, combat);
        var entered = ApplyBoundary(accumulator, sequence, entry, entering: true);
        if (entered.IsFailure) return Result<PhaseGraphResult>.Failure(entered.Error);
        return AdvanceAutomaticCore(entered.Value, sequence);
    }

    public Result<PhaseGraphResult> HandleCommand(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        CombatActionCommand command,
        IReadOnlySet<string> commandTags)
    {
        ArgumentNullException.ThrowIfNull(command);
        commandTags ??= ImmutableHashSet<string>.Empty;
        var valid = ValidateCommand(combat, sequence, command, commandTags);
        if (valid.IsFailure) return Result<PhaseGraphResult>.Failure(valid.Error);
        var accumulator = Accumulator.Create(run, combat);
        var selected = SelectEdge(accumulator, sequence, PhaseEdgeTrigger.Command, command, commandTags);
        if (selected.IsFailure) return Result<PhaseGraphResult>.Failure(selected.Error);
        if (selected.Value == null) return Success(accumulator);
        var moved = Transition(accumulator, sequence, selected.Value);
        return moved.IsFailure ? Result<PhaseGraphResult>.Failure(moved.Error)
            : AdvanceAutomaticCore(moved.Value, sequence);
    }

    public Result<PhaseGraphResult> Exit(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence)
    {
        var valid = ValidateInputs(run, combat, sequence, requireState: true);
        if (valid.IsFailure) return Result<PhaseGraphResult>.Failure(valid.Error);
        var accumulator = Accumulator.Create(run, combat);
        var count = 0;
        while (true)
        {
            var phase = sequence.Find(accumulator.Combat.PhaseState!.Cursor)!;
            if (phase.Role == PhaseRole.End) return Success(accumulator);
            var trigger = phase.Edges.Any(edge => edge.Trigger == PhaseEdgeTrigger.Automatic)
                ? PhaseEdgeTrigger.Automatic
                : PhaseEdgeTrigger.ActivationExit;
            var selected = SelectEdge(accumulator, sequence, trigger, null, ImmutableHashSet<string>.Empty);
            if (selected.IsFailure) return Result<PhaseGraphResult>.Failure(selected.Error);
            if (selected.Value == null)
                return Result<PhaseGraphResult>.Failure(
                    $"Phase '{phase.PhaseId}' cannot reach an END phase during activation exit");
            if (count >= sequence.MaxAutomaticTransitions)
                return Result<PhaseGraphResult>.Failure(
                    $"Phase exit exceeded {sequence.MaxAutomaticTransitions} deterministic transitions");
            var moved = Transition(accumulator, sequence, selected.Value);
            if (moved.IsFailure) return Result<PhaseGraphResult>.Failure(moved.Error);
            accumulator = moved.Value;
            count++;
        }
    }

    public Result<PhaseGraphResult> AdvanceAutomatic(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence)
    {
        var valid = ValidateInputs(run, combat, sequence, requireState: true);
        return valid.IsFailure ? Result<PhaseGraphResult>.Failure(valid.Error)
            : AdvanceAutomaticCore(Accumulator.Create(run, combat), sequence);
    }

    public Result ValidateCommand(
        CombatState combat,
        PhaseSequenceDefinition sequence,
        CombatActionCommand command,
        IReadOnlySet<string> commandTags)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(command);
        commandTags ??= ImmutableHashSet<string>.Empty;
        var state = ValidateState(combat, sequence);
        if (state.IsFailure) return state;
        var phase = sequence.Find(combat.PhaseState!.Cursor)!;
        var typeAllowed = phase.AllowedActions.Contains(command.ActionType);
        var tagAllowed = phase.AllowedCommandTags.Any(allowed =>
            commandTags.Contains(allowed, StringComparer.OrdinalIgnoreCase));
        return typeAllowed || tagAllowed
            ? Result.Success()
            : Result.Failure(
                $"Action '{command.ActionType}' with tags [{string.Join(", ", commandTags.OrderBy(tag => tag, StringComparer.Ordinal))}] " +
                $"is not allowed in phase '{phase.PhaseId}'");
    }

    private Result<PhaseGraphResult> AdvanceAutomaticCore(
        Accumulator accumulator,
        PhaseSequenceDefinition sequence)
    {
        var count = 0;
        while (true)
        {
            var selected = SelectEdge(accumulator, sequence, PhaseEdgeTrigger.Automatic,
                null, ImmutableHashSet<string>.Empty);
            if (selected.IsFailure) return Result<PhaseGraphResult>.Failure(selected.Error);
            if (selected.Value == null) return Success(accumulator);
            if (count >= sequence.MaxAutomaticTransitions)
                return Result<PhaseGraphResult>.Failure(
                    $"Automatic phase resolution exceeded {sequence.MaxAutomaticTransitions} transitions");
            var moved = Transition(accumulator, sequence, selected.Value);
            if (moved.IsFailure) return Result<PhaseGraphResult>.Failure(moved.Error);
            accumulator = moved.Value;
            count++;
        }
    }

    private Result<PhaseEdgeDefinition?> SelectEdge(
        Accumulator accumulator,
        PhaseSequenceDefinition sequence,
        PhaseEdgeTrigger trigger,
        CombatActionCommand? command,
        IReadOnlySet<string> commandTags)
    {
        var phase = sequence.Find(accumulator.Combat.PhaseState!.Cursor)!;
        var matches = new List<PhaseEdgeDefinition>();
        foreach (var edge in phase.Edges.Where(edge => edge.Trigger == trigger))
        {
            if (trigger == PhaseEdgeTrigger.Command && command != null)
            {
                if (edge.ActionTypes.Count > 0 && !edge.ActionTypes.Contains(command.ActionType)) continue;
                if (edge.CommandTags.Count > 0 && !edge.CommandTags.Any(tag =>
                        commandTags.Contains(tag, StringComparer.OrdinalIgnoreCase))) continue;
            }
            var condition = EvaluateCondition(accumulator, phase, edge, command, commandTags);
            if (condition.IsFailure) return Result<PhaseEdgeDefinition?>.Failure(condition.Error);
            if (condition.Value) matches.Add(edge);
        }
        if (matches.Count == 0) return Result<PhaseEdgeDefinition?>.Success(null);
        var highest = matches.Max(edge => edge.Priority);
        var selected = matches.Where(edge => edge.Priority == highest)
            .OrderBy(edge => edge.EdgeId, StringComparer.Ordinal).ToArray();
        return selected.Length == 1
            ? Result<PhaseEdgeDefinition?>.Success(selected[0])
            : Result<PhaseEdgeDefinition?>.Failure(
                $"Phase '{phase.PhaseId}' resolved ambiguous {trigger} edges at priority {highest}: " +
                string.Join(", ", selected.Select(edge => edge.EdgeId)));
    }

    private Result<bool> EvaluateCondition(
        Accumulator accumulator,
        PhaseDefinition phase,
        PhaseEdgeDefinition edge,
        CombatActionCommand? command,
        IReadOnlySet<string> tags)
    {
        if (string.IsNullOrWhiteSpace(edge.Condition)) return Result<bool>.Success(true);
        var actorId = command?.ActorId ?? accumulator.Combat.ActivationState?.ActiveActorId;
        var actor = accumulator.Combat.GetActor(actorId ?? string.Empty);
        if (actor == null) return Result<bool>.Failure("Phase condition requires an active actor");
        var variables = GameplayFormulaContext.Build(actor, actor, actor, accumulator.Run);
        variables["turn"] = accumulator.Combat.CurrentTurn;
        variables["round"] = accumulator.Combat.ActivationState?.Round ?? 0;
        variables["activation"] = accumulator.Combat.ActivationState?.ActivationNumber ?? 0;
        variables["actions_taken"] = accumulator.Combat.ActivationState?.ActionsTaken ?? 0;
        variables["phase_order"] = phase.Order;
        variables["command_type"] = command == null ? -1 : (int)command.ActionType;
        foreach (var tag in tags)
            variables[$"tag_{Normalize(tag)}"] = 1;
        var value = GameplayFormulaContext.Evaluate(
            _formulas,
            edge.Condition,
            accumulator.Run.Determinism.ContentRevision,
            variables);
        return value.IsFailure ? Result<bool>.Failure(value.Error) : Result<bool>.Success(value.Value > 0);
    }

    private Result<Accumulator> Transition(
        Accumulator accumulator,
        PhaseSequenceDefinition sequence,
        PhaseEdgeDefinition edge)
    {
        var source = sequence.Find(accumulator.Combat.PhaseState!.Cursor)!;
        var before = CanonicalJson.ComputeHash(accumulator.Combat);
        var exited = ApplyBoundary(accumulator, sequence, source, entering: false);
        if (exited.IsFailure) return Result<Accumulator>.Failure(exited.Error);
        var target = sequence.Find(edge.TargetPhaseId)!;
        var movedCombat = exited.Value.Combat with
        {
            PhaseState = exited.Value.Combat.PhaseState! with { Cursor = target.PhaseId }
        };
        var moved = exited.Value with { Combat = movedCombat };
        var entered = ApplyBoundary(moved, sequence, target, entering: true);
        if (entered.IsFailure) return Result<Accumulator>.Failure(entered.Error);
        return Result<Accumulator>.Success(entered.Value with
        {
            Transitions = entered.Value.Transitions.Add(new PhaseTransitionRecord
            {
                EdgeId = edge.EdgeId,
                FromPhaseId = source.PhaseId,
                ToPhaseId = target.PhaseId,
                Trigger = edge.Trigger,
                Priority = edge.Priority,
                StateBeforeHash = before,
                StateAfterHash = CanonicalJson.ComputeHash(entered.Value.Combat)
            })
        });
    }

    private Result<Accumulator> ApplyBoundary(
        Accumulator accumulator,
        PhaseSequenceDefinition sequence,
        PhaseDefinition phase,
        bool entering)
    {
        var definitions = entering ? phase.EntryEffects : phase.ExitEffects;
        if (definitions.Count == 0) return Result<Accumulator>.Success(accumulator);
        var actorId = accumulator.Combat.ActivationState?.ActiveActorId;
        if (string.IsNullOrWhiteSpace(actorId) || accumulator.Combat.GetActor(actorId) == null)
            return Result<Accumulator>.Failure($"Phase '{phase.PhaseId}' effects require an active actor");
        var boundary = entering ? "entry" : "exit";
        var executed = _effects.Execute(new EffectTriggerExecutionRequest
        {
            Run = accumulator.Run,
            Combat = accumulator.Combat,
            Trigger = new EffectTriggerDefinition
            {
                TriggerId = $"phase:{sequence.SequenceId}:{phase.PhaseId}:{boundary}",
                Boundary = $"Phase{(entering ? "Entry" : "Exit")}",
                Effects = definitions
            },
            OwnerEntityId = actorId,
            SourceEntityId = actorId,
            SelectedTargetEntityIds = [actorId],
            ContentRevision = accumulator.Run.Determinism.ContentRevision,
            Provenance = new EffectProvenance
            {
                Kind = EffectProvenanceKind.Rule,
                SourceId = sequence.SequenceId,
                ComponentId = $"{phase.PhaseId}:{boundary}"
            }
        });
        if (executed.IsFailure) return Result<Accumulator>.Failure(executed.Error);
        var offset = accumulator.Steps.Length;
        return Result<Accumulator>.Success(accumulator with
        {
            Combat = executed.Value.State,
            Run = executed.Value.Run ?? accumulator.Run,
            Steps = accumulator.Steps.AddRange(executed.Value.Steps.Select((step, index) =>
                step with { Index = offset + index })),
            Calculations = accumulator.Calculations.AddRange(executed.Value.Calculations),
            Applications = accumulator.Applications.AddRange(executed.Value.Records)
        });
    }

    private static Result ValidateInputs(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        bool requireState)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(sequence);
        var validation = PhaseSequenceValidator.Validate(sequence);
        if (validation.IsFailure) return validation;
        return requireState ? ValidateState(combat, sequence) : Result.Success();
    }

    private static Result ValidateState(CombatState combat, PhaseSequenceDefinition sequence)
    {
        var state = combat.PhaseState;
        if (state == null) return Result.Failure("Combat phase has not been initialized");
        if (!string.Equals(state.SequenceId, sequence.SequenceId, StringComparison.Ordinal))
            return Result.Failure(
                $"Phase state sequence '{state.SequenceId}' does not match pinned sequence '{sequence.SequenceId}'");
        if (!string.Equals(state.ContentRevision, combat.Determinism.ContentRevision, StringComparison.Ordinal))
            return Result.Failure(
                $"Phase state revision '{state.ContentRevision}' does not match combat revision '{combat.Determinism.ContentRevision}'");
        return sequence.Find(state.Cursor) == null
            ? Result.Failure($"Phase cursor does not exist in sequence '{sequence.SequenceId}': {state.Cursor}")
            : Result.Success();
    }

    private static Result<PhaseGraphResult> Success(Accumulator value)
    {
        var result = new PhaseGraphResult
        {
            Combat = value.Combat,
            Run = value.Run,
            Transitions = value.Transitions,
            Steps = value.Steps,
            Calculations = value.Calculations,
            Applications = value.Applications
        };
        return Result<PhaseGraphResult>.Success(result with
        {
            Fingerprint = CanonicalJson.ComputeHash(new
            {
                result.Combat,
                result.Run,
                result.Transitions,
                result.Steps,
                result.Applications
            })
        });
    }

    private static string Normalize(string value) => new(value.Trim().ToLowerInvariant()
        .Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    private sealed record Accumulator(
        RunState Run,
        CombatState Combat,
        ImmutableArray<PhaseTransitionRecord> Transitions,
        ImmutableArray<EffectExecutionStep> Steps,
        ImmutableArray<CalculationResult> Calculations,
        ImmutableArray<EffectApplicationRecord> Applications)
    {
        public static Accumulator Create(RunState run, CombatState combat) =>
            new(run, combat, [], [], [], []);
    }
}
