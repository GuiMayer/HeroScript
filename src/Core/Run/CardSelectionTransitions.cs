using System.Collections.Immutable;
using Core.Common;

namespace Core.Run;

public static class CardSelectionTransitions
{
    public static RunStateTransition<CardSelectionState> Create(
        RunState state,
        CardSelectionDefinition definition,
        IReadOnlyList<CardSelectionOptionState> options)
    {
        var instanceId = state.Determinism.AllocateId("card-selection");
        var selection = new CardSelectionState
        {
            SelectionInstanceId = instanceId.Value,
            RunId = state.RunId,
            SelectionId = definition.SelectionId,
            PickCount = definition.PickCount,
            OfferCount = definition.OfferCount,
            CardPoolId = definition.CardPoolId,
            Reroll = definition.Reroll,
            Decompose = definition.Decompose,
            FreeRerollsRemaining = definition.Reroll.FreeRerolls,
            RerollCostGold = CalculateRerollCost(definition.Reroll, 0),
            Options = options
        };

        var next = state with
        {
            CardSelections = state.CardSelections.Add(selection),
            Determinism = instanceId.Context.AdvanceStep()
        };
        return new RunStateTransition<CardSelectionState>(next, selection);
    }

    public static Result<RunStateTransition<CardSelectionState>> Pick(
        RunState state,
        Guid selectionInstanceId,
        IReadOnlyList<string> cardIds)
    {
        var located = Locate(state, selectionInstanceId);
        if (located.IsFailure)
            return Result<RunStateTransition<CardSelectionState>>.Failure(located.Error);

        var (index, selection) = located.Value;
        if (selection.Completed)
            return Result<RunStateTransition<CardSelectionState>>.Failure(
                $"Card selection already completed: {selectionInstanceId}");

        var picks = cardIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        if (picks.IsEmpty || picks.Length > selection.PickCount)
            return Result<RunStateTransition<CardSelectionState>>.Failure(
                $"Pick between 1 and {selection.PickCount} cards");

        var available = selection.Options
            .Where(option => !option.Decomposed)
            .Select(option => option.CardId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalid = picks.Where(id => !available.Contains(id)).ToArray();
        if (invalid.Length > 0)
            return Result<RunStateTransition<CardSelectionState>>.Failure(
                $"Invalid card options: {string.Join(", ", invalid)}");

        var deck = DeckTransitions.AddToDiscard(state.Deck, picks, state.Determinism);
        if (deck.IsFailure)
            return Result<RunStateTransition<CardSelectionState>>.Failure(deck.Error);

        var updated = selection with
        {
            Completed = true,
            PickedCardIds = selection.PickedCardIds.Concat(picks).ToImmutableList()
        };
        var next = state with
        {
            Deck = deck.Value.State,
            CardSelections = state.CardSelections.SetItem(index, updated),
            Determinism = deck.Value.Context.AdvanceStep()
        };
        return Result<RunStateTransition<CardSelectionState>>.Success(new(next, updated));
    }

    public static Result<RunStateTransition<CardSelectionState>> Reroll(
        RunState state,
        Guid selectionInstanceId,
        IReadOnlyList<CardSelectionOptionState> options)
    {
        var located = Locate(state, selectionInstanceId);
        if (located.IsFailure)
            return Result<RunStateTransition<CardSelectionState>>.Failure(located.Error);

        var (index, selection) = located.Value;
        if (selection.Completed)
            return Result<RunStateTransition<CardSelectionState>>.Failure(
                $"Card selection already completed: {selectionInstanceId}");

        var cost = selection.FreeRerollsRemaining > 0 ? 0 : selection.RerollCostGold;
        if (state.Gold < cost)
            return Result<RunStateTransition<CardSelectionState>>.Failure(
                $"Insufficient gold for reroll: {selection.SelectionId}");

        var rerollsUsed = checked(selection.RerollsUsed + 1);
        var updated = selection with
        {
            RerollsUsed = rerollsUsed,
            FreeRerollsRemaining = System.Math.Max(0, selection.FreeRerollsRemaining - 1),
            RerollCostGold = CalculateRerollCost(selection.Reroll, rerollsUsed),
            Options = options
        };
        var next = state with
        {
            Gold = state.Gold - cost,
            CardSelections = state.CardSelections.SetItem(index, updated),
            Determinism = state.Determinism.AdvanceStep()
        };
        return Result<RunStateTransition<CardSelectionState>>.Success(new(next, updated));
    }

    public static Result<RunStateTransition<CardSelectionState>> Decompose(
        RunState state,
        Guid selectionInstanceId,
        string cardId)
    {
        var located = Locate(state, selectionInstanceId);
        if (located.IsFailure)
            return Result<RunStateTransition<CardSelectionState>>.Failure(located.Error);

        var (selectionIndex, selection) = located.Value;
        if (selection.Completed)
            return Result<RunStateTransition<CardSelectionState>>.Failure(
                $"Card selection already completed: {selectionInstanceId}");
        if (!selection.Decompose.Enabled)
            return Result<RunStateTransition<CardSelectionState>>.Failure(
                $"Decompose is disabled for card selection: {selection.SelectionId}");

        var optionIndex = selection.Options
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase));
        if (optionIndex.option == null)
            return Result<RunStateTransition<CardSelectionState>>.Failure($"Card option not found: {cardId}");
        if (optionIndex.option.Decomposed)
            return Result<RunStateTransition<CardSelectionState>>.Failure($"Card option already decomposed: {cardId}");

        var updatedOption = optionIndex.option with { Decomposed = true };
        var options = selection.Options.ToImmutableList().SetItem(optionIndex.index, updatedOption);
        var updated = selection with
        {
            Options = options,
            DecomposedCardIds = selection.DecomposedCardIds.Append(updatedOption.CardId).ToImmutableList()
        };
        var next = state with
        {
            PowerPoints = checked(state.PowerPoints + updatedOption.DecomposePowerPoints),
            CardSelections = state.CardSelections.SetItem(selectionIndex, updated),
            Determinism = state.Determinism.AdvanceStep()
        };
        return Result<RunStateTransition<CardSelectionState>>.Success(new(next, updated));
    }

    private static Result<(int Index, CardSelectionState Selection)> Locate(RunState state, Guid instanceId)
    {
        for (var index = 0; index < state.CardSelections.Length; index++)
        {
            if (state.CardSelections[index].SelectionInstanceId == instanceId)
                return Result<(int, CardSelectionState)>.Success((index, state.CardSelections[index]));
        }

        return Result<(int, CardSelectionState)>.Failure($"Card selection not found: {instanceId}");
    }

    private static int CalculateRerollCost(RerollRulesDefinition rules, int rerollsUsed) =>
        checked(rules.BaseGoldCost + System.Math.Max(0, rerollsUsed) * rules.GoldCostPerReroll);
}
