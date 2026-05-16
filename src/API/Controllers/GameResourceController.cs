using API.Models.Resources;
using Core.Resources;
using Microsoft.AspNetCore.Mvc;
using CoreLogger = Core.Logging.ILogger;

namespace API.Controllers;

/// <summary>
/// Controller para gerenciamento de recursos de gameplay
/// </summary>
[ApiController]
[Route("api/game-resources")]
public class GameResourceController : ControllerBase
{
    private readonly IResourceManager _resourceManager;
    private readonly CoreLogger _logger;

    public GameResourceController(IResourceManager resourceManager, CoreLogger logger)
    {
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lista todas as definições de recursos
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ResourceSummaryDto>), 200)]
    public IActionResult GetAllResources()
    {
        try
        {
            var definitions = _resourceManager.GetAllDefinitions();
            var summaries = definitions.Select(MapToSummary).ToList();
            
            _logger.LogDebug($"Retrieved {summaries.Count} resource definitions");
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting all resources: {ex.Message}");
            return StatusCode(500, new { error = "Failed to retrieve resources", details = ex.Message });
        }
    }

    /// <summary>
    /// Obtém definição de um recurso específico
    /// </summary>
    [HttpGet("{resourceId}")]
    [ProducesResponseType(typeof(ResourceDefinitionDto), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetResource(string resourceId)
    {
        try
        {
            var result = _resourceManager.GetDefinition(resourceId);
            
            if (result.IsFailure)
                return NotFound(new { error = result.Error });
            
            var dto = MapToDto(result.Value);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting resource {resourceId}: {ex.Message}");
            return StatusCode(500, new { error = "Failed to retrieve resource", details = ex.Message });
        }
    }

    /// <summary>
    /// Filtra recursos por categoria
    /// </summary>
    [HttpGet("by-category/{category}")]
    [ProducesResponseType(typeof(List<ResourceSummaryDto>), 200)]
    [ProducesResponseType(400)]
    public IActionResult GetResourcesByCategory(string category)
    {
        try
        {
            if (!Enum.TryParse<ResourceCategory>(category, true, out var categoryEnum))
                return BadRequest(new { error = $"Invalid category: {category}" });
            
            var definitions = _resourceManager.GetDefinitionsByCategory(categoryEnum);
            var summaries = definitions.Select(MapToSummary).ToList();
            
            _logger.LogDebug($"Retrieved {summaries.Count} resources in category {category}");
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting resources by category {category}: {ex.Message}");
            return StatusCode(500, new { error = "Failed to retrieve resources", details = ex.Message });
        }
    }

    /// <summary>
    /// Filtra recursos por tag
    /// </summary>
    [HttpGet("by-tag/{tag}")]
    [ProducesResponseType(typeof(List<ResourceSummaryDto>), 200)]
    public IActionResult GetResourcesByTag(string tag)
    {
        try
        {
            var definitions = _resourceManager.GetDefinitionsByTag(tag);
            var summaries = definitions.Select(MapToSummary).ToList();
            
            _logger.LogDebug($"Retrieved {summaries.Count} resources with tag '{tag}'");
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting resources by tag {tag}: {ex.Message}");
            return StatusCode(500, new { error = "Failed to retrieve resources", details = ex.Message });
        }
    }

    /// <summary>
    /// Valida uma definição de recurso
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ResourceValidationResponse), 200)]
    [ProducesResponseType(400)]
    public IActionResult ValidateResource([FromBody] ResourceValidationRequest request)
    {
        try
        {
            if (request?.Definition == null)
                return BadRequest(new { error = "Definition is required" });
            
            var definition = MapFromDto(request.Definition);
            var result = _resourceManager.ValidateResourceDefinition(definition);
            
            var response = new ResourceValidationResponse
            {
                IsValid = result.IsSuccess,
                Errors = result.IsFailure ? new List<string> { result.Error } : new List<string>()
            };
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error validating resource: {ex.Message}");
            return StatusCode(500, new { error = "Failed to validate resource", details = ex.Message });
        }
    }

    /// <summary>
    /// Recarrega definições de recursos (dev mode)
    /// </summary>
    [HttpPost("reload")]
    [ProducesResponseType(200)]
    [ProducesResponseType(500)]
    public IActionResult ReloadResources([FromQuery] string configName = "default")
    {
        try
        {
            _resourceManager.LoadResourceDefinitions(configName);
            var count = _resourceManager.GetAllDefinitions().Count;
            
            _logger.LogDebug($"Reloaded {count} resource definitions from config '{configName}'");
            return Ok(new { message = $"Reloaded {count} resources successfully", configName, timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error reloading resources: {ex.Message}");
            return StatusCode(500, new { error = "Failed to reload resources", details = ex.Message });
        }
    }

    /// <summary>
    /// Cria um pool de recurso
    /// </summary>
    [HttpPost("create-pool")]
    [ProducesResponseType(typeof(ResourcePoolDto), 200)]
    [ProducesResponseType(400)]
    public IActionResult CreatePool([FromBody] CreatePoolRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.ResourceId))
                return BadRequest(new { error = "ResourceId is required" });
            
            var pool = _resourceManager.CreatePool(request.ResourceId, request.InitialCurrent);
            var dto = MapPoolToDto(pool);
            
            _logger.LogDebug($"Created pool for resource {request.ResourceId}");
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating pool: {ex.Message}");
            return StatusCode(500, new { error = "Failed to create pool", details = ex.Message });
        }
    }

    /// <summary>
    /// Valida se há recurso suficiente para um custo
    /// </summary>
    [HttpPost("validate-cost")]
    [ProducesResponseType(typeof(ValidateCostResponse), 200)]
    [ProducesResponseType(400)]
    public IActionResult ValidateCost([FromBody] ValidateCostRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.ResourceId))
                return BadRequest(new { error = "ResourceId is required" });
            
            // Create a mock pool with the current amount
            var poolResult = _resourceManager.GetDefinition(request.ResourceId);
            if (poolResult.IsFailure)
                return NotFound(new { error = $"Resource {request.ResourceId} not found" });
            
            var pool = _resourceManager.CreatePool(request.ResourceId, request.CurrentAmount);
            var validationResult = _resourceManager.ValidateCost(pool, request.Cost);
            
            var response = new ValidateCostResponse
            {
                CanAfford = validationResult.IsSuccess,
                Error = validationResult.IsFailure ? validationResult.Error : null
            };
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error validating cost: {ex.Message}");
            return StatusCode(500, new { error = "Failed to validate cost", details = ex.Message });
        }
    }

    // Mapping helpers
    private ResourceSummaryDto MapToSummary(ResourceDefinition definition)
    {
        return new ResourceSummaryDto
        {
            ResourceId = definition.ResourceId,
            DisplayName = definition.DisplayName,
            Category = definition.Category.ToString(),
            DefaultMax = definition.DefaultMax,
            Tags = definition.Tags
        };
    }

    private ResourceDefinitionDto MapToDto(ResourceDefinition definition)
    {
        return new ResourceDefinitionDto
        {
            ResourceId = definition.ResourceId,
            DisplayName = definition.DisplayName,
            ShortName = definition.ShortName,
            Category = definition.Category.ToString(),
            DefaultCurrent = definition.DefaultCurrent,
            DefaultMax = definition.DefaultMax,
            DefaultMin = definition.DefaultMin,
            CanBeNegative = definition.CanBeNegative,
            CanExceedMax = definition.CanExceedMax,
            Tags = definition.Tags
        };
    }

    private ResourceDefinition MapFromDto(ResourceDefinitionDto dto)
    {
        return new ResourceDefinition
        {
            ResourceId = dto.ResourceId,
            DisplayName = dto.DisplayName,
            ShortName = dto.ShortName,
            Category = Enum.TryParse<ResourceCategory>(dto.Category, true, out var cat) ? cat : ResourceCategory.TACTICAL,
            DefaultCurrent = dto.DefaultCurrent,
            DefaultMax = dto.DefaultMax,
            DefaultMin = dto.DefaultMin,
            CanBeNegative = dto.CanBeNegative,
            CanExceedMax = dto.CanExceedMax,
            Tags = dto.Tags
        };
    }

    private ResourcePoolDto MapPoolToDto(ResourcePool pool)
    {
        return new ResourcePoolDto
        {
            ResourceId = pool.ResourceId,
            Current = pool.Current,
            Maximum = pool.Maximum,
            Minimum = pool.Minimum,
            Percentage = pool.GetPercentage()
        };
    }
}
