using System.Collections.Concurrent;
using Core.Common;
using Core.Config;
using Core.Math;
using Core.Resources;

namespace Core.StatusEffects;

/// <summary>
/// Gerenciador de status effects.
/// Thread-safe, suporta múltiplas entidades simultaneamente.
/// </summary>
public class StatusEffectManager : IStatusEffectManager
{
    private readonly IConfigManager _configManager;
    private readonly IResourceManager _resourceManager;
    private readonly IMathEngine _mathEngine;
    
    // Status effects ativos por entidade (thread-safe)
    private readonly ConcurrentDictionary<Guid, List<StatusEffectInstance>> _activeStatus = new();
    
    // Definições de status effects carregadas (thread-safe)
    private readonly ConcurrentDictionary<string, StatusEffectDefinition> _definitions = new();
    
    // Processor para executar behaviors
    private readonly StatusEffectProcessor _processor;
    
    public StatusEffectManager(
        IConfigManager configManager,
        IResourceManager resourceManager,
        IMathEngine mathEngine)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _mathEngine = mathEngine ?? throw new ArgumentNullException(nameof(mathEngine));
        
        _processor = new StatusEffectProcessor(this, mathEngine);
    }
    
    // ===== APLICAR/REMOVER =====
    
    public Result<StatusEffectInstance> ApplyStatus(
        Guid targetId,
        string statusId,
        int stacks = 1,
        int? duration = null,
        Guid? sourceId = null)
    {
        if (string.IsNullOrWhiteSpace(statusId))
            return Result<StatusEffectInstance>.Failure("StatusId cannot be empty");
        
        if (stacks <= 0)
            return Result<StatusEffectInstance>.Failure("Stacks must be greater than 0");
        
        // Obter definição
        var defResult = GetDefinition(statusId);
        if (!defResult.IsSuccess)
            return Result<StatusEffectInstance>.Failure(defResult.Error!);
        
        var definition = defResult.Value!;
        
        // Verificar se já existe status do mesmo tipo
        var existingStatus = GetActiveStatusList(targetId)
            .FirstOrDefault(s => s.StatusId == statusId && s.IsActive);
        
        if (existingStatus != null)
        {
            // Adicionar stacks ao existente
            var newStacks = System.Math.Min(existingStatus.Stacks + stacks, definition.MaxStacks);
            var updated = existingStatus with { Stacks = newStacks };
            
            UpdateStatusInstance(targetId, updated);
            return Result<StatusEffectInstance>.Success(updated);
        }
        
        // Criar nova instância
        var instance = new StatusEffectInstance
        {
            StatusId = statusId,
            Definition = definition,
            TargetId = targetId,
            SourceId = sourceId,
            Stacks = System.Math.Min(stacks, definition.MaxStacks),
            Duration = duration ?? definition.DefaultDuration,
            TurnApplied = 0, // Será atualizado pelo CombatSystem
            IsActive = true
        };
        
        // Adicionar à lista
        var statusList = _activeStatus.GetOrAdd(targetId, _ => new List<StatusEffectInstance>());
        lock (statusList)
        {
            statusList.Add(instance);
        }
        
        return Result<StatusEffectInstance>.Success(instance);
    }
    
    public Result RemoveStatus(Guid targetId, Guid instanceId)
    {
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            var index = statusList.FindIndex(s => s.InstanceId == instanceId);
            if (index == -1)
                return Result.Failure($"Status instance {instanceId} not found");
            
            statusList.RemoveAt(index);
        }
        
        return Result.Success();
    }
    
    public Result RemoveStatusByStatusId(Guid targetId, string statusId)
    {
        if (string.IsNullOrEmpty(statusId))
            return Result.Failure("StatusId cannot be null or empty");
        
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            var removed = statusList.RemoveAll(s => s.Definition.StatusId == statusId);
            if (removed == 0)
                return Result.Failure($"No status with StatusId '{statusId}' found on target {targetId}");
        }
        
        return Result.Success();
    }
    
    public Result RemoveAllStatus(Guid targetId, StatusEffectType? type = null)
    {
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            if (type.HasValue)
            {
                statusList.RemoveAll(s => s.Definition.Type == type.Value);
            }
            else
            {
                statusList.Clear();
            }
        }
        
        return Result.Success();
    }
    
    // ===== MODIFICAR =====
    
    public Result<StatusEffectInstance> AddStacks(Guid targetId, Guid instanceId, int stacks)
    {
        if (stacks <= 0)
            return Result<StatusEffectInstance>.Failure("Stacks must be greater than 0");
        
        var statusResult = GetStatus(targetId, instanceId);
        if (!statusResult.IsSuccess)
            return statusResult;
        
        var status = statusResult.Value!;
        var newStacks = System.Math.Min(status.Stacks + stacks, status.Definition.MaxStacks);
        var updated = status with { Stacks = newStacks };
        
        UpdateStatusInstance(targetId, updated);
        return Result<StatusEffectInstance>.Success(updated);
    }
    
    public Result<StatusEffectInstance?> RemoveStacks(Guid targetId, Guid instanceId, int stacks)
    {
        if (stacks <= 0)
            return Result<StatusEffectInstance?>.Failure("Stacks must be greater than 0");
        
        var statusResult = GetStatus(targetId, instanceId);
        if (!statusResult.IsSuccess)
            return Result<StatusEffectInstance?>.Failure(statusResult.Error!);
        
        var status = statusResult.Value!;
        var newStacks = status.Stacks - stacks;
        
        if (newStacks <= 0)
        {
            // Remove o status
            RemoveStatus(targetId, instanceId);
            return Result<StatusEffectInstance?>.Success(null);
        }
        
        var updated = status with { Stacks = newStacks };
        UpdateStatusInstance(targetId, updated);
        return Result<StatusEffectInstance?>.Success(updated);
    }
    
    public Result<StatusEffectInstance> RefreshDuration(Guid targetId, Guid instanceId, int duration)
    {
        var statusResult = GetStatus(targetId, instanceId);
        if (!statusResult.IsSuccess)
            return statusResult;
        
        var status = statusResult.Value!;
        var updated = status with { Duration = duration };
        
        UpdateStatusInstance(targetId, updated);
        return Result<StatusEffectInstance>.Success(updated);
    }
    
    // ===== CONSULTAR =====
    
    public Result<List<StatusEffectInstance>> GetActiveStatus(Guid targetId)
    {
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            return Result<List<StatusEffectInstance>>.Success(
                statusList.Where(s => s.IsActive).ToList());
        }
    }
    
    public Result<StatusEffectInstance> GetStatus(Guid targetId, Guid instanceId)
    {
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            var status = statusList.FirstOrDefault(s => s.InstanceId == instanceId);
            if (status == null)
                return Result<StatusEffectInstance>.Failure($"Status instance {instanceId} not found");
            
            return Result<StatusEffectInstance>.Success(status);
        }
    }
    
    public bool HasStatus(Guid targetId, StatusEffectType type)
    {
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            return statusList.Any(s => s.IsActive && s.Definition.Type == type);
        }
    }
    
    public int GetStatusStacks(Guid targetId, StatusEffectType type)
    {
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            return statusList
                .Where(s => s.IsActive && s.Definition.Type == type)
                .Sum(s => s.Stacks);
        }
    }
    
    // ===== PROCESSAR =====
    
    public Result<StatusEffectProcessResult> ProcessStatusEffects(
        Guid targetId,
        StatusEffectTiming timing,
        int currentTurn)
    {
        var statusList = GetActiveStatusList(targetId);
        var tickResults = new List<StatusEffectTickResult>();
        var expiredStatus = new List<StatusEffectInstance>();
        
        lock (statusList)
        {
            var statusToProcess = statusList
                .Where(s => s.IsActive && s.Definition.Timing == timing)
                .ToList();
            
            foreach (var status in statusToProcess)
            {
                var result = _processor.ProcessTick(status, targetId, currentTurn);
                if (result.IsSuccess && result.Value != null)
                {
                    tickResults.Add(result.Value);
                }
            }
        }
        
        var processResult = new StatusEffectProcessResult
        {
            TargetId = targetId,
            TickResults = tickResults,
            ExpiredStatus = expiredStatus
        };
        
        return Result<StatusEffectProcessResult>.Success(processResult);
    }
    
    public Result TickDurations(Guid targetId)
    {
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            var toRemove = new List<StatusEffectInstance>();
            
            for (int i = 0; i < statusList.Count; i++)
            {
                var status = statusList[i];
                
                // Pular status permanentes
                if (status.Duration == -1)
                    continue;
                
                var newDuration = status.Duration - 1;
                
                if (newDuration <= 0)
                {
                    // Marcar para remoção
                    toRemove.Add(status);
                }
                else
                {
                    // Atualizar duração
                    statusList[i] = status with { Duration = newDuration };
                }
            }
            
            // Remover status expirados
            foreach (var expired in toRemove)
            {
                statusList.Remove(expired);
            }
        }
        
        return Result.Success();
    }
    
    public Dictionary<string, float> GetPipelineModifiers(Guid targetId)
    {
        var modifiers = new Dictionary<string, float>();
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            foreach (var status in statusList.Where(s => s.IsActive))
            {
                if (string.IsNullOrWhiteSpace(status.Definition.ModifierKey))
                    continue;
                
                var value = CalculateModifierValue(status);
                
                // Acumular modificadores com mesma chave
                if (modifiers.ContainsKey(status.Definition.ModifierKey))
                {
                    modifiers[status.Definition.ModifierKey] += value;
                }
                else
                {
                    modifiers[status.Definition.ModifierKey] = value;
                }
            }
        }
        
        return modifiers;
    }
    
    // ===== DEFINIÇÕES =====
    
    public Result LoadStatusDefinitions(string configName)
    {
        try
        {
            var configPath = _configManager.GetConfigPath(configName);
            var statusPath = Path.Combine(configPath, "StatusEffects", "status_effects.json");
            
            if (!File.Exists(statusPath))
                return Result.Failure($"Status effects file not found: {statusPath}");
            
            var json = File.ReadAllText(statusPath);
            var definitions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, StatusEffectDefinition>>(json);
            
            if (definitions == null)
                return Result.Failure("Failed to deserialize status effects");
            
            _definitions.Clear();
            foreach (var kvp in definitions)
            {
                _definitions[kvp.Key] = kvp.Value;
            }
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to load status definitions: {ex.Message}");
        }
    }
    
    public Result<StatusEffectDefinition> GetDefinition(string statusId)
    {
        if (_definitions.TryGetValue(statusId, out var definition))
            return Result<StatusEffectDefinition>.Success(definition);
        
        return Result<StatusEffectDefinition>.Failure($"Status effect definition not found: {statusId}");
    }
    
    public List<StatusEffectDefinition> GetAllDefinitions()
    {
        return _definitions.Values.ToList();
    }
    
    // ===== HELPERS =====
    
    private List<StatusEffectInstance> GetActiveStatusList(Guid targetId)
    {
        return _activeStatus.GetOrAdd(targetId, _ => new List<StatusEffectInstance>());
    }
    
    private void UpdateStatusInstance(Guid targetId, StatusEffectInstance updated)
    {
        var statusList = GetActiveStatusList(targetId);
        
        lock (statusList)
        {
            var index = statusList.FindIndex(s => s.InstanceId == updated.InstanceId);
            if (index != -1)
            {
                statusList[index] = updated;
            }
        }
    }
    
    private float CalculateModifierValue(StatusEffectInstance status)
    {
        // Se tem fórmula, avaliar manualmente (por enquanto)
        // TODO: Integrar com MathEngine quando suportar fórmulas string
        if (!string.IsNullOrWhiteSpace(status.Definition.ModifierFormula))
        {
            try
            {
                // Parse simples para fórmulas básicas como "stacks * 0.25"
                var formula = status.Definition.ModifierFormula.Replace("stacks", status.Stacks.ToString());
                formula = formula.Replace("duration", status.Duration.ToString());
                
                // Avaliar expressão simples (apenas multiplicação por enquanto)
                if (formula.Contains("*"))
                {
                    var parts = formula.Split('*');
                    if (parts.Length == 2 && 
                        float.TryParse(parts[0].Trim(), out var left) && 
                        float.TryParse(parts[1].Trim(), out var right))
                    {
                        return left * right;
                    }
                }
                
                // Fallback para BaseValue
                return status.Definition.BaseValue * (status.Definition.ScalesWithStacks ? status.Stacks : 1);
            }
            catch
            {
                // Fallback para BaseValue
                return status.Definition.BaseValue * (status.Definition.ScalesWithStacks ? status.Stacks : 1);
            }
        }
        
        // Usar BaseValue
        return status.Definition.BaseValue * (status.Definition.ScalesWithStacks ? status.Stacks : 1);
    }
}
