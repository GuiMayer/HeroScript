using Core.Combat.Models;
using Core.Common;
using Core.Damage;

namespace Core.Effects;

/// <summary>
/// Interface para resolução de efeitos.
/// Responsável por executar effects, aplicar modificadores, e validar condições.
/// </summary>
public interface IEffectResolver
{
    // ===== EXECUÇÃO =====
    
    /// <summary>
    /// Resolve (executa) um efeito único
    /// </summary>
    /// <param name="effect">Instância do efeito a executar</param>
    /// <param name="state">Estado atual do combate</param>
    /// <returns>Resultado da execução</returns>
    Result<EffectResult> ResolveEffect(EffectInstance effect, CombatState state);
    
    /// <summary>
    /// Aplica um efeito em um contexto generico de jogo/run.
    /// </summary>
    Result<EffectApplicationResult> ApplyEffect(EffectInstance effect, IEffectContext context);

    /// <summary>
    /// Aplica um efeito usando o cursor aleatório pertencente à transição atual.
    /// </summary>
    Result<EffectApplicationResult> ApplyEffect(
        EffectInstance effect,
        IEffectContext context,
        IRandomProvider randomProvider);
    
    /// <summary>
    /// Resolve múltiplos efeitos em sequência
    /// </summary>
    /// <param name="effects">Lista de efeitos a executar</param>
    /// <param name="state">Estado atual do combate</param>
    /// <returns>Lista de resultados</returns>
    Result<List<EffectResult>> ResolveEffects(List<EffectInstance> effects, CombatState state);
    
    /// <summary>
    /// Aplica múltiplos efeitos em sequência em um contexto generico de jogo/run.
    /// </summary>
    Result<List<EffectApplicationResult>> ApplyEffects(List<EffectInstance> effects, IEffectContext context);
    
    // ===== MODIFICAÇÃO =====
    
    /// <summary>
    /// Aplica modificadores a uma definição de efeito
    /// </summary>
    /// <param name="definition">Definição original</param>
    /// <param name="modifiers">Modificadores a aplicar</param>
    /// <returns>Definição modificada</returns>
    EffectDefinition ApplyModifiers(EffectDefinition definition, List<EffectModifier> modifiers);
    
    /// <summary>
    /// Cria instância de efeito com modificadores aplicados
    /// </summary>
    /// <param name="definition">Definição do efeito</param>
    /// <param name="sourceEntityId">ID da entidade origem</param>
    /// <param name="targetEntityId">ID da entidade alvo</param>
    /// <param name="modifiers">Modificadores a aplicar</param>
    /// <returns>Instância do efeito</returns>
    EffectInstance CreateEffectInstance(
        EffectDefinition definition, 
        string sourceEntityId, 
        string targetEntityId,
        List<EffectModifier>? modifiers = null);
    
    // ===== VALIDAÇÃO =====
    
    /// <summary>
    /// Verifica se um efeito pode ser executado
    /// </summary>
    /// <param name="effect">Instância do efeito</param>
    /// <param name="state">Estado atual do combate</param>
    /// <returns>True se pode executar, false caso contrário</returns>
    Result<bool> CanExecuteEffect(EffectInstance effect, CombatState state);
    
    /// <summary>
    /// Verifica se um efeito pode ser executado no contexto informado.
    /// </summary>
    Result<bool> CanExecuteEffect(EffectInstance effect, IEffectContext context);
    
    /// <summary>
    /// Valida uma definição de efeito
    /// </summary>
    /// <param name="definition">Definição a validar</param>
    /// <returns>True se válida, false caso contrário</returns>
    Result<bool> ValidateDefinition(EffectDefinition definition);
    
    // ===== QUERY =====
    
    /// <summary>
    /// Obtém modificadores ativos para uma entidade
    /// </summary>
    /// <param name="entityId">ID da entidade</param>
    /// <param name="filterType">Filtrar por tipo de efeito (opcional)</param>
    /// <returns>Lista de modificadores ativos</returns>
    List<EffectModifier> GetActiveModifiers(string entityId, EffectType? filterType = null);
}
