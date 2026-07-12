using Microsoft.AspNetCore.Mvc;
using Core.Combat;
using Core.Combat.Models;
using Core.Entity;
using Core.Entity.Components;
using Core.Entity.Definitions;
using Core.Resources;
using API.Models.Entities;

namespace API.Controllers;

/// <summary>
/// Controller para gerenciamento de entidades.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EntityController : BaseApiController
{
    private readonly EntityDefinitionLoader _definitionLoader;
    private readonly ResourceManager _resourceManager;

    public EntityController(
        EntityDefinitionLoader definitionLoader,
        ResourceManager resourceManager,
        ILogger<EntityController> logger)
        : base(logger)
    {
        _definitionLoader = definitionLoader ?? throw new ArgumentNullException(nameof(definitionLoader));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
    }

    /// <summary>
    /// Lista todas as definições de entidades disponíveis.
    /// </summary>
    [HttpGet("definitions")]
    public IActionResult GetDefinitions()
    {
        try
        {
            var result = _definitionLoader.LoadAllDefinitions();
            
            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            var dtos = result.Value.Values.Select(MapToDefinitionDto).ToList();
            return Ok(dtos);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get entity definitions");
        }
    }

    /// <summary>
    /// Obtém uma definição de entidade específica.
    /// </summary>
    [HttpGet("definitions/{definitionId}")]
    public IActionResult GetDefinition(string definitionId)
    {
        try
        {
            var result = _definitionLoader.LoadDefinition(definitionId);
            
            if (result.IsFailure)
                return NotFound(new { error = result.Error });

            return Ok(MapToDefinitionDto(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get entity definition", definitionId);
        }
    }

    /// <summary>
    /// Cria uma nova entidade a partir de uma definição.
    /// </summary>
    [HttpPost("create")]
    public IActionResult CreateEntity([FromBody] CreateEntityRequest request)
    {
        try
        {
            // Carregar definição
            var defResult = _definitionLoader.LoadDefinition(request.DefinitionId);
            if (defResult.IsFailure)
                return NotFound(new { error = $"Definition not found: {request.DefinitionId}" });

            var definition = defResult.Value;
            
            // Gerar ID se não fornecido
            var entityId = request.EntityId ?? Guid.NewGuid().ToString();
            
            // Criar recursos
            var resources = new Dictionary<string, ResourcePool>();
            if (definition.Resources?.Resources != null)
            {
                foreach (var (resourceId, poolDef) in definition.Resources.Resources)
                {
                    var initialValue = request.InitialResources?.GetValueOrDefault(resourceId) ?? poolDef.Current;
                    
                    // Criar definição de recurso
                    var resourceDef = new ResourceDefinition
                    {
                        ResourceId = resourceId,
                        DisplayName = resourceId,
                        DefaultMax = poolDef.Max,
                        DefaultCurrent = initialValue,
                        CanExceedMax = false,
                        CanBeNegative = false
                    };
                    
                    // Criar pool
                    var pool = new ResourcePool
                    {
                        ResourceId = resourceId,
                        Current = initialValue,
                        Maximum = poolDef.Max,
                        Minimum = 0,
                        Definition = resourceDef
                    };
                    
                    resources[resourceId] = pool;
                }
            }
            
            // Criar estado de recursos
            var resourceState = new EntityResourceState
            {
                EntityId = entityId,
                Resources = resources
            };
            
            var resourceComponent = new ResourceComponent(resourceState);
            
            // Criar componentes
            var components = new Dictionary<Type, IComponent>
            {
                [typeof(ResourceComponent)] = resourceComponent
            };
            
            // Criar entidade
            var entity = new Core.Entity.Entity
            {
                EntityId = entityId,
                Type = definition.Type,
                DefinitionId = definition.DefinitionId,
                DisplayName = request.DisplayName ?? definition.DisplayName,
                Components = components
            };
            
            return Ok(MapToEntityDto(entity));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "create entity");
        }
    }

    /// <summary>
    /// Valida se uma definição de entidade é válida.
    /// </summary>
    [HttpPost("definitions/validate")]
    public IActionResult ValidateDefinition([FromBody] EntityDefinitionDto dto)
    {
        try
        {
            var errors = new List<string>();
            
            // Validações básicas
            if (string.IsNullOrWhiteSpace(dto.DefinitionId))
                errors.Add("DefinitionId is required");
            
            if (string.IsNullOrWhiteSpace(dto.Type))
                errors.Add("Type is required");
            
            if (string.IsNullOrWhiteSpace(dto.DisplayName))
                errors.Add("DisplayName is required");
            
            // Validar tipo
            if (!Enum.TryParse<Core.Entity.EntityType>(dto.Type, true, out _))
                errors.Add($"Invalid entity type: {dto.Type}");
            
            // Validar recursos
            if (dto.Resources != null)
            {
                foreach (var (resourceId, resource) in dto.Resources)
                {
                    if (resource.Max <= 0)
                        errors.Add($"Resource {resourceId}: Max must be positive");
                    
                    if (resource.Current < 0)
                        errors.Add($"Resource {resourceId}: Current cannot be negative");
                    
                    if (resource.Current > resource.Max)
                        errors.Add($"Resource {resourceId}: Current cannot exceed Max");
                }
            }
            
            if (errors.Any())
                return BadRequest(new { valid = false, errors });
            
            return Ok(new { valid = true, message = "Definition is valid" });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "validate entity definition");
        }
    }

    /// <summary>
    /// Cria uma nova definição de entidade
    /// </summary>
    [HttpPost("definitions")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(typeof(EntityDefinitionDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public IActionResult CreateDefinition([FromBody] EntityDefinitionDto dto, [FromQuery] string configName = "default")
    {
        // Sanitize path traversal
        if (!API.Helpers.ValidationHelper.IsValidConfigName(configName))
            return BadRequest(new { error = "Invalid configuration name" });

        if (dto == null)
            return BadRequest(new { error = "Entity definition cannot be null" });

        if (string.IsNullOrWhiteSpace(dto.DefinitionId))
            return BadRequest(new { error = "DefinitionId is required" });

        try
        {
            var definition = MapFromDto(dto);
            var result = _definitionLoader.SaveDefinition(definition, configName);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            _logger.LogInformation($"Created entity definition: {dto.DefinitionId}");
            return CreatedAtAction(nameof(GetDefinition), new { definitionId = dto.DefinitionId }, dto);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "create entity definition", dto.DefinitionId);
        }
    }

    /// <summary>
    /// Atualiza uma definição de entidade existente
    /// </summary>
    [HttpPut("definitions/{definitionId}")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(typeof(EntityDefinitionDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public IActionResult UpdateDefinition(string definitionId, [FromBody] EntityDefinitionDto dto, [FromQuery] string configName = "default")
    {
        // Sanitize path traversal
        if (!API.Helpers.ValidationHelper.IsValidConfigName(configName))
            return BadRequest(new { error = "Invalid configuration name" });

        if (string.IsNullOrWhiteSpace(definitionId))
            return BadRequest(new { error = "DefinitionId cannot be empty" });

        if (dto == null)
            return BadRequest(new { error = "Entity definition cannot be null" });

        if (dto.DefinitionId != definitionId)
            return BadRequest(new { error = $"DefinitionId mismatch: URL has '{definitionId}' but body has '{dto.DefinitionId}'" });

        try
        {
            var definition = MapFromDto(dto);
            var result = _definitionLoader.UpdateDefinition(definitionId, definition, configName);

            if (result.IsFailure)
            {
                if (result.Error.Contains("not found"))
                    return NotFound(new { error = result.Error });
                return BadRequest(new { error = result.Error });
            }

            _logger.LogInformation($"Updated entity definition: {definitionId}");
            return Ok(dto);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "update entity definition", definitionId);
        }
    }

    /// <summary>
    /// Deleta uma definição de entidade
    /// </summary>
    [HttpDelete("definitions/{definitionId}")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public IActionResult DeleteDefinition(string definitionId, [FromQuery] string configName = "default")
    {
        // Sanitize path traversal
        if (!API.Helpers.ValidationHelper.IsValidConfigName(configName))
            return BadRequest(new { error = "Invalid configuration name" });

        if (string.IsNullOrWhiteSpace(definitionId))
            return BadRequest(new { error = "DefinitionId cannot be empty" });

        try
        {
            var result = _definitionLoader.DeleteDefinition(definitionId, configName);

            if (result.IsFailure)
            {
                if (result.Error.Contains("not found"))
                    return NotFound(new { error = result.Error });
                return BadRequest(new { error = result.Error });
            }

            _logger.LogInformation($"Deleted entity definition: {definitionId}");
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex, "delete entity definition", definitionId);
        }
    }

    // Métodos de mapeamento
    private EntityDefinitionDto MapToDefinitionDto(EntityDefinition definition)
    {
        return new EntityDefinitionDto
        {
            DefinitionId = definition.DefinitionId,
            Type = definition.Type.ToString(),
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Resources = definition.Resources?.Resources?.ToDictionary(
                kvp => kvp.Key,
                kvp => new ResourceDefinitionDto
                {
                    Current = kvp.Value.Current,
                    Max = kvp.Value.Max
                }
            ),
            Attributes = definition.Stats?.CustomStats
        };
    }

    private EntityDto MapToEntityDto(Core.Entity.Entity entity)
    {
        var dto = new EntityDto
        {
            EntityId = entity.EntityId,
            Type = entity.Type.ToString(),
            DefinitionId = entity.DefinitionId,
            DisplayName = entity.DisplayName
        };
        
        // Mapear recursos
        if (entity.Components.TryGetValue(typeof(ResourceComponent), out var resourceComp) 
            && resourceComp is ResourceComponent rc)
        {
            dto.Resources = rc.ResourceState.Resources.ToDictionary(
                kvp => kvp.Key,
                kvp => new ResourcePoolDto
                {
                    ResourceId = kvp.Value.ResourceId,
                    Current = kvp.Value.Current,
                    Maximum = kvp.Value.Maximum,
                    Minimum = kvp.Value.Minimum
                }
            );
        }
        
        return dto;
    }

    private EntityDefinition MapFromDto(EntityDefinitionDto dto)
    {
        var definition = new EntityDefinition
        {
            DefinitionId = dto.DefinitionId,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Type = Enum.TryParse<Core.Entity.EntityType>(dto.Type, true, out var entityType) 
                ? entityType 
                : Core.Entity.EntityType.NPC,
            Resources = dto.Resources != null && dto.Resources.Any()
                ? new ResourcesDefinition
                {
                    Resources = dto.Resources.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new ResourcePoolDefinition
                        {
                            Current = kvp.Value.Current,
                            Max = kvp.Value.Max
                        }
                    )
                }
                : null,
            Stats = dto.Attributes != null && dto.Attributes.Any()
                ? new StatsDefinition
                {
                    CustomStats = dto.Attributes
                }
                : null
        };

        return definition;
    }
}
