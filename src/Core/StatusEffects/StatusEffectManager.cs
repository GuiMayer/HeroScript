using System.Collections.Concurrent;
using Core.Common;
using Core.Config;
using Core.Events;
using Core.Events.Domain;
using Core.Math;
using Core.Resources;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.StatusEffects;

/// <summary>
/// Gerenciador de status effects.
/// Thread-safe, suporta múltiplas entidades simultaneamente.
/// </summary>
public class StatusEffectManager : IStatusEffectManager
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly IResourceManager _resourceManager;
    private readonly IRuntimeFormulaEvaluator _formulaEvaluator;
    private readonly IDefinitionPersister? _persister;
    private readonly IEventBus? _eventBus;
    
    // Status effects ativos por entidade (thread-safe)
    private readonly ConcurrentDictionary<Guid, List<StatusEffectInstance>> _activeStatus = new();
    
    // Definições de status effects carregadas (thread-safe)
    private readonly ConcurrentDictionary<string, StatusEffectDefinition> _definitions = new();
    
    // Processor para executar behaviors
    private readonly StatusEffectProcessor _processor;
    
    public StatusEffectManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        IResourceManager resourceManager,
        IRuntimeFormulaEvaluator formulaEvaluator,
        IEventBus? eventBus = null,
        IDefinitionPersister? persister = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _formulaEvaluator = formulaEvaluator ?? throw new ArgumentNullException(nameof(formulaEvaluator));
        _eventBus = eventBus;
        _persister = persister; // Optional for backward compatibility
        
        _processor = new StatusEffectProcessor(this, formulaEvaluator);
    }
    
    // ===== APLICAR/REMOVER =====
    
    public Result<StatusEffectInstance> ApplyStatus(
        Guid targetId,
        string statusId,
        int stacks = 1,
        int? duration = null,
        Guid? sourceId = null)
        => ApplyStatus(
            targetId,
            statusId,
            Guid.NewGuid(), // nondeterministic-boundary: compatibility overload outside replayable runs
            DateTime.UtcNow, // nondeterministic-boundary: compatibility overload outside replayable runs
            stacks,
            duration,
            sourceId);

    public Result<StatusEffectInstance> ApplyStatus(
        Guid targetId,
        string statusId,
        Guid instanceId,
        DateTime appliedAt,
        int stacks = 1,
        int? duration = null,
        Guid? sourceId = null)
    {
        if (string.IsNullOrWhiteSpace(statusId))
            return Result<StatusEffectInstance>.Failure("StatusId cannot be empty");

        if (instanceId == Guid.Empty)
            return Result<StatusEffectInstance>.Failure("InstanceId cannot be empty");
        
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
            var oldStacks = existingStatus.Stacks;
            var newStacks = System.Math.Min(existingStatus.Stacks + stacks, definition.MaxStacks);
            var updated = existingStatus with { Stacks = newStacks };
            
            UpdateStatusInstance(targetId, updated);
            _eventBus?.Publish(new StatusStackChangedEvent(targetId, statusId, existingStatus.InstanceId, oldStacks, newStacks));
            return Result<StatusEffectInstance>.Success(updated);
        }
        
        // Criar nova instância
        var instance = new StatusEffectInstance
        {
            InstanceId = instanceId,
            StatusId = statusId,
            Definition = definition,
            TargetId = targetId,
            SourceId = sourceId,
            Stacks = System.Math.Min(stacks, definition.MaxStacks),
            Duration = duration ?? definition.DefaultDuration,
            AppliedAt = appliedAt,
            TurnApplied = 0, // Será atualizado pelo CombatSystem
            IsActive = true
        };
        
        // Adicionar à lista
        var statusList = _activeStatus.GetOrAdd(targetId, _ => new List<StatusEffectInstance>());
        lock (statusList)
        {
            statusList.Add(instance);
        }
        
        _eventBus?.Publish(new StatusAppliedEvent(targetId, statusId, instance.InstanceId, instance.Stacks, instance.Duration, sourceId));
        return Result<StatusEffectInstance>.Success(instance);
    }
    
    public Result RemoveStatus(Guid targetId, Guid instanceId)
    {
        var statusList = GetActiveStatusList(targetId);
        string? statusId = null;
        
        lock (statusList)
        {
            var index = statusList.FindIndex(s => s.InstanceId == instanceId);
            if (index == -1)
                return Result.Failure($"Status instance {instanceId} not found");
            
            statusId = statusList[index].StatusId;
            statusList.RemoveAt(index);
        }
        
        _eventBus?.Publish(new StatusRemovedEvent(targetId, statusId!, instanceId));
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
        var toRemove = new List<StatusEffectInstance>();
        var ticked = new List<(StatusEffectInstance status, int newDuration)>();
        
        lock (statusList)
        {
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
                    ticked.Add((status, newDuration));
                }
            }
            
            // Remover status expirados
            foreach (var expired in toRemove)
            {
                statusList.Remove(expired);
            }
        }
        
        // Publicar eventos fora do lock
        foreach (var (status, newDuration) in ticked)
        {
            _eventBus?.Publish(new StatusTickProcessedEvent(targetId, status.StatusId, status.InstanceId, newDuration));
        }
        foreach (var expired in toRemove)
        {
            _eventBus?.Publish(new StatusExpiredEvent(targetId, expired.StatusId, expired.InstanceId));
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
            var chain = _configManager.ResolveInheritanceChain(configName);
            var data = _resourceLoader.LoadResource("StatusEffects/status_effects.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result.Failure("Status effects not found: StatusEffects/status_effects.json");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            var definitions = DeserializeStatusDefinitions(data, options);
            
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

    private static Dictionary<string, StatusEffectDefinition>? DeserializeStatusDefinitions(Dictionary<string, JsonElement> data, JsonSerializerOptions options)
    {
        var definitions = new Dictionary<string, StatusEffectDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, element) in data)
        {
            var definition = JsonSerializer.Deserialize<StatusEffectDefinition>(element.GetRawText(), options);
            if (definition == null)
                return null;

            var statusId = string.IsNullOrWhiteSpace(definition.StatusId) ? key : definition.StatusId;
            definitions[statusId] = definition with { StatusId = statusId };
        }

        return definitions;
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
        if (!string.IsNullOrWhiteSpace(status.Definition.ModifierFormula))
        {
            var variables = new Dictionary<string, float>
            {
                ["stacks"] = status.Stacks,
                ["duration"] = status.Duration
            };

            var formulaValue = _formulaEvaluator.Evaluate(status.Definition.ModifierFormula, variables);
            if (formulaValue.IsSuccess)
                return formulaValue.Value;
        }
        
        return status.Definition.BaseValue * (status.Definition.ScalesWithStacks ? status.Stacks : 1);
    }
    
    // ===== PERSISTÊNCIA =====
    
    public Result SaveDefinition(StatusEffectDefinition definition, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (definition == null)
            return Result.Failure("Definition cannot be null");

        // Basic validation
        if (string.IsNullOrWhiteSpace(definition.StatusId))
            return Result.Failure("StatusId is required");

        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            return Result.Failure("DisplayName is required");

        if (definition.MaxStacks < 1)
            return Result.Failure("MaxStacks must be at least 1");

        try
        {
            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(definition, options));
            
            // Save via persister
            var result = _persister.SaveDefinition("StatusEffects", definition.StatusId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Add to cache
            _definitions[definition.StatusId] = definition;

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to save status definition: {ex.Message}");
        }
    }

    public Result UpdateDefinition(string statusId, StatusEffectDefinition updatedDefinition, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (string.IsNullOrWhiteSpace(statusId))
            return Result.Failure("StatusId cannot be empty");

        if (updatedDefinition == null)
            return Result.Failure("Updated definition cannot be null");

        // Ensure IDs match
        if (updatedDefinition.StatusId != statusId)
            return Result.Failure($"StatusId mismatch: URL has '{statusId}' but definition has '{updatedDefinition.StatusId}'");

        // Basic validation
        if (string.IsNullOrWhiteSpace(updatedDefinition.DisplayName))
            return Result.Failure("DisplayName is required");

        if (updatedDefinition.MaxStacks < 1)
            return Result.Failure("MaxStacks must be at least 1");

        try
        {
            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(updatedDefinition, options));
            
            // Update via persister
            var result = _persister.UpdateDefinition("StatusEffects", statusId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Update cache
            _definitions[statusId] = updatedDefinition;

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to update status definition: {ex.Message}");
        }
    }

    public Result DeleteDefinition(string statusId, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (string.IsNullOrWhiteSpace(statusId))
            return Result.Failure("StatusId cannot be empty");

        try
        {
            // Delete via persister
            var result = _persister.DeleteDefinition("StatusEffects", statusId, configName);
            if (result.IsFailure)
                return result;

            // Remove from cache
            _definitions.TryRemove(statusId, out _);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete status definition: {ex.Message}");
        }
    }
}
