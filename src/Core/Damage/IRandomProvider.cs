namespace Core.Damage;

/// <summary>
/// Interface para geração de números aleatórios.
/// Permite injeção de implementações determinísticas para testes.
/// </summary>
public interface IRandomProvider
{
    /// <summary>
    /// Retorna um número aleatório entre 0.0 e 1.0
    /// </summary>
    double NextDouble();
    
    /// <summary>
    /// Retorna um número inteiro aleatório entre 0 (inclusive) e maxValue (exclusivo)
    /// </summary>
    int Next(int maxValue);
    
    /// <summary>
    /// Retorna um número inteiro aleatório entre minValue (inclusive) e maxValue (exclusivo)
    /// </summary>
    int Next(int minValue, int maxValue);
}
