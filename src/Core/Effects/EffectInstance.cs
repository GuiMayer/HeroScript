namespace Core.Effects;

/// <summary>
/// Instância runtime de um efeito.
/// Representa um efeito específico sendo executado em combate.
/// Imutável - cada mudança cria nova instância.
/// </summary>
public record EffectInstance
{
    /// <summary>
    /// ID único da instância (gerado automaticamente)
    /// </summary>
    public string InstanceId { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Definição do efeito (template)
    /// </summary>
    public EffectDefinition Definition { get; init; } = null!;
    
    // ===== ORIGEM =====
    
    /// <summary>
    /// ID da entidade que originou o efeito
    /// </summary>
    public string SourceEntityId { get; init; } = string.Empty;
    
    /// <summary>
    /// ID da ação que originou o efeito (se aplicável)
    /// </summary>
    public string? SourceActionId { get; init; }
    
    /// <summary>
    /// ID da carta que originou o efeito (se aplicável)
    /// </summary>
    public string? SourceCardId { get; init; }
    
    // ===== ALVO =====
    
    /// <summary>
    /// ID da entidade alvo do efeito
    /// </summary>
    public string TargetEntityId { get; init; } = string.Empty;
    
    // ===== MODIFICADORES =====
    
    /// <summary>
    /// Modificadores aplicados durante a run (relíquias, poderes, etc.)
    /// </summary>
    public List<EffectModifier> AppliedModifiers { get; init; } = new();
    
    // ===== ESTADO DE EXECUÇÃO =====
    
    /// <summary>
    /// Estado atual da execução
    /// </summary>
    public EffectExecutionState State { get; init; } = EffectExecutionState.PENDING;
    
    /// <summary>
    /// Timestamp de criação
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp de execução (quando completado)
    /// </summary>
    public DateTime? ExecutedAt { get; init; }
    
    // ===== RESULTADO =====
    
    /// <summary>
    /// Resultado da execução (após completar)
    /// </summary>
    public EffectResult? Result { get; init; }
    
    // ===== MÉTODOS IMUTÁVEIS =====
    
    /// <summary>
    /// Cria nova instância com modificador adicionado
    /// </summary>
    public EffectInstance WithModifier(EffectModifier modifier)
    {
        var newModifiers = new List<EffectModifier>(AppliedModifiers) { modifier };
        return this with { AppliedModifiers = newModifiers };
    }
    
    /// <summary>
    /// Cria nova instância com estado atualizado
    /// </summary>
    public EffectInstance WithState(EffectExecutionState state)
    {
        return this with { State = state };
    }
    
    /// <summary>
    /// Cria nova instância com resultado definido
    /// </summary>
    public EffectInstance WithResult(EffectResult result)
    {
        return this with 
        { 
            Result = result,
            State = result.Success ? EffectExecutionState.COMPLETED : EffectExecutionState.FAILED,
            ExecutedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Cria nova instância marcada como executando
    /// </summary>
    public EffectInstance MarkAsExecuting()
    {
        return this with { State = EffectExecutionState.EXECUTING };
    }
    
    /// <summary>
    /// Cria nova instância marcada como cancelada
    /// </summary>
    public EffectInstance MarkAsCancelled()
    {
        return this with { State = EffectExecutionState.CANCELLED };
    }
}
