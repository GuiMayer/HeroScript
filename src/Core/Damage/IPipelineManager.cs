namespace Core.Damage;

/// <summary>
/// Interface para gerenciamento do pipeline de dano
/// </summary>
public interface IPipelineManager
{
    /// <summary>
    /// Executa o pipeline completo de dano
    /// </summary>
    DamageContext ExecutePipeline(DamageContext initialContext);
    
    /// <summary>
    /// Recarrega configuração do pipeline (dev mode)
    /// </summary>
    void ReloadConfiguration(System.Collections.Generic.IEnumerable<string> configChain);
    
    /// <summary>
    /// Obtém configuração atual do pipeline
    /// </summary>
    PipelineConfiguration GetCurrentConfiguration();
}
