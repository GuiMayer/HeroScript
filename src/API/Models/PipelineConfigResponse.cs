namespace API.Models;

/// <summary>
/// Response com configuração atual do pipeline
/// </summary>
public class PipelineConfigResponse
{
    /// <summary>
    /// Número de buckets no pipeline
    /// </summary>
    public int BucketCount { get; set; }
    
    /// <summary>
    /// Lista de buckets na ordem de execução
    /// </summary>
    public List<BucketInfoDto> Buckets { get; set; } = new();
}

/// <summary>
/// Informações de um bucket
/// </summary>
public class BucketInfoDto
{
    /// <summary>
    /// ID do bucket
    /// </summary>
    public string BucketId { get; set; } = string.Empty;
    
    /// <summary>
    /// Descrição do bucket
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Número de operações no bucket
    /// </summary>
    public int OperationCount { get; set; }
    
    /// <summary>
    /// Se o bucket emite eventos
    /// </summary>
    public bool EmitEvents { get; set; }
}
