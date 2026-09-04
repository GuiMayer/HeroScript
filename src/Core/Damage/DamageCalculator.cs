using System;
using System.Collections.Generic;
using System.Linq;
using Core.Combat.Models;
using Core.Effects;
using Core.Events;
using Core.Logging;
using Core.Resources;
using Core.StatusEffects;

namespace Core.Damage;

/// <summary>
/// Calculadora de dano que usa o pipeline JSON-driven.
/// Facade que constrói o contexto inicial e executa o pipeline.
/// </summary>
public class DamageCalculator : IDamageCalculator, IRevisionedDamageCalculator
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
        => CalculateDamageCore(action, attacker, target, null);

    public DamageResult CalculateDamage(
        ActionDefinition action,
        CombatEntity attacker,
        CombatEntity target,
        IRandomProvider randomProvider)
    {
        ArgumentNullException.ThrowIfNull(randomProvider);
        return CalculateDamageCore(action, attacker, target, randomProvider, null, null);
    }

    public DamageResult CalculateDamage(
        ActionDefinition action,
        CombatEntity attacker,
        CombatEntity target,
        IRandomProvider randomProvider,
        string contentRevision,
        string? pipelineId = null)
    {
        ArgumentNullException.ThrowIfNull(randomProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRevision);
        return CalculateDamageCore(
            action,
            attacker,
            target,
            randomProvider,
            contentRevision,
            pipelineId);
    }

    private DamageResult CalculateDamageCore(
        ActionDefinition action,
        CombatEntity attacker,
        CombatEntity target,
        IRandomProvider? randomProvider,
        string? contentRevision = null,
        string? pipelineId = null)
    {
        // 1. Construir contexto inicial
        var context = BuildInitialContext(action, attacker, target);
        
        _logger.LogDebug($"Calculating damage: {action.ActionId} from {attacker.EntityId} to {target.EntityId}");
        
        // 2. Executar pipeline
        var result = randomProvider == null
            ? _pipelineManager.ExecutePipeline(context)
            : !string.IsNullOrWhiteSpace(contentRevision) && _pipelineManager is IRevisionedPipelineManager revisioned
                ? revisioned.ExecutePipeline(context, randomProvider, contentRevision, pipelineId)
                : _pipelineManager.ExecutePipeline(context, randomProvider);
        
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
        
        var modifiers = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["base_damage"] = baseDamage,
            ["added_damage"] = 0f,
            ["increased_damage_total"] = 0f
        };
        ResourceFormulaVariables.AddOwner(modifiers, "source", attacker.ResourceState);
        ResourceFormulaVariables.AddOwner(modifiers, "target", target.ResourceState);

        var context = new DamageContext
        {
            BaseDamage = baseDamage,
            CurrentDamage = baseDamage,
            Tags = new HashSet<string>(action.Tags ?? new List<string>()),
            MoreMultipliers = new List<float>(),
            Modifiers = modifiers,
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
            context = ApplyStatusModifiers(context, attacker.EntityId, target.EntityId);
        }
        
        _logger.LogDebug(
            $"Initial context: base={baseDamage:F2}, sourceResources={attacker.ResourceState.Resources.Count}, targetResources={target.ResourceState.Resources.Count}");
        
        return context;
    }
    
    /// <summary>
    /// Aplica modificadores de status effects ao contexto de dano
    /// </summary>
    private DamageContext ApplyStatusModifiers(DamageContext context, string attackerId, string targetId)
    {
        if (_statusEffectManager == null)
            return context;
        
        // Obter modificadores do atacante (ex: Strength aumenta dano)
        var attackerModifiers = _statusEffectManager.GetPipelineModifiers(attackerId);
        if (attackerModifiers != null && attackerModifiers.Count > 0)
        {
            foreach (var modifier in attackerModifiers)
            {
                var currentValue = context.Modifiers.GetValueOrDefault(modifier.Key, 0f);
                context = context.WithModifier(modifier.Key, currentValue + modifier.Value);
                
                _logger.LogDebug($"Applied attacker modifier: {modifier.Key} = {modifier.Value}");
            }
        }
        
        // Obter modificadores do alvo (ex: Vulnerable aumenta dano recebido)
        var targetModifiers = _statusEffectManager.GetPipelineModifiers(targetId);
        if (targetModifiers != null && targetModifiers.Count > 0)
        {
            foreach (var modifier in targetModifiers)
            {
                var currentValue = context.Modifiers.GetValueOrDefault(modifier.Key, 0f);
                context = context.WithModifier(modifier.Key, currentValue + modifier.Value);
                
                _logger.LogDebug($"Applied target modifier: {modifier.Key} = {modifier.Value}");
            }
        }

        return context;
    }
}
