using API.Models.Modifiers;
using Core.Combat.Modifiers;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/modifiers")]
public class ModifierController : BaseApiController
{
    private readonly IScriptModifierManager _modifierManager;

    public ModifierController(IScriptModifierManager modifierManager, ILogger<ModifierController> logger)
        : base(logger)
    {
        _modifierManager = modifierManager ?? throw new ArgumentNullException(nameof(modifierManager));
    }

    [HttpGet]
    public IActionResult GetDefinitions()
    {
        return Ok(_modifierManager.GetAllDefinitions().Select(ModifierDefinitionResponse.FromDefinition));
    }

    [HttpGet("{modifierId}")]
    public IActionResult GetDefinition(string modifierId)
    {
        var result = _modifierManager.GetDefinition(modifierId);
        return result.IsSuccess
            ? Ok(ModifierDefinitionResponse.FromDefinition(result.Value))
            : NotFound(new { error = result.Error });
    }

    [HttpPost("reload")]
    [API.Attributes.AdminEndpoint]
    public IActionResult Reload([FromQuery] string configName = "default")
    {
        var result = _modifierManager.LoadDefinitions(configName);
        return result.IsSuccess
            ? Ok(new { message = "Script modifiers reloaded", configName })
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("apply")]
    [API.Attributes.AdminEndpoint]
    public IActionResult Apply([FromBody] ApplyModifierRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerId))
            return BadRequest(new { error = "OwnerId cannot be empty" });

        if (string.IsNullOrWhiteSpace(request.ModifierId))
            return BadRequest(new { error = "ModifierId cannot be empty" });

        var result = _modifierManager.ApplyModifier(
            request.OwnerId,
            request.ModifierId,
            request.Stacks,
            request.Duration,
            request.SourceId);

        return result.IsSuccess
            ? Ok(ModifierInstanceResponse.FromInstance(result.Value))
            : BadRequest(new { error = result.Error });
    }

    [HttpGet("active/{ownerId}")]
    public IActionResult GetActive(string ownerId)
    {
        return Ok(_modifierManager.GetActiveModifiers(ownerId).Select(ModifierInstanceResponse.FromInstance));
    }

    [HttpGet("active/{ownerId}/pipeline")]
    public IActionResult GetPipelineModifiers(string ownerId, [FromQuery] List<string>? tags = null)
    {
        return Ok(_modifierManager.GetPipelineModifiers(ownerId, tags));
    }

    [HttpDelete("active/{ownerId}/{instanceId:guid}")]
    [API.Attributes.AdminEndpoint]
    public IActionResult Remove(string ownerId, Guid instanceId)
    {
        var result = _modifierManager.RemoveModifier(ownerId, instanceId);
        return result.IsSuccess
            ? Ok(new { message = "Script modifier removed" })
            : NotFound(new { error = result.Error });
    }

    [HttpPost("active/{ownerId}/tick")]
    [API.Attributes.AdminEndpoint]
    public IActionResult TickDurations(string ownerId)
    {
        var result = _modifierManager.TickDurations(ownerId);
        return result.IsSuccess
            ? Ok(new { message = "Script modifier durations ticked" })
            : BadRequest(new { error = result.Error });
    }
}
