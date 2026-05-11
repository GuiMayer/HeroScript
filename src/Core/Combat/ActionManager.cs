using Core.Combat.Models;
using Core.Common;
using Core.Config;
using Core.Logging;
using System.Text.Json;

namespace Core.Combat;

/// <summary>
/// Gerenciador de ações configuráveis.
/// Carrega definições de ações de arquivos JSON.
/// </summary>
public class ActionManager : IActionManager
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ILogger _logger;
    private readonly Dictionary<string, ActionDefinition> _definitions = new();
    
    public ActionManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ILogger logger)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// Carrega definições de ações de um config.
    /// </summary>
    public void LoadActionDefinitions(string configName)
    {
        _definitions.Clear();
        
        try
        {
            // Obter cadeia de herança do config
            var configChain = _configManager.ResolveInheritanceChain(configName);
            
            // Tentar carregar ações conhecidas
            var actionNames = new[] { "basic_attack", "power_attack", "heal", "defend" };
            
            foreach (var actionName in actionNames)
            {
                try
                {
                    var relativePath = $"actions/{actionName}.json";
                    var actionData = _resourceLoader.LoadResource(relativePath, configChain, strictMode: false);
                    
                    if (actionData.Count == 0)
                        continue;
                    
                    var firstElement = actionData.Values.FirstOrDefault();
                    if (firstElement.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                        continue;
                    
                    var definition = JsonSerializer.Deserialize<ActionDefinition>(
                        firstElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (definition == null)
                    {
                        _logger.LogWarning($"Failed to deserialize action definition: {actionName}");
                        continue;
                    }
                    
                    var validation = ValidateActionDefinition(definition);
                    if (validation.IsFailure)
                    {
                        _logger.LogError($"Invalid action definition '{actionName}': {validation.Error}");
                        continue;
                    }
                    
                    _definitions[definition.ActionId] = definition;
                    _logger.LogDebug($"Loaded action: {definition.ActionId}");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Could not load action '{actionName}': {ex.Message}");
                }
            }
            
            _logger.LogInformation($"Loaded {_definitions.Count} action definitions from config '{configName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading action definitions: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Obtém definição de uma ação.
    /// </summary>
    public Result<ActionDefinition> GetDefinition(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
            return Result<ActionDefinition>.Failure("Action ID cannot be empty");
        
        if (_definitions.TryGetValue(actionId, out var definition))
            return Result<ActionDefinition>.Success(definition);
        
        return Result<ActionDefinition>.Failure($"Action not found: {actionId}");
    }
    
    /// <summary>
    /// Obtém todas as definições de ações.
    /// </summary>
    public IReadOnlyList<ActionDefinition> GetAllDefinitions()
    {
        return _definitions.Values.ToList();
    }
    
    /// <summary>
    /// Obtém definições por tipo.
    /// </summary>
    public IReadOnlyList<ActionDefinition> GetDefinitionsByType(ActionType actionType)
    {
        return _definitions.Values
            .Where(d => d.ActionType == actionType)
            .ToList();
    }
    
    /// <summary>
    /// Obtém definições por tag.
    /// </summary>
    public IReadOnlyList<ActionDefinition> GetDefinitionsByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<ActionDefinition>();
        
        return _definitions.Values
            .Where(d => d.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
    
    /// <summary>
    /// Valida definição de ação.
    /// </summary>
    public Result ValidateActionDefinition(ActionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ActionId))
            return Result.Failure("Action ID cannot be empty");
        
        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            return Result.Failure("Display name cannot be empty");
        
        if (definition.Cooldown < 0)
            return Result.Failure("Cooldown cannot be negative");
        
        // Validar custos
        foreach (var cost in definition.Costs.Costs)
        {
            if (string.IsNullOrWhiteSpace(cost.ResourceId))
                return Result.Failure("Resource ID in cost cannot be empty");
            
            if (cost.Amount < 0)
                return Result.Failure($"Cost amount for {cost.ResourceId} cannot be negative");
        }
        
        return Result.Success();
    }
}
