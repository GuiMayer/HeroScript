using System.Collections.Concurrent;
using Core.Common;
using Core.Config;
using Core.Math;
using Core.Resources;
using System.Globalization;
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
    private readonly IMathEngine _mathEngine;
    
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
        IMathEngine mathEngine)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
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
        if (data.TryGetValue("statusEffects", out var legacyStatusEffects) &&
            legacyStatusEffects.ValueKind == JsonValueKind.Array)
        {
            return DeserializeLegacyStatusDefinitions(legacyStatusEffects);
        }

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

    private static Dictionary<string, StatusEffectDefinition> DeserializeLegacyStatusDefinitions(JsonElement statusEffects)
    {
        var definitions = new Dictionary<string, StatusEffectDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in statusEffects.EnumerateArray())
        {
            var rawStatusId = GetString(element, "statusId");
            if (string.IsNullOrWhiteSpace(rawStatusId))
                continue;

            var statusId = rawStatusId.ToLowerInvariant();
            var behavior = GetPrimaryLegacyBehavior(element);
            var definition = new StatusEffectDefinition
            {
                StatusId = statusId,
                Type = ParseEnum(rawStatusId, StatusEffectType.CUSTOM),
                DisplayName = GetString(element, "displayName") ?? rawStatusId,
                Description = GetString(element, "description") ?? string.Empty,
                Behavior = MapLegacyBehavior(GetString(behavior, "type")),
                DefaultDuration = GetInt(element, "defaultDuration", -1),
                DefaultStacks = 1,
                MaxStacks = GetInt(element, "maxStacks", 99),
                BaseValue = GetFloat(behavior, "value", 0f),
                FormulaValue = GetString(behavior, "formulaValue"),
                ScalesWithStacks = GetBool(behavior, "scalesWithStacks", true),
                ModifierKey = GetString(behavior, "modifierKey"),
                ModifierFormula = GetString(behavior, "formulaValue"),
                Timing = MapLegacyTiming(GetString(behavior, "timing")),
                Tags = GetLegacyTags(element)
            };

            definitions[statusId] = definition;
        }

        return definitions;
    }

    private static JsonElement GetPrimaryLegacyBehavior(JsonElement element)
    {
        if (element.TryGetProperty("behaviors", out var behaviors) &&
            behaviors.ValueKind == JsonValueKind.Array &&
            behaviors.GetArrayLength() > 0)
        {
            return behaviors[0];
        }

        return default;
    }

    private static StatusEffectBehavior MapLegacyBehavior(string? behavior)
    {
        return behavior?.ToUpperInvariant() switch
        {
            "DAMAGE_OVER_TIME" => StatusEffectBehavior.DAMAGE_OVER_TIME,
            "HEAL_OVER_TIME" => StatusEffectBehavior.HEAL_OVER_TIME,
            "STAT_MODIFIER" => StatusEffectBehavior.STAT_MODIFIER,
            "ABSORB_DAMAGE" => StatusEffectBehavior.SHIELD,
            "REFLECT_DAMAGE" => StatusEffectBehavior.REACTIVE,
            "SKIP_TURN" or "DISABLE_POWERS" => StatusEffectBehavior.CONTROL,
            "PREVENT_NEXT_DEBUFF" => StatusEffectBehavior.PREVENT_NEXT_DEBUFF,
            "DAMAGE_CAP" => StatusEffectBehavior.DAMAGE_CAP,
            "DEATH_PREVENTION" => StatusEffectBehavior.DEATH_PREVENTION,
            _ => StatusEffectBehavior.RULE_MODIFIER
        };
    }

    private static StatusEffectTiming MapLegacyTiming(string? timing)
    {
        return timing?.ToUpperInvariant() switch
        {
            "START_OF_TURN" => StatusEffectTiming.START_OF_TURN,
            "END_OF_TURN" => StatusEffectTiming.END_OF_TURN,
            "ON_DAMAGE_DEALT" => StatusEffectTiming.ON_DAMAGE_DEALT,
            "ON_DAMAGE_TAKEN" => StatusEffectTiming.ON_DAMAGE_TAKEN,
            "ON_DEBUFF_APPLIED" or "ON_STATUS_APPLIED" => StatusEffectTiming.ON_STATUS_APPLIED,
            "ON_STATUS_REMOVED" => StatusEffectTiming.ON_STATUS_REMOVED,
            "PASSIVE" or "PERMANENT" => StatusEffectTiming.PERMANENT,
            _ => StatusEffectTiming.PERMANENT
        };
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int GetInt(JsonElement element, string propertyName, int fallback)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.TryGetInt32(out var value)
            ? value
            : fallback;
    }

    private static float GetFloat(JsonElement element, string propertyName, float fallback)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.TryGetSingle(out var value)
            ? value
            : fallback;
    }

    private static bool GetBool(JsonElement element, string propertyName, bool fallback)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : fallback;
    }

    private static List<string> GetLegacyTags(JsonElement element)
    {
        var tags = new List<string>();
        var type = GetString(element, "type");
        if (!string.IsNullOrWhiteSpace(type))
            tags.Add(type.ToLowerInvariant());

        return tags;
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

            if (TryEvaluateFormula(status.Definition.ModifierFormula, variables, out var formulaValue))
                return formulaValue;
        }
        
        return status.Definition.BaseValue * (status.Definition.ScalesWithStacks ? status.Stacks : 1);
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
