namespace Core.Damage;

/// <summary>
/// Provider de compatibilidade que usa a fonte aleatória global do processo.
/// </summary>
public class DefaultRandomProvider : IRandomProvider
{
    public double NextDouble() => Random.Shared.NextDouble(); // nondeterministic-boundary: compatibility provider
    
    public int Next(int maxValue) => Random.Shared.Next(maxValue); // nondeterministic-boundary: compatibility provider
    
    public int Next(int minValue, int maxValue) => Random.Shared.Next(minValue, maxValue); // nondeterministic-boundary: compatibility provider
}
