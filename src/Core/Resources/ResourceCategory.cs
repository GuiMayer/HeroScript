namespace Core.Resources;

/// <summary>
/// Categorias de recursos no sistema.
/// </summary>
public enum ResourceCategory
{
    /// <summary>
    /// Recursos classificados como vitais para consulta e apresentação.
    /// A categoria não implica derrota ou qualquer outra consequência.
    /// </summary>
    VITAL,
    
    /// <summary>
    /// Recursos classificados como táticos para consulta e apresentação.
    /// </summary>
    TACTICAL,
    
    /// <summary>
    /// Recursos classificados como especiais para consulta e apresentação.
    /// </summary>
    SPECIAL,
    
    /// <summary>
    /// Recursos classificados como temporários para consulta e apresentação.
    /// </summary>
    TEMPORARY
}
