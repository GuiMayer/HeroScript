using System;
using System.Collections.Generic;
using System.Linq;
using Core.Combat;
using Core.Effects;
using Core.Events;
using Core.Logging;
using Core.StatusEffects;

namespace Core.Damage;

/// <summary>
/// Calculadora de dano que usa o pipeline JSON-driven.
/// Facade que constrói o contexto inicial e executa o pipeline.
/// </summary>
public class DamageCalculator : IDamageCalculator
{
    private readonly IPipelineManager _pipelineManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger _logger;
    private readonly IStatusEffectManager? _statusEffectManager;

    public DamageCalculator(
        IPipelineManager pipelineManager,
        IEventBus eventBus,
        ILogger logger,
        IStatusEffectManager? statusEffectManager = null)
    {
        _pipelineManager = pipelineManager ?? throw new ArgumentNullException(nameof(pipelineManager));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _statusEffectManager = statusEffectManager;
    }

    /// <summary>
    /// Calcula dano de uma ação considerando attacker e target
    /// </summary>
    public DamageResult CalculateDamage(
        ActionDefinition action,
        CombatEntity attacker,
        CombatEntity target)
    {
        // 1. Construir contexto inicial
        var context = BuildInitialContext(action, attacker, target);
        
        _logger.LogDebug($"Calculating damage: {action.ActionId} from {attacker.EntityId} to {target.EntityId}");
        
        // 2. Executar pipeline
        var result = _pipelineManager.ExecutePipeline(context);
        
        // 3. Garantir dano não-negativo
        var finalDamage = System.Math.Max(0, result.CurrentDamage);
        
        // 4. Extrair tier de crítico (se houver)
        var critTier = result.Metadata.TryGetValue("crit_tier", out var tier) ? Convert.ToInt32(tier) : 0;
        
        // 5. Emitir evento de dano calculado
        _eventBus.Publish(new Events.DamageCalculatedEvent
        {
            ActionId = action.ActionId,
            AttackerId = attacker.EntityId,
            TargetId = target.EntityId,
            BaseDamage = context.BaseDamage,
            FinalDamage = finalDamage,
            CritTier = critTier,
            Tags = context.Tags,
            Metadata = result.Metadata
        });
        
        _logger.LogDebug($"Damage calculated: {finalDamage:F2} (crit tier: {critTier})");
        
        return new DamageResult
        {
            FinalDamage = finalDamage,
            CritTier = critTier,
            Metadata = result.Metadata
        };
    }

    /// <summary>
    /// Constrói contexto inicial de dano a partir da ação e entidades
    /// </summary>
    private DamageContext BuildInitialContext(
        ActionDefinition action,
        CombatEntity attacker,
        CombatEntity target)
    {
        // Obter dano base da ação através dos efeitos de dano
        var baseDamage = action.Effects
            .Where(e => e.Type == EffectType.DAMAGE)
            .Sum(e => e.FlatValue ?? 0f);
        
        var context = new DamageContext
        {
            BaseDamage = baseDamage,
            CurrentDamage = baseDamage,
            Tags = new HashSet<string>(action.Tags ?? new List<string>()),
            MoreMultipliers = new List<float>(),
            Modifiers = new Dictionary<string, float>
            {
                // Base
                ["base_damage"] = baseDamage,
                ["added_damage"] = 0f,
                
                // Increased (soma de todos "increased")
                ["increased_damage_total"] = 0f,
                
                // Critical
                ["crit_chance"] = attacker.GetCritChance(),
                ["crit_multiplier"] = attacker.GetCritMultiplier(),
                
                // Mitigation
                ["target_armor"] = target.GetArmor()
            },
            Metadata = new Dictionary<string, object>
            {
                ["attacker_id"] = attacker.EntityId,
                ["target_id"] = target.EntityId,
                ["action_id"] = action.ActionId
            }
        };
        
        // Aplicar modifiers do attacker (buffs, debuffs, status effects)
        if (_statusEffectManager != null)
        {
            ApplyStatusModifiers(context, attacker.EntityId, target.EntityId);
        }
        
        _logger.LogDebug($"Initial context: base={baseDamage:F2}, crit_chance={attacker.GetCritChance():F1}%, armor={target.GetArmor():F1}");
        
        return context;
    }
    
    /// <summary>
    /// Aplica modificadores de status effects ao contexto de dano
    /// </summary>
    private void ApplyStatusModifiers(DamageContext context, string attackerId, string targetId)
    {
        if (_statusEffectManager == null)
            return;
        
        // Converter IDs para Guid
        if (!Guid.TryParse(attackerId, out var attackerGuid) || !Guid.TryParse(targetId, out var targetGuid))
        {
            _logger.LogWarning("Invalid entity IDs for status modifier application");
            return;
        }
        
        // Obter modificadores do atacante (ex: Strength aumenta dano)
        var attackerModifiers = _statusEffectManager.GetPipelineModifiers(attackerGuid);
        if (attackerModifiers != null && attackerModifiers.Count > 0)
        {
            foreach (var modifier in attackerModifiers)
            {
                if (context.Modifiers.ContainsKey(modifier.Key))
                {
                    context.Modifiers[modifier.Key] += modifier.Value;
                }
                else
                {
                    context.Modifiers[modifier.Key] = modifier.Value;
                }
                
                _logger.LogDebug($"Applied attacker modifier: {modifier.Key} = {modifier.Value}");
            }
        }
        
        // Obter modificadores do alvo (ex: Vulnerable aumenta dano recebido)
        var targetModifiers = _statusEffectManager.GetPipelineModifiers(targetGuid);
        if (targetModifiers != null && targetModifiers.Count > 0)
        {
            foreach (var modifier in targetModifiers)
            {
                if (context.Modifiers.ContainsKey(modifier.Key))
                {
                    context.Modifiers[modifier.Key] += modifier.Value;
                }
                else
                {
                    context.Modifiers[modifier.Key] = modifier.Value;
                }
                
                _logger.LogDebug($"Applied target modifier: {modifier.Key} = {modifier.Value}");
            }
        }
    }
}
