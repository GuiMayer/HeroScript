using System.Collections.Immutable;
using Core.CardZones;
using Core.Common;
using Core.Determinism;
using Core.Combat.Modifiers;
using Core.Resources;
using Core.Combat.Models;

namespace Core.Run;

public sealed record PlannedModifierGrant(
    Guid InstanceId,
    string OwnerId,
    string ModifierId,
    int Stacks,
    int? Duration,
    string SourceId);

public sealed record PreparationApplyPlan(
    Guid PreparationInstanceId,
    string OptionId,
    DeterministicContext SourceContext,
    DeterministicContext Context,
    ImmutableArray<PlannedModifierGrant> ModifierGrants);

public static class PreparationTransitions
{
    public static RunStateTransition<PreparationState> Create(
        RunState state,
        PreparationDefinition definition,
        string nodeId = "")
    {
        var instanceId = state.Determinism.AllocateId("preparation");
        var preparation = new PreparationState
        {
            PreparationInstanceId = instanceId.Value,
            RunId = state.RunId,
            NodeId = nodeId,
            PreparationId = definition.PreparationId,
            Options = definition.Options.Select(option => new PreparationOptionState
            {
                OptionId = option.OptionId,
                Costs = option.Costs,
                GrantedCardIds = option.GrantedCardIds,
                ApplyModifiers = option.ApplyModifiers.Select(modifier => new PreparationModifierGrantState
                {
                    OwnerId = modifier.OwnerId,
                    ModifierId = modifier.ModifierId,
                    Stacks = modifier.Stacks,
                    Duration = modifier.Duration,
                    SourceId = modifier.SourceId
                }).ToImmutableList()
            }).ToImmutableList()
        };
        var next = state with
        {
            Preparations = state.Preparations.Add(preparation),
            Determinism = instanceId.Context.AdvanceStep()
        };
        return new RunStateTransition<PreparationState>(next, preparation);
    }

    public static Result<PreparationApplyPlan> PlanApply(
        RunState state,
        Guid preparationInstanceId,
        string optionId)
    {
        var located = Locate(state, preparationInstanceId, optionId);
        if (located.IsFailure)
            return Result<PreparationApplyPlan>.Failure(located.Error);

        var option = located.Value.Option;
        if (option.Applied)
            return Result<PreparationApplyPlan>.Failure($"Preparation option already applied: {optionId}");
        var affordability = RunResourceTransitions.Spend(
            state.ResourceState,
            option.Costs,
            $"preparation:{preparationInstanceId}:plan:{optionId}");
        if (affordability.IsFailure)
            return Result<PreparationApplyPlan>.Failure(affordability.Error);

        var context = state.Determinism;
        var grants = ImmutableArray.CreateBuilder<PlannedModifierGrant>(option.ApplyModifiers.Count);
        foreach (var modifier in option.ApplyModifiers)
        {
            var ownerId = ResolveModifierOwner(state, modifier.OwnerId);
            var sourceId = string.IsNullOrWhiteSpace(modifier.SourceId) ? option.OptionId : modifier.SourceId;
            var allocation = context.AllocateId($"script-modifier:{ownerId}:{modifier.ModifierId}");
            context = allocation.Context;
            grants.Add(new PlannedModifierGrant(
                allocation.Value,
                ownerId,
                modifier.ModifierId,
                modifier.Stacks,
                modifier.Duration,
                sourceId!));
        }

        return Result<PreparationApplyPlan>.Success(new(
            preparationInstanceId,
            optionId,
            state.Determinism,
            context,
            grants.MoveToImmutable()));
    }

    public static Result<RunStateTransition<PreparationOptionState>> CommitApply(
        RunState state,
        PreparationApplyPlan plan,
        IReadOnlyList<ScriptModifierInstance> modifiers,
        ICardZoneFlowExecutor? zoneFlows = null)
    {
        if (state.Determinism != plan.SourceContext)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(
                "Preparation plan is stale for the current deterministic context");

        if (modifiers.Count != plan.ModifierGrants.Length)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(
                "Applied modifier count does not match the deterministic preparation plan");

        var located = Locate(state, plan.PreparationInstanceId, plan.OptionId);
        if (located.IsFailure)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(located.Error);

        var (preparationIndex, optionIndex, preparation, option) = located.Value;
        if (option.Applied)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(
                $"Preparation option already applied: {option.OptionId}");
        var spent = RunResourceTransitions.Spend(
            state.ResourceState,
            option.Costs,
            $"preparation:{plan.PreparationInstanceId}:apply:{option.OptionId}");
        if (spent.IsFailure)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(spent.Error);

        var deck = CardZoneGrantTransitions.Grant(state with { Determinism = plan.Context },
            option.GrantedCardIds, zoneFlows);
        if (deck.IsFailure)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(deck.Error);

        for (var index = 0; index < modifiers.Count; index++)
        {
            var modifier = modifiers[index];
            var grant = plan.ModifierGrants[index];
            if (modifier.InstanceId != grant.InstanceId ||
                !string.Equals(modifier.ModifierId, grant.ModifierId, StringComparison.Ordinal) ||
                !string.Equals(modifier.OwnerId, grant.OwnerId, StringComparison.Ordinal))
            {
                return Result<RunStateTransition<PreparationOptionState>>.Failure(
                    $"Resolved modifier does not match preparation grant: {grant.ModifierId}");
            }
        }

        var updatedModifiers = state with { Determinism = plan.Context };
        var appliedIds = new List<Guid>();
        foreach (var modifier in modifiers)
        {
            var owner = modifier.OwnerId.StartsWith("run:", StringComparison.Ordinal)
                ? new GameplayOwner { Kind = GameplayOwnerKind.Run, Id = modifier.OwnerId[4..] }
                : new GameplayOwner { Kind = GameplayOwnerKind.Entity, Id = modifier.OwnerId };
            var applied = ModifierTransitions.Apply(updatedModifiers, modifier.Definition, owner, modifier.SourceId,
                modifier.Stacks, modifier.Duration, modifier.InstanceId);
            if (applied.IsFailure) return Result<RunStateTransition<PreparationOptionState>>.Failure(applied.Error);
            updatedModifiers = applied.Value.Run;
            appliedIds.Add(applied.Value.Instance.InstanceId);
        }

        var updatedOption = option with
        {
            Applied = true,
            AppliedModifierInstanceIds = appliedIds.ToArray()
        };
        var updatedPreparation = preparation with
        {
            Options = preparation.Options.ToImmutableList().SetItem(optionIndex, updatedOption),
            AppliedOptionIds = preparation.AppliedOptionIds.Append(option.OptionId).ToImmutableList()
        };
        var next = state with
        {
            ResourceState = spent.Value.State,
            Deck = deck.Value.State,
            Preparations = state.Preparations.SetItem(preparationIndex, updatedPreparation),
            Modifiers = updatedModifiers.Modifiers,
            Determinism = deck.Value.Context.AdvanceStep()
        };
        return Result<RunStateTransition<PreparationOptionState>>.Success(new(next, updatedOption));
    }

    private static Result<(int PreparationIndex, int OptionIndex, PreparationState Preparation, PreparationOptionState Option)> Locate(
        RunState state,
        Guid preparationInstanceId,
        string optionId)
    {
        for (var preparationIndex = 0; preparationIndex < state.Preparations.Length; preparationIndex++)
        {
            var preparation = state.Preparations[preparationIndex];
            if (preparation.PreparationInstanceId != preparationInstanceId)
                continue;

            for (var optionIndex = 0; optionIndex < preparation.Options.Count; optionIndex++)
            {
                var option = preparation.Options[optionIndex];
                if (option.OptionId == optionId)
                {
                    return Result<(int, int, PreparationState, PreparationOptionState)>.Success(
                        (preparationIndex, optionIndex, preparation, option));
                }
            }

            return Result<(int, int, PreparationState, PreparationOptionState)>.Failure(
                $"Preparation option not found: {optionId}");
        }

        return Result<(int, int, PreparationState, PreparationOptionState)>.Failure(
            $"Preparation not found: {preparationInstanceId}");
    }

    private static string ResolveModifierOwner(RunState state, string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Equals("run", StringComparison.OrdinalIgnoreCase))
            return $"run:{state.RunId}";
        return ownerId.Equals("player", StringComparison.OrdinalIgnoreCase)
            ? state.PlayerEntityId
            : ownerId;
    }

}
