namespace Core.Damage;

/// <summary>
/// Implementação padrão de IRandomProvider usando Random.Shared
/// </summary>
public class DefaultRandomProvider : IRandomProvider
{
    public double NextDouble() => Random.Shared.NextDouble();
    
    public int Next(int maxValue) => Random.Shared.Next(maxValue);
    
    public int Next(int minValue, int maxValue) => Random.Shared.Next(minValue, maxValue);
}
