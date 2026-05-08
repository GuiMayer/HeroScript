namespace Core.Validation;

/// <summary>
/// Resultado base de validação.
/// Pode ser usado diretamente ou estendido para validações específicas.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Indica se a validação foi bem-sucedida.
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Lista de erros encontrados durante a validação.
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// Lista de avisos encontrados durante a validação.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Adiciona um erro à lista de erros.
    /// </summary>
    public void AddError(string error) => Errors.Add(error);
    
    /// <summary>
    /// Adiciona um aviso à lista de avisos.
    /// </summary>
    public void AddWarning(string warning) => Warnings.Add(warning);
}
