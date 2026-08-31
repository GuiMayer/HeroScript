using Core.Common;

namespace Core.Run.Content;

public interface ICardContentCatalog
{
    Result<CardContentDefinition> GetCard(string cardId, string configName = "default");
    Result<IReadOnlyList<CardContentDefinition>> GetAllCards(string configName = "default");
    void Invalidate();
}

public interface IRevisionedCardContentCatalog
{
    Result<CardContentDefinition> GetCard(string cardId, string contentRevision, string? configName = null);
    Result<IReadOnlyList<CardContentDefinition>> GetAllCards(string contentRevision, string? configName = null);
}

public interface ICardPoolResolver
{
    Result<CardPoolDefinition> GetPool(string poolId, string configName = "default");
    Result<CardPoolResult> ResolvePool(string poolId, string configName = "default");
    void Invalidate();
}

public interface IRevisionedCardPoolResolver
{
    Result<CardPoolDefinition> GetPool(string poolId, string contentRevision, string? configName = null);
    Result<CardPoolResult> ResolvePool(string poolId, string contentRevision, string? configName = null);
}
