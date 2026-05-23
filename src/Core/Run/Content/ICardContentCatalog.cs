using Core.Common;

namespace Core.Run.Content;

public interface ICardContentCatalog
{
    Result<CardContentDefinition> GetCard(string cardId, string configName = "default");
    Result<IReadOnlyList<CardContentDefinition>> GetAllCards(string configName = "default");
    void Invalidate();
}

public interface ICardPoolResolver
{
    Result<CardPoolDefinition> GetPool(string poolId, string configName = "default");
    Result<CardPoolResult> ResolvePool(string poolId, string configName = "default");
    void Invalidate();
}
