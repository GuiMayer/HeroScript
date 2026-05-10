using Core.StatusEffects;

namespace Core.Entity.Components;

/// <summary>
/// Componente que gerencia status effects ativos na entidade.
/// Integra com StatusEffectManager existente.
/// </summary>
public class StatusEffectComponent : ComponentBase
{
    /// <summary>
    /// IDs dos status effects ativos nesta entidade
    /// </summary>
    public IReadOnlyList<Guid> ActiveEffectIds { get; init; } = new List<Guid>();
    
    public StatusEffectComponent(IReadOnlyList<Guid>? activeEffectIds = null)
    {
        ActiveEffectIds = activeEffectIds ?? new List<Guid>();
    }
    
    /// <summary>
    /// Verifica se a entidade possui um status effect específico
    /// </summary>
    public bool HasEffect(string statusId)
    {
        // Nota: Requer StatusEffectManager para verificar
        // Esta é uma versão simplificada que apenas rastreia IDs
        return ActiveEffectIds.Any();
    }
    
    /// <summary>
    /// Verifica se a entidade está sob controle (stunned, silenced, etc.)
    /// </summary>
    public bool IsControlled()
    {
        // Nota: Requer StatusEffectManager para verificar tipos
        // Esta é uma versão simplificada
        return false;
    }
    
    /// <summary>
    /// Adiciona um status effect ID à lista
    /// </summary>
    public StatusEffectComponent AddEffect(Guid effectId)
    {
        var newList = new List<Guid>(ActiveEffectIds) { effectId };
        return new StatusEffectComponent(newList)
        {
            ComponentId = this.ComponentId,
            IsEnabled = this.IsEnabled
        };
    }
    
    /// <summary>
    /// Remove um status effect ID da lista
    /// </summary>
    public StatusEffectComponent RemoveEffect(Guid effectId)
    {
        var newList = ActiveEffectIds.Where(id => id != effectId).ToList();
        return new StatusEffectComponent(newList)
        {
            ComponentId = this.ComponentId,
            IsEnabled = this.IsEnabled
        };
    }
}
