using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using System.Text.Json.Serialization;

namespace Core.Run;

public enum RunActivityBoundary
{
    Entry,
    Exit
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunActivityEffectOwner { RunWallet, PlayerEntity }

public sealed record RunActivityEffectResult
{
    public required RunState State { get; init; }
    public ImmutableArray<EffectExecutionStep> Steps { get; init; } = [];
    public ImmutableArray<EffectApplicationRecord> Applications { get; init; } = [];
    public string Fingerprint { get; init; } = string.Empty;
    public ImmutableArray<Core.Calculations.CalculationResult> Calculations => Steps.SelectMany(step =>
        step.Parameters.Select(parameter => parameter.Calculation).OfType<Core.Calculations.CalculationResult>()
            .Concat(step.PayloadCalculations)
            .Concat(step.RandomInputs.Select(input => input.Calculation).OfType<Core.Calculations.CalculationResult>())
            .Concat(step.RandomInputs.SelectMany(input => input.Captures.Values))
            .Concat(step.Calculation == null ? [] : new[] { step.Calculation })
            .Concat(step.SequenceBudgets.Select(budget => budget.Capture))
            .Concat(step.Continuation?.Overflow == null ? [] : new[] { step.Continuation.Overflow }))
        .DistinctBy(calculation => calculation.Fingerprint).ToImmutableArray();
}

public interface IRunActivityEffectExecutor
{
    Result<RunActivityEffectResult> Execute(
        RunState run,
        RunMapNodeState node,
        RunActivityBoundary boundary);
}

/// <summary>
/// Adapts run activity boundaries to the universal effect executor. The
/// synthetic owner exposes run resources through the same resource processor;
/// deck and modifier effects already reduce the immutable run snapshot.
/// </summary>
public sealed class RunActivityEffectExecutor(IEffectTriggerExecutor effects) : IRunActivityEffectExecutor
{
    public Result<RunActivityEffectResult> Execute(
        RunState run,
        RunMapNodeState node,
        RunActivityBoundary boundary)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(node);
        var definitions = boundary == RunActivityBoundary.Entry ? node.EntryEffects : node.ExitEffects;
        if (definitions.Count == 0)
            return Result<RunActivityEffectResult>.Success(new RunActivityEffectResult { State = run });
        if (run.ActiveEncounterId != null)
            return Result<RunActivityEffectResult>.Failure("Persistent activity effects cannot execute during an active encounter");
        var binding = boundary == RunActivityBoundary.Entry ? node.EntryEffectOwner : node.ExitEffectOwner;
        if (!Enum.IsDefined(binding)) return Result<RunActivityEffectResult>.Failure("Invalid activity effect owner binding");
        var playerResources = run.PlayerEntity?.Component<ResourceEntityComponentState>();
        if (binding == RunActivityEffectOwner.PlayerEntity && playerResources == null)
            return Result<RunActivityEffectResult>.Failure("PlayerEntity activity effects require persistent actor resources");

        var invalid = false;
        EffectDefinitionValidator.Validate(definitions, (effect, _) => invalid |= effect.Chance != 1 ||
            effect.Target is not (EffectTarget.SELF or EffectTarget.TARGET) ||
            effect.Type is EffectType.APPLY_STATUS or EffectType.REMOVE_STATUS or EffectType.DISPEL_STATUS ||
            effect.Type == EffectType.MODIFY_ATTRIBUTE && effect.AttributeMutation?.Lifetime != Core.Entity.AttributeLifetime.RunBase);
        if (invalid)
        {
            return Result<RunActivityEffectResult>.Failure(
                "Run activity effects must be guaranteed, target the run owner, and persist in run state");
        }

        var resourceComponentId = playerResources?.ComponentId ?? "resources";
        var owner = new CombatActorState
        {
            InstanceId = run.PlayerEntityId,
            DefinitionId = run.PlayerEntity?.DefinitionId ?? "run-owner",
            ContentRevision = run.Determinism.ContentRevision,
            Name = run.PlayerEntity?.Name ?? run.PlayerEntityId,
            SideId = "run-owner",
            ControllerBinding = new ControllerBinding { Kind = ControllerKind.Player },
            Components = (run.PlayerEntity?.Components ?? ImmutableDictionary<string, EntityComponentState>.Empty)
                .Where(pair => pair.Value is not ResourceEntityComponentState)
                .ToImmutableDictionary(StringComparer.Ordinal).SetItem(resourceComponentId, new ResourceEntityComponentState
                {
                    ComponentId = resourceComponentId,
                    State = binding == RunActivityEffectOwner.PlayerEntity ? playerResources!.State : run.ResourceState
                })
        };
        var synthetic = new CombatState
        {
            CombatId = Guid.Empty,
            RunId = run.RunId,
            RunNodeId = node.NodeId,
            Actors = new Dictionary<string, CombatActorState>(StringComparer.Ordinal)
            {
                [owner.InstanceId] = owner
            },
            Determinism = run.Determinism,
            Status = CombatStatus.ACTIVE,
            Sides = [new CombatSide { SideId = "run-owner" }]
        };
        var trigger = new EffectTriggerDefinition
        {
            TriggerId = $"run-activity:{node.NodeId}:{boundary}",
            Boundary = boundary.ToString(),
            Effects = definitions
        };
        var executed = effects.Execute(new EffectTriggerExecutionRequest
        {
            Combat = synthetic,
            Run = run,
            Trigger = trigger,
            OwnerEntityId = run.PlayerEntityId,
            SourceEntityId = run.PlayerEntityId,
            SelectedTargetEntityIds = [run.PlayerEntityId],
            ContentRevision = run.Determinism.ContentRevision,
            Provenance = new EffectProvenance
            {
                Kind = EffectProvenanceKind.Rule,
                SourceId = node.NodeId,
                ComponentId = boundary.ToString()
            }
        });
        if (executed.IsFailure)
            return Result<RunActivityEffectResult>.Failure(executed.Error);

        var state = (executed.Value.Run ?? run) with
        {
            Determinism = executed.Value.Run?.Determinism ?? executed.Value.State.Determinism
        };
        var updatedResources = executed.Value.State.GetActor(run.PlayerEntityId)!.ResourceState;
        if (binding == RunActivityEffectOwner.RunWallet)
            state = state with { ResourceState = updatedResources };
        else
            state = state with { PlayerEntity = state.PlayerEntity! with
            { Components = state.PlayerEntity!.Components.ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem(resourceComponentId, playerResources! with { State = updatedResources }) } };
        return Result<RunActivityEffectResult>.Success(new RunActivityEffectResult
        {
            State = state,
            Steps = executed.Value.Steps,
            Applications = executed.Value.Records.ToImmutableArray(),
            Fingerprint = executed.Value.Fingerprint
        });
    }
}
