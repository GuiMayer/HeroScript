using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Common;
using Core.Determinism;
using Core.Entity.Definitions;
using Core.Entity.Integration;
using Core.Resources;
using Core.StatusEffects;

namespace Core.Combat;

/// <summary>Pure initial-snapshot boundary from explicit deterministic inputs.</summary>
public sealed class CombatFactory : ICombatFactory
{
    private readonly ITurnOrderCalculator _turnOrder;
    private readonly EntityDefinitionLoader? _entities;
    private readonly EntityMaterializer _materializer;

    public CombatFactory(IResourceManager resources, ITurnOrderCalculator turnOrder, EntityDefinitionLoader? entities = null)
    {
        _turnOrder = turnOrder ?? throw new ArgumentNullException(nameof(turnOrder));
        _entities = entities;
        _materializer = new EntityMaterializer(resources ?? throw new ArgumentNullException(nameof(resources)));
    }

    public Result<CombatState> Create(IReadOnlyList<CombatParticipantReference> participants, CombatStartOptions options)
    {
        var validation = ValidateReferences(participants, options);
        if (validation.IsFailure)
            return Result<CombatState>.Failure(validation.Error);
        if (_entities == null)
            return Result<CombatState>.Failure("Entity definition catalog is unavailable");

        var actors = new List<CombatActorState>(participants.Count);
        foreach (var participant in participants)
        {
            var definition = _entities.LoadDefinition(participant.DefinitionId, options.ContentRevision);
            if (definition.IsFailure)
                return Result<CombatState>.Failure(definition.Error);
            var actor = _materializer.Materialize(definition.Value, participant.InstanceId,
                options.ContentRevision, participant.SideId, participant.ControllerBinding);
            if (actor.IsFailure)
                return Result<CombatState>.Failure(actor.Error);
            actors.Add(actor.Value);
        }
        return Create(actors, options);
    }

    public Result<CombatState> Create(IReadOnlyList<CombatActorState> participants, CombatStartOptions options)
    {
        var validation = ValidateActors(participants, options);
        if (validation.IsFailure)
            return Result<CombatState>.Failure(validation.Error);

        var actors = new List<CombatActorState>(participants.Count);
        foreach (var actor in participants)
        {
            var overridden = ApplyInitialResourceValues(actor, options.InitialResourceValues);
            if (overridden.IsFailure)
                return Result<CombatState>.Failure(overridden.Error);
            actors.Add(overridden.Value);
        }

        var combat = CombatTransitions.Create(actors,
            DeterministicContext.Create(options.Seed!.Value, options.ContentRevision), options.IdScope ?? "combat") with
        {
            RunId = options.RunId,
            RunNodeId = options.RunNodeId
        };
        combat = ApplyInitialStatusEffects(combat, options.InitialStatusEffects);
        var initialized = _turnOrder.InitializeState(combat);
        if (initialized.IsFailure)
            return Result<CombatState>.Failure($"Failed to initialize turn order calculator: {initialized.Error}");
        var calculated = _turnOrder.Calculate(initialized.Value);
        return calculated.IsFailure
            ? Result<CombatState>.Failure($"Failed to calculate turn order: {calculated.Error}")
            : Result<CombatState>.Success(calculated.Value.State with { TurnOrder = calculated.Value.Order });
    }

    private static Result ValidateReferences(IReadOnlyList<CombatParticipantReference>? participants, CombatStartOptions options)
    {
        if (participants == null || participants.Count < 1)
            return Result.Failure("At least one combat participant is required");
        if (participants.Any(item => item == null || string.IsNullOrWhiteSpace(item.InstanceId) ||
            string.IsNullOrWhiteSpace(item.DefinitionId) || string.IsNullOrWhiteSpace(item.SideId) || item.ControllerBinding == null))
            return Result.Failure("Every participant requires instanceId, definitionId, sideId and controllerBinding");
        foreach (var participant in participants)
        {
            var binding = ControllerBindingValidator.Validate(participant.ControllerBinding);
            if (binding.IsFailure)
                return Result.Failure($"Combat participant '{participant.InstanceId}': {binding.Error}");
        }
        return ValidateCommon(participants.Select(item => item.InstanceId), participants.Select(item => item.SideId), options);
    }

    private static Result ValidateActors(IReadOnlyList<CombatActorState>? participants, CombatStartOptions options)
    {
        if (participants == null || participants.Count < 1)
            return Result.Failure("At least one combat participant is required");
        if (participants.Any(item => item == null || string.IsNullOrWhiteSpace(item.InstanceId) ||
            string.IsNullOrWhiteSpace(item.DefinitionId) || string.IsNullOrWhiteSpace(item.ContentRevision) ||
            string.IsNullOrWhiteSpace(item.SideId) || item.ControllerBinding == null || !Enum.IsDefined(item.ControllerBinding.Kind)))
            return Result.Failure("Every combat actor requires immutable identity, content, side and controller binding");
        if (participants.Any(item => !string.Equals(item.ContentRevision, options.ContentRevision, StringComparison.Ordinal)))
            return Result.Failure("Every combat actor must use the combat content revision");
        foreach (var participant in participants)
        {
            var binding = ControllerBindingValidator.Validate(participant.ControllerBinding);
            if (binding.IsFailure)
                return Result.Failure($"Combat participant '{participant.InstanceId}': {binding.Error}");
        }
        return ValidateCommon(participants.Select(item => item.InstanceId), participants.Select(item => item.SideId), options);
    }

    private static Result ValidateCommon(IEnumerable<string> instanceIds, IEnumerable<string> sideIds, CombatStartOptions? options)
    {
        if (options == null || !options.Seed.HasValue || string.IsNullOrWhiteSpace(options.ContentRevision))
            return Result.Failure("Combat seed and contentRevision are required");
        var ids = instanceIds.ToArray();
        var duplicate = ids.GroupBy(id => id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            return Result.Failure($"Combat participant instance IDs must be unique: {duplicate.Key}");
        if (sideIds.Any(string.IsNullOrWhiteSpace))
            return Result.Failure("Every combat participant requires a sideId");
        if (options.InitialResourceValues?.Keys.Any(id => !ids.Contains(id, StringComparer.Ordinal)) == true)
            return Result.Failure("Initial resource override references an unknown combat participant");
        return Result.Success();
    }

    private static Result<CombatActorState> ApplyInitialResourceValues(
        CombatActorState actor,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>>? valuesByActor)
    {
        if (valuesByActor == null || !valuesByActor.TryGetValue(actor.InstanceId, out var values))
            return Result<CombatActorState>.Success(actor);
        var current = actor;
        foreach (var (resourceId, value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!float.IsFinite(value) || current.GetResource(resourceId) == null)
                return Result<CombatActorState>.Failure($"Invalid initial resource override: {actor.InstanceId}/{resourceId}");
            var applied = current.ApplyResourceMutation($"combat-start:{actor.InstanceId}:{resourceId}", resourceId,
                ResourceMutationOperation.Set, value);
            if (applied.IsFailure)
                return Result<CombatActorState>.Failure(applied.Error);
            current = applied.Value;
        }
        return Result<CombatActorState>.Success(current);
    }

    private static CombatState ApplyInitialStatusEffects(CombatState combat,
        IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>? initialStatuses)
    {
        if (initialStatuses == null || initialStatuses.Count == 0)
            return combat;
        return combat with
        {
            StatusEffects = initialStatuses.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToImmutableDictionary(item => item.Key,
                    item => item.Value.Where(status => status.IsActive).OrderBy(status => status.InstanceId).ToImmutableArray(),
                    StringComparer.Ordinal)
        };
    }
}
