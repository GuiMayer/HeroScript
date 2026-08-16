using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

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
        PreparationDefinition definition)
    {
        var instanceId = state.Determinism.AllocateId("preparation");
        var preparation = new PreparationState
        {
            PreparationInstanceId = instanceId.Value,
            RunId = state.RunId,
            PreparationId = definition.PreparationId,
            Options = definition.Options.Select(option => new PreparationOptionState
            {
                OptionId = option.OptionId,
                GoldCost = option.GoldCost,
                PowerPointCost = option.PowerPointCost,
                AddCardsToDiscard = option.AddCardsToDiscard,
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
        if (state.Gold < option.GoldCost || state.PowerPoints < option.PowerPointCost)
            return Result<PreparationApplyPlan>.Failure($"Insufficient resources for preparation option: {optionId}");

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
        IReadOnlyList<Guid> appliedModifierIds)
    {
        if (state.Determinism != plan.SourceContext)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(
                "Preparation plan is stale for the current deterministic context");

        if (appliedModifierIds.Count != plan.ModifierGrants.Length)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(
                "Applied modifier count does not match the deterministic preparation plan");

        var located = Locate(state, plan.PreparationInstanceId, plan.OptionId);
        if (located.IsFailure)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(located.Error);

        var (preparationIndex, optionIndex, preparation, option) = located.Value;
        if (option.Applied)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(
                $"Preparation option already applied: {option.OptionId}");
        if (state.Gold < option.GoldCost || state.PowerPoints < option.PowerPointCost)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(
                $"Insufficient resources for preparation option: {option.OptionId}");

        var deck = DeckTransitions.AddToDiscard(state.Deck, option.AddCardsToDiscard, plan.Context);
        if (deck.IsFailure)
            return Result<RunStateTransition<PreparationOptionState>>.Failure(deck.Error);

        var updatedOption = option with
        {
            Applied = true,
            AppliedModifierInstanceIds = appliedModifierIds
        };
        var updatedPreparation = preparation with
        {
            Options = preparation.Options.ToImmutableList().SetItem(optionIndex, updatedOption),
            AppliedOptionIds = preparation.AppliedOptionIds.Append(option.OptionId).ToImmutableList()
        };
        var next = state with
        {
            Gold = state.Gold - option.GoldCost,
            PowerPoints = state.PowerPoints - option.PowerPointCost,
            Deck = deck.Value.State,
            Preparations = state.Preparations.SetItem(preparationIndex, updatedPreparation),
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
