using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Run;

namespace Core.GameplayEvents.Responses;

/// <summary>Trusted captured timing adapter, not a second effect format and never stored in a snapshot.</summary>
public sealed record GameplayBoundaryInvocation
{
    public required EffectTriggerExecutionRequest Template { get; init; }
    public required GameplayEventOrigin Origin { get; init; }
    public required string Kind { get; init; }
    public required string BoundaryId { get; init; }
    public GameplayResponseScheduleKey Order { get; init; } = new();
    public Guid? StatusInstanceId { get; init; }
    public GameplayOwner? RequiredOwner { get; init; }
    public bool RequireActiveOwner { get; init; }

    internal EffectTriggerExecutionRequest? Resolve(RunState run, CombatState combat)
    {
        var request = Template with { Run = run, Combat = combat };
        if (RequiredOwner != null && (combat.GetActor(request.OwnerEntityId) is not { } owner || !RequiredOwner.Includes(owner, combat, run))) return null;
        if (RequireActiveOwner && combat.ActivationState?.ActiveActorId != request.OwnerEntityId) return null;
        if (StatusInstanceId is not { } statusId) return request;
        var status = combat.StatusEffects.GetValueOrDefault(request.OwnerEntityId, []).FirstOrDefault(item => item.InstanceId == statusId && item.IsActive);
        return status == null ? null : request with { StackPayloadLots = status.PayloadLots,
            Variables = new Dictionary<string, float>(StringComparer.Ordinal) { ["stacks"] = status.Stacks, ["duration"] = status.Duration } };
    }
}

public sealed record GameplayBoundaryInvocationResult(GameplayBoundaryInvocation Invocation, EffectBatchResult Execution);
public sealed record GameplayBoundaryDispatchResult(EffectBatchResult Execution, GameplayEffectObservation? Observation,
    ImmutableArray<GameplayBoundaryInvocationResult> Invocations);

public interface IGameplayBoundaryExecutionScope
{
    Result<GameplayBoundaryDispatchResult> Dispatch(RunState run, CombatState combat, string kind, string boundaryId,
        string? actorId, IReadOnlyList<GameplayBoundaryInvocation> invocations, GameplayEffectObservation? observation = null);
}

internal sealed class CapturedBoundaryObserverProvider(IReadOnlyList<GameplayBoundaryInvocation> invocations) : IGameplayObserverProvider
{
    public string ProviderId => "core.captured-boundary";
    public Result<ImmutableArray<GameplayObserverRegistration>> Collect(GameplayObserverCollectionContext context)
    {
        if (invocations.Count > HierarchySelectorIndex.MaximumRegistrations)
            return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("Captured boundary exceeds the registration budget");
        var result = ImmutableArray.CreateBuilder<GameplayObserverRegistration>();
        foreach (var (invocation, index) in invocations.Select((item, index) => (item, index)))
        {
            var template = invocation.Template;
            var definition = new GameplayResponseDefinition
            {
                ResponseId = template.Trigger.TriggerId,
                Observe = new() { Kinds = new() { Include = [invocation.Kind] }, Boundary = new() { BoundaryIds = [invocation.BoundaryId] } },
                Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner }, Missing = MissingResponseBinding.RejectAction },
                Availability = new() { RequireLivingOwner = false }, Propagation = ResponsePropagation.AllowResponses,
                Order = new() { Priority = template.Trigger.Priority, Descending = true },
                Effects = template.Trigger.Effects.ToImmutableArray()
            };
            var compiled = GameplayResponseCompiler.Compile(definition, context.Catalog, new() { EmptyEffects = true, MaximumEffects = EffectExecutionLimits.MaximumSteps });
            if (compiled.IsFailure) return Result<ImmutableArray<GameplayObserverRegistration>>.Failure(compiled.Error);
            var identity = CanonicalJson.ComputeHash(new { invocation.Origin, template.Trigger, template.OwnerEntityId, template.SourceEntityId,
                template.ContentRevision, template.Provenance, template.SelectedTargetEntityIds, invocation.StatusInstanceId });
            result.Add(new() { RegistrationId = $"{index:D6}:{identity}", ProviderId = ProviderId, Response = compiled.Value,
                Origin = invocation.Origin, OwnerActorId = template.OwnerEntityId, SourceActorId = template.OwnerEntityId,
                Order = invocation.Order, BoundaryExecution = invocation });
        }
        return Result<ImmutableArray<GameplayObserverRegistration>>.Success(result.ToImmutable());
    }
}
