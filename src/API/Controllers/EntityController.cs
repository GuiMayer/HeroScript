using API.Models.Entities;
using Core.Entity.Definitions;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>Read-only entity content catalog and authoring validation.</summary>
[ApiController]
[Route("api/v1/entities")]
public sealed class EntityController : BaseApiController
{
    private readonly EntityDefinitionLoader _definitions;

    public EntityController(EntityDefinitionLoader definitions, ILogger<EntityController> logger) : base(logger) =>
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    [HttpGet("definitions")]
    public IActionResult GetDefinitions()
    {
        var result = _definitions.LoadAllDefinitions();
        return result.IsFailure
            ? BadRequest(new { error = result.Error })
            : Ok(result.Value.Values.OrderBy(item => item.DefinitionId, StringComparer.Ordinal).Select(Map));
    }

    [HttpGet("definitions/{definitionId}")]
    public IActionResult GetDefinition(string definitionId)
    {
        var result = _definitions.LoadDefinition(definitionId);
        return result.IsFailure ? NotFound(new { error = result.Error }) : Ok(Map(result.Value));
    }

    [HttpPost("definitions/validate")]
    public IActionResult ValidateDefinition([FromBody] EntityDefinition definition)
    {
        var result = EntityDefinitionValidator.Validate(definition);
        return result.IsFailure
            ? UnprocessableEntity(new { valid = false, errors = new[] { result.Error } })
            : Ok(new { valid = true });
    }

    private static EntityDefinitionDto Map(EntityDefinition definition) => new()
    {
        DefinitionId = definition.DefinitionId,
        DisplayName = definition.DisplayName,
        Description = definition.Description,
        IconPath = definition.IconPath,
        SpritePath = definition.SpritePath,
        Components = definition.Components,
        Metadata = definition.Metadata
    };
}
