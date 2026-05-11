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
}
