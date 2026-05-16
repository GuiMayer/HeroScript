using Core.Common;
using Core.Math;
using System.Globalization;

namespace Core.StatusEffects;

/// <summary>
/// Processa status effects individuais baseado em seus behaviors.
/// Responsável por executar a lógica de cada tipo de status effect.
/// </summary>
public class StatusEffectProcessor
{
    private readonly IStatusEffectManager _manager;
    private readonly IMathEngine _mathEngine;
    
    public StatusEffectProcessor(IStatusEffectManager manager, IMathEngine mathEngine)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _mathEngine = mathEngine ?? throw new ArgumentNullException(nameof(mathEngine));
    }
    
    /// <summary>
    /// Processa um tick de um status effect
    /// </summary>
    public Result<StatusEffectTickResult> ProcessTick(
        StatusEffectInstance instance,
        Guid targetId,
        int currentTurn)
    {
        return instance.Definition.Behavior switch
        {
            StatusEffectBehavior.DAMAGE_OVER_TIME => ProcessDoT(instance, targetId),
            StatusEffectBehavior.HEAL_OVER_TIME => ProcessHoT(instance, targetId),
            StatusEffectBehavior.STAT_MODIFIER => ProcessStatModifier(instance, targetId),
            StatusEffectBehavior.SHIELD => ProcessShield(instance, targetId),
            StatusEffectBehavior.REACTIVE => ProcessReactive(instance, targetId),
            StatusEffectBehavior.CONTROL => ProcessControl(instance, targetId),
            
            // Behaviors especiais
            StatusEffectBehavior.PREVENT_NEXT_DEBUFF => ProcessPreventDebuff(instance, targetId),
            StatusEffectBehavior.DAMAGE_CAP => ProcessDamageCap(instance, targetId),
            StatusEffectBehavior.DEATH_PREVENTION => ProcessDeathPrevention(instance, targetId),
            StatusEffectBehavior.RULE_MODIFIER => ProcessRuleModifier(instance, targetId),
            StatusEffectBehavior.TRIGGER_ON_STATUS => ProcessTriggerOnStatus(instance, targetId),
            
            _ => Result<StatusEffectTickResult>.Failure($"Unknown behavior: {instance.Definition.Behavior}")
        };
    }
    
    // ===== DAMAGE OVER TIME =====
    
    private Result<StatusEffectTickResult> ProcessDoT(StatusEffectInstance instance, Guid targetId)
    {
        var damage = CalculateValue(instance);
        
        // TODO: Aplicar dano ao alvo via ResourceManager
        // Por enquanto, apenas retornar o resultado
        
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = damage,
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} dealt {damage} damage"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    // ===== HEAL OVER TIME =====
    
    private Result<StatusEffectTickResult> ProcessHoT(StatusEffectInstance instance, Guid targetId)
    {
        var healing = CalculateValue(instance);
        
        // TODO: Aplicar cura ao alvo via ResourceManager
        // Por enquanto, apenas retornar o resultado
        
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = healing,
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} healed {healing} HP"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    // ===== STAT MODIFIER =====
    
    private Result<StatusEffectTickResult> ProcessStatModifier(StatusEffectInstance instance, Guid targetId)
    {
        // Stat modifiers são processados passivamente via GetPipelineModifiers
        // Não precisam de tick ativo
        
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = 0,
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} is active"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    // ===== SHIELD =====
    
    private Result<StatusEffectTickResult> ProcessShield(StatusEffectInstance instance, Guid targetId)
    {
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = CalculateValue(instance),
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} is protecting"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    // ===== REACTIVE =====
    
    private Result<StatusEffectTickResult> ProcessReactive(StatusEffectInstance instance, Guid targetId)
    {
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = CalculateValue(instance),
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} is ready"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    // ===== CONTROL =====
    
    private Result<StatusEffectTickResult> ProcessControl(StatusEffectInstance instance, Guid targetId)
    {
        // Control effects são verificados antes de ações
        // Não precisam de tick ativo
        
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = 0,
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} is preventing actions"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    // ===== BEHAVIORS ESPECIAIS =====
    
    private Result<StatusEffectTickResult> ProcessPreventDebuff(StatusEffectInstance instance, Guid targetId)
    {
        // Artifact: Previne o próximo debuff
        // Processado quando debuff é aplicado
        
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = 0,
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} is ready to block debuffs"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    private Result<StatusEffectTickResult> ProcessDamageCap(StatusEffectInstance instance, Guid targetId)
    {
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = CalculateValue(instance),
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} is capping damage"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    private Result<StatusEffectTickResult> ProcessDeathPrevention(StatusEffectInstance instance, Guid targetId)
    {
        // Buffer: Previne morte
        // Processado quando HP chegaria a 0
        
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = 0,
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} is ready to prevent death"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    private Result<StatusEffectTickResult> ProcessRuleModifier(StatusEffectInstance instance, Guid targetId)
    {
        // Barricade: Modifica regras do jogo
        // Processado passivamente
        
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = 0,
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} is modifying game rules"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    private Result<StatusEffectTickResult> ProcessTriggerOnStatus(StatusEffectInstance instance, Guid targetId)
    {
        // Evolve: Dispara ao receber status
        // Processado quando outro status é aplicado
        
        var result = new StatusEffectTickResult
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.StatusId,
            Type = instance.Definition.Type,
            Behavior = instance.Definition.Behavior,
            Value = 0,
            WasBlocked = false,
            Message = $"{instance.Definition.DisplayName} is ready to trigger"
        };
        
        return Result<StatusEffectTickResult>.Success(result);
    }
    
    // ===== HELPERS =====
    
    private float CalculateValue(StatusEffectInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.Definition.FormulaValue))
        {
            var variables = new Dictionary<string, float>
            {
                ["stacks"] = instance.Stacks,
                ["duration"] = instance.Duration
            };

            if (TryEvaluateFormula(instance.Definition.FormulaValue, variables, out var formulaValue))
                return formulaValue;
        }
        
        return instance.Definition.BaseValue * (instance.Definition.ScalesWithStacks ? instance.Stacks : 1);
    }

    private static bool TryEvaluateFormula(string formula, Dictionary<string, float> variables, out float result)
    {
        result = 0;
        var tokens = formula.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Length % 2 == 0)
            return false;

        if (!TryReadValue(tokens[0], variables, out result))
            return false;

        for (var i = 1; i < tokens.Length; i += 2)
        {
            if (!TryReadValue(tokens[i + 1], variables, out var right))
                return false;

            result = tokens[i] switch
            {
                "+" => result + right,
                "-" => result - right,
                "*" => result * right,
                "/" when right != 0 => result / right,
                _ => float.NaN
            };

            if (float.IsNaN(result))
                return false;
        }

        return true;
    }

    private static bool TryReadValue(string token, Dictionary<string, float> variables, out float value)
    {
        if (variables.TryGetValue(token, out value))
            return true;

        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
