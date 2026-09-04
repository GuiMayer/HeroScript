namespace Core.Resources;

/// <summary>
/// Pool imutável de um recurso.
/// Cada operação retorna uma nova instância.
/// </summary>
public record ResourcePool
{
    /// <summary>
    /// Materializes a valid immutable pool from one pinned definition and
    /// optional owner-specific starting bounds.
    /// </summary>
    public static ResourcePool Materialize(
        ResourceDefinition definition,
        float? initialCurrent = null,
        float? maximum = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var validation = ResourceDefinitionValidator.Validate(definition);
        if (validation.IsFailure)
            throw new InvalidOperationException(validation.Error);

        var resolvedMaximum = maximum ?? definition.DefaultMax;
        if (!IsFinite(resolvedMaximum))
            throw new ArgumentOutOfRangeException(nameof(maximum), "Resource maximum must be finite");
        if (resolvedMaximum < definition.DefaultMin)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                "Resource maximum cannot be below its configured minimum");
        }

        var pool = new ResourcePool
        {
            ResourceId = definition.ResourceId,
            Current = initialCurrent ?? definition.DefaultCurrent,
            Maximum = resolvedMaximum,
            Minimum = definition.DefaultMin,
            Definition = definition
        };
        return pool.Set(pool.Current);
    }

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
    public bool CanAfford(float cost)
    {
        if (!IsFinite(cost) || cost < 0)
            return false;

        return Current - cost >= Minimum;
    }
    
    /// <summary>
    /// Gasta recurso.
    /// </summary>
    /// <param name="amount">Quantidade a gastar</param>
    /// <returns>Nova instância com recurso reduzido</returns>
    /// <exception cref="InvalidOperationException">Se não houver recurso suficiente</exception>
    public ResourcePool Spend(float amount)
    {
        ValidateNonNegativeFinite(amount, nameof(amount));
        if (!CanAfford(amount))
            throw new InvalidOperationException(
                $"Insufficient {Definition.DisplayName}: has {Current}, needs {amount}");
        return Set(Current - amount);
    }
    
    /// <summary>
    /// Ganha recurso.
    /// </summary>
    /// <param name="amount">Quantidade a ganhar</param>
    /// <returns>Nova instância com recurso aumentado</returns>
    public ResourcePool Gain(float amount)
    {
        ValidateNonNegativeFinite(amount, nameof(amount));
        return Set(Current + amount);
    }
    
    /// <summary>
    /// Define valor do recurso.
    /// </summary>
    /// <param name="value">Novo valor</param>
    /// <returns>Nova instância com valor definido</returns>
    public ResourcePool Set(float value)
    {
        if (!IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Resource value must be finite");

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
    public float GetPercentage()
    {
        var range = Maximum - Minimum;
        return range > 0 ? ((Current - Minimum) / range) * 100f : 0f;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void ValidateNonNegativeFinite(float value, string parameterName)
    {
        if (!IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameterName, "Resource amount must be finite and non-negative");
    }
}
