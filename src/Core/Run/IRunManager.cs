using Core.Common;

namespace Core.Run;

public interface IRunManager
{
    Result<RunState> StartRun(string configName = "default", string runDefinitionId = "default_run", string playerEntityId = "player");
    Result<RunState> GetRun(Guid runId);
    Result<RunState> ApplyEconomy(Guid runId, string resource, int amount);
    Result<IReadOnlyList<string>> DrawCards(Guid runId, int count);
    Result<IReadOnlyList<string>> DiscardCards(Guid runId, IReadOnlyList<string> cardIds);
    Result<IReadOnlyList<string>> ExhaustCards(Guid runId, IReadOnlyList<string> cardIds);
    Result<IReadOnlyList<string>> AddCardsToHand(Guid runId, IReadOnlyList<string> cardIds);
    Result ShuffleDiscardIntoDrawPile(Guid runId);
    Result<CardSelectionState> CreateCardSelection(Guid runId, string selectionId);
    Result<CardSelectionState> PickCards(Guid runId, Guid selectionInstanceId, IReadOnlyList<string> cardIds);
}
