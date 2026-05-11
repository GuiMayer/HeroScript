using API.Models.Actions;
using Core.Combat;
using Core.Combat.Models;
using Core.Effects;
using Microsoft.AspNetCore.Mvc;
using CoreLogger = Core.Logging.ILogger;

namespace API.Controllers;

/// <summary>
/// Controller para gerenciamento de definições de ações
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ActionController : ControllerBase
{
    private readonly IActionManager _actionManager;
    private readonly CoreLogger _logger;

    public ActionController(IActionManager actionManager, CoreLogger logger)
    {
        _actionManager = actionManager ?? throw new ArgumentNullException(nameof(actionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lista todas as ações disponíveis
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ActionSummaryDto>), 200)]
    public IActionResult GetAllActions()
    {
        try
        {
            var definitions = _actionManager.GetAllDefinitions();
            var summaries = definitions.Select(MapToSummary).ToList();
            
            _logger.LogDebug($"Retrieved {summaries.Count} action definitions");
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting all actions: {ex.Message}");
            return StatusCode(500, new { error = "Failed to retrieve actions", details = ex.Message });
        }
    }

    /// <summary>
    /// Obtém definição de uma ação específica
    /// </summary>
    [HttpGet("{actionId}")]
    [ProducesResponseType(typeof(ActionDefinitionDto), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetAction(string actionId)
    {
        try
        {
            var result = _actionManager.GetDefinition(actionId);
            
            if (result.IsFailure)
                return NotFound(new { error = result.Error });
            
            var dto = MapToDto(result.Value);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting action {actionId}: {ex.Message}");
            return StatusCode(500, new { error = "Failed to retrieve action", details = ex.Message });
        }
    }

    /// <summary>
    /// Filtra ações por tipo
    /// </summary>
    [HttpGet("by-type/{actionType}")]
    [ProducesResponseType(typeof(List<ActionSummaryDto>), 200)]
    [ProducesResponseType(400)]
    public IActionResult GetActionsByType(string actionType)
    {
        try
        {
            if (!Enum.TryParse<ActionType>(actionType, true, out var type))
                return BadRequest(new { error = $"Invalid action type: {actionType}" });
            
            var definitions = _actionManager.GetDefinitionsByType(type);
            var summaries = definitions.Select(MapToSummary).ToList();
            
            _logger.LogDebug($"Retrieved {summaries.Count} actions of type {actionType}");
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting actions by type {actionType}: {ex.Message}");
            return StatusCode(500, new { error = "Failed to retrieve actions", details = ex.Message });
        }
    }

    /// <summary>
    /// Filtra ações por tag
    /// </summary>
    [HttpGet("by-tag/{tag}")]
    [ProducesResponseType(typeof(List<ActionSummaryDto>), 200)]
    public IActionResult GetActionsByTag(string tag)
    {
        try
        {
            var definitions = _actionManager.GetDefinitionsByTag(tag);
            var summaries = definitions.Select(MapToSummary).ToList();
            
            _logger.LogDebug($"Retrieved {summaries.Count} actions with tag '{tag}'");
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting actions by tag {tag}: {ex.Message}");
            return StatusCode(500, new { error = "Failed to retrieve actions", details = ex.Message });
        }
    }

    /// <summary>
    /// Valida uma definição de ação
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ActionValidationResponse), 200)]
    [ProducesResponseType(400)]
    public IActionResult ValidateAction([FromBody] ActionValidationRequest request)
    {
        try
        {
            if (request?.Definition == null)
                return BadRequest(new { error = "Definition is required" });
            
            var definition = MapFromDto(request.Definition);
            var result = _actionManager.ValidateActionDefinition(definition);
            
            var response = new ActionValidationResponse
            {
                IsValid = result.IsSuccess,
                Errors = result.IsFailure ? new List<string> { result.Error } : new List<string>()
            };
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error validating action: {ex.Message}");
            return StatusCode(500, new { error = "Failed to validate action", details = ex.Message });
        }
    }

    /// <summary>
    /// Recarrega definições de ações (dev mode)
    /// </summary>
    [HttpPost("reload")]
    [ProducesResponseType(200)]
    [ProducesResponseType(500)]
    public IActionResult ReloadActions([FromQuery] string configName = "base")
    {
        try
        {
            _actionManager.LoadActionDefinitions(configName);
            var count = _actionManager.GetAllDefinitions().Count;
            
            _logger.LogDebug($"Reloaded {count} action definitions from config '{configName}'");
            return Ok(new { message = $"Reloaded {count} actions successfully", configName, timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error reloading actions: {ex.Message}");
            return StatusCode(500, new { error = "Failed to reload actions", details = ex.Message });
        }
    }

    // Mapping helpers
    private ActionSummaryDto MapToSummary(ActionDefinition definition)
    {
        return new ActionSummaryDto
        {
            ActionId = definition.ActionId,
            DisplayName = definition.DisplayName,
            ActionType = definition.ActionType.ToString(),
            Cooldown = definition.Cooldown,
            Tags = definition.Tags,
            CostOptionsCount = definition.Costs.AlternativeCosts.Count + (definition.Costs.Costs.Any() ? 1 : 0)
        };
    }

    private ActionDefinitionDto MapToDto(ActionDefinition definition)
    {
        return new ActionDefinitionDto
        {
            ActionId = definition.ActionId,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            ActionType = definition.ActionType.ToString(),
            Cooldown = definition.Cooldown,
            BaseDamage = definition.Effects
                .Where(e => e.Type == EffectType.DAMAGE)
                .Sum(e => e.FlatValue ?? 0f),
            Tags = definition.Tags,
            Costs = new ActionCostsDto
            {
                Costs = definition.Costs.Costs.Select(c => new ResourceCostDto
                {
                    ResourceId = c.ResourceId,
                    Amount = c.Amount,
                    AllowOverdraft = c.AllowOverdraft
                }).ToList(),
                AlternativeOptions = definition.Costs.AlternativeCosts.Select(o => new AlternativeCostOptionDto
                {
                    OptionId = o.OptionId,
                    Description = o.Description,
                    Costs = o.Costs.Select(c => new ResourceCostDto
                    {
                        ResourceId = c.ResourceId,
                        Amount = c.Amount,
                        AllowOverdraft = c.AllowOverdraft
                    }).ToList()
                }).ToList()
            }
        };
    }

    private ActionDefinition MapFromDto(ActionDefinitionDto dto)
    {
        return new ActionDefinition
        {
            ActionId = dto.ActionId,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            ActionType = Enum.TryParse<ActionType>(dto.ActionType, true, out var type) ? type : ActionType.POWER,
            Cooldown = dto.Cooldown,
            Effects = new List<EffectDefinition>
            {
                new EffectDefinition
                {
                    Type = EffectType.DAMAGE,
                    FlatValue = dto.BaseDamage,
                    Target = EffectTarget.TARGET
                }
            },
            Tags = dto.Tags,
            Costs = new ActionCosts
            {
                Costs = dto.Costs.Costs.Select(c => new ResourceCost
                {
                    ResourceId = c.ResourceId,
                    Amount = c.Amount,
                    AllowOverdraft = c.AllowOverdraft
                }).ToList(),
                AlternativeCosts = dto.Costs.AlternativeOptions.Select(o => new AlternativeCostOption
                {
                    OptionId = o.OptionId,
                    Description = o.Description,
                    Costs = o.Costs.Select(c => new ResourceCost
                    {
                        ResourceId = c.ResourceId,
                        Amount = c.Amount,
                        AllowOverdraft = c.AllowOverdraft
                    }).ToList()
                }).ToList()
            }
        };
    }
}
