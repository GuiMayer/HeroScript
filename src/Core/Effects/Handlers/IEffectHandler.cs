namespace Core.Effects.Handlers;

/// <summary>
/// Extensão coesa para uma família de efeitos. O dispatcher escolhe exatamente
/// um handler por tipo, mantendo o resolver independente das integrações.
/// </summary>
public interface IEffectHandler
{
    IReadOnlySet<EffectType> SupportedTypes { get; }
    EffectResult Execute(EffectExecutionRequest request);
}
