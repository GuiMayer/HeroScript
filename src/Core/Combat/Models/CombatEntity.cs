namespace Core.Combat.Models;

/// <summary>
/// Representa uma entidade em combate (herói ou inimigo).
/// Imutável - cada mudança cria nova instância.
/// </summary>
public record CombatEntity
{
    public string EntityId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsHero { get; init; }
    
    /// <summary>
    /// Estado de recursos da entidade (health, energy, mana, etc.)
    /// </summary>
    public Resources.ResourceSet ResourceState { get; init; } = null!;
    
    /// <summary>
    /// Verifica se nenhuma política de recurso declarou o dono derrotado.
    /// </summary>
    public bool IsAlive
    {
        get
        {
            return !Resources.ResourceThresholdEvaluator.IsOwnerDefeated(
                ResourceState.Resources.Values);
        }
    }
    
    /// <summary>
    /// Obtém um recurso específico.
    /// </summary>
    public Resources.ResourcePool? GetResource(string resourceId) 
        => ResourceState.Get(resourceId);
    
    /// <summary>
    /// Applies one source-agnostic resource mutation through the canonical
    /// atomic reducer.
    /// </summary>
    public Common.Result<CombatEntity> ApplyResourceMutation(
        string mutationId,
        string resourceId,
        Resources.ResourceMutationOperation operation,
        float value,
        Resources.ResourceValueField field = Resources.ResourceValueField.Current)
    {
        var applied = ResourceState.Apply(
        [
            new Resources.ResolvedResourceMutation
            {
                MutationId = mutationId,
                ResourceId = resourceId,
                Field = field,
                Operation = operation,
                Value = value
            }
        ]);
        return applied.IsFailure
            ? Common.Result<CombatEntity>.Failure(applied.Error)
            : Common.Result<CombatEntity>.Success(this with { ResourceState = applied.Value.State });
    }
    
}
