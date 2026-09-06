using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Resources;

namespace Core.Run;

public interface IRunManager
{
    Result<RunState> StartRun(string configName = "default", string runDefinitionId = "default_run", string playerEntityId = "player");
    Result<RunState> StartRun(RunStartOptions options);
    Result<RunState> GetRun(Guid runId);
    Result<RunState> GetRunByCombat(Guid combatId);
    Result<IReadOnlyList<RunAvailableCommand>> GetAvailableCommands(Guid runId);
    Result<RunMapNodeState> ResolveCurrentNode(Guid runId, string currentNodeId);
    Result<RunMapNodeState> AdvanceNode(Guid runId, string targetNodeId);
    Result<RunState> AttachEncounter(
        Guid runId,
        int expectedSequence,
        ulong expectedStep,
        CombatState combatState,
        RunCommandIdentity? commandIdentity = null,
        RunState? initializedRun = null,
        RunEncounterStartCommand? initialCommand = null,
        CombatState? stateBeforeInitialization = null,
        CombatResolutionStep? initializationStep = null);
    Result<RunState> CommitCombatAction(
        Guid runId,
        int expectedSequence,
        CombatState previousCombat,
        CombatState nextCombat,
        CombatActionCommand command,
        string? consumedCardId,
        CardConsumeDestination destination,
        RunCommandIdentity? commandIdentity = null);
    Result<RunState> ResolveEncounter(
        Guid runId,
        int expectedSequence,
        Guid combatId,
        RunCommandIdentity? commandIdentity = null);
    Result<RunState> ApplyRunResource(
        Guid runId,
        string resourceId,
        float value,
        ResourceEffectOperation operation,
        ResourceValueField field = ResourceValueField.Current);
    Result<IReadOnlyList<string>> DrawCards(Guid runId, int count);
    Result<IReadOnlyList<string>> DiscardCards(Guid runId, IReadOnlyList<string> cardIds);
    Result<IReadOnlyList<string>> ExhaustCards(Guid runId, IReadOnlyList<string> cardIds);
    Result<IReadOnlyList<string>> AddCardsToHand(Guid runId, IReadOnlyList<string> cardIds);
    Result<bool> HasCardInHand(Guid runId, string cardId);
    Result<IReadOnlyList<string>> ConsumeCardsFromHand(Guid runId, IReadOnlyList<string> cardIds, CardConsumeDestination destination);
    Result ShuffleDiscardIntoDrawPile(Guid runId);
    Result<CardSelectionState> CreateCardSelection(Guid runId, string selectionId);
    Result<CardSelectionState> PickCards(Guid runId, Guid selectionInstanceId, IReadOnlyList<string> cardIds);
    Result<CardSelectionState> RerollCardSelection(Guid runId, Guid selectionInstanceId, IReadOnlyList<string>? lockedCardIds = null);
    Result<CardSelectionState> DecomposeCardSelectionOption(Guid runId, Guid selectionInstanceId, string cardId);
    Result<ShopState> CreateShop(Guid runId, string shopId);
    Result<ShopItemState> BuyShopItem(Guid runId, Guid shopInstanceId, string itemId);
    Result<ShopState> RerollShop(Guid runId, Guid shopInstanceId);
    Result<PreparationState> CreatePreparation(Guid runId, string preparationId);
    Result<PreparationOptionState> ApplyPreparationOption(Guid runId, Guid preparationInstanceId, string optionId);
    Result<RunState> RestoreState(RunState state);
}
