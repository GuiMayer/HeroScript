namespace Core.Resources;

/// <summary>
/// Pool imutável de um recurso.
/// Cada operação retorna uma nova instância.
/// </summary>
public record ResourcePool
{
    /// <summary>
    /// ID do recurso.
    /// </summary>
    public string ResourceId { get; init; } = string.Empty;
    
    /// <summary>
    /// Valor atual do recurso.
    /// </summary>
    public float Current { get; init; }
    
    /// <summary>
    /// Valor máximo do recurso.
    /// </summary>
    public float Maximum { get; init; }
    
    /// <summary>
    /// Valor mínimo do recurso.
    /// </summary>
    public float Minimum { get; init; }
    
    /// <summary>
    /// Definição do recurso (metadados).
    /// </summary>
    public ResourceDefinition Definition { get; init; } = null!;
    
    /// <summary>
    /// Verifica se há recurso suficiente para pagar um custo.
    /// </summary>
    public bool CanAfford(float cost) => Current >= cost;
    
    /// <summary>
    /// Gasta recurso.
    /// </summary>
    /// <param name="amount">Quantidade a gastar</param>
    /// <returns>Nova instância com recurso reduzido</returns>
    /// <exception cref="InvalidOperationException">Se não houver recurso suficiente</exception>
    public ResourcePool Spend(float amount)
    {
        if (amount > Current)
            throw new InvalidOperationException(
                $"Insufficient {Definition.DisplayName}: has {Current}, needs {amount}");
        return this with { Current = Current - amount };
    }
    
    /// <summary>
    /// Ganha recurso.
    /// </summary>
    /// <param name="amount">Quantidade a ganhar</param>
    /// <returns>Nova instância com recurso aumentado</returns>
    public ResourcePool Gain(float amount)
    {
        var newValue = Current + amount;
        if (!Definition.CanExceedMax)
            newValue = System.Math.Min(Maximum, newValue);
        return this with { Current = newValue };
    }
    
    /// <summary>
    /// Define valor do recurso.
    /// </summary>
    /// <param name="value">Novo valor</param>
    /// <returns>Nova instância com valor definido</returns>
    public ResourcePool Set(float value)
    {
        var clamped = value;
        if (!Definition.CanBeNegative)
            clamped = System.Math.Max(Minimum, clamped);
        if (!Definition.CanExceedMax)
            clamped = System.Math.Min(Maximum, clamped);
        return this with { Current = clamped };
    }
    
    /// <summary>
    /// Reseta recurso para o máximo.
    /// </summary>
    /// <returns>Nova instância com recurso no máximo</returns>
    public ResourcePool Reset()
    {
        return this with { Current = Maximum };
    }
    
    /// <summary>
    /// Obtém porcentagem atual do recurso (0-100).
    /// </summary>
    public float GetPercentage() => Maximum > 0 ? (Current / Maximum) * 100f : 0f;
}
