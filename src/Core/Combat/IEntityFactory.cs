namespace Core.Combat;

/// <summary>
/// Interface para criação de entidades de combate
/// </summary>
public interface IEntityFactory
{
    /// <summary>
    /// Cria uma entidade mock para testes/API
    /// </summary>
    /// <param name="entityId">ID da entidade</param>
    /// <param name="health">Vida inicial (padrão: 100)</param>
    /// <returns>Entidade criada</returns>
    CombatEntity CreateMockEntity(string entityId, float health = 100f);
}
