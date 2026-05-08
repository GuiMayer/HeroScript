namespace Core.Combat;

/// <summary>
/// Gerencia energia do herói.
/// Imutável - cada mudança cria nova instância.
/// </summary>
public record EnergyPool
{
    public int Current { get; init; }
    public int Maximum { get; init; }
    
    /// <summary>
    /// Verifica se há energia suficiente para pagar um custo.
    /// </summary>
    public bool CanAfford(int cost) => Current >= cost;
    
    /// <summary>
    /// Gasta energia.
    /// </summary>
    /// <param name="amount">Quantidade a gastar</param>
    /// <returns>Nova instância com energia reduzida</returns>
    /// <exception cref="InvalidOperationException">Se não houver energia suficiente</exception>
    public EnergyPool Spend(int amount)
    {
        if (amount > Current)
            throw new InvalidOperationException($"Insufficient energy: has {Current}, needs {amount}");
        return this with { Current = Current - amount };
    }
    
    /// <summary>
    /// Ganha energia.
    /// </summary>
    /// <param name="amount">Quantidade a ganhar</param>
    /// <returns>Nova instância com energia aumentada (limitada ao máximo)</returns>
    public EnergyPool Gain(int amount)
    {
        return this with { Current = System.Math.Min(Maximum, Current + amount) };
    }
    
    /// <summary>
    /// Reseta energia para o máximo.
    /// </summary>
    /// <returns>Nova instância com energia no máximo</returns>
    public EnergyPool Reset()
    {
        return this with { Current = Maximum };
    }
}
