using API.Models.Gambits;
using Core.Combat;
using Core.Combat.Gambits;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/gambits")]
public class GambitController : BaseApiController
{
    private readonly IGambitEngine _gambitEngine;
    private readonly ICombatSystem _combatSystem;

    public GambitController(IGambitEngine gambitEngine, ICombatSystem combatSystem, ILogger<GambitController> logger)
        : base(logger)
    {
        _gambitEngine = gambitEngine ?? throw new ArgumentNullException(nameof(gambitEngine));
        _combatSystem = combatSystem ?? throw new ArgumentNullException(nameof(combatSystem));
    }

    [HttpGet]
    public IActionResult GetDefinitions()
    {
        return Ok(_gambitEngine.GetAllDefinitions().Select(GambitDefinitionResponse.FromDefinition));
    }

    [HttpGet("{gambitId}")]
    public IActionResult GetDefinition(string gambitId)
    {
        var result = _gambitEngine.GetDefinition(gambitId);
        return result.IsSuccess
            ? Ok(GambitDefinitionResponse.FromDefinition(result.Value))
            : NotFound(new { error = result.Error });
    }

    [HttpPost("reload")]
    public IActionResult Reload([FromQuery] string configName = "default")
    {
        var result = _gambitEngine.LoadDefinitions(configName);
        return result.IsSuccess
            ? Ok(new { message = "Gambits reloaded", configName })
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("decide")]
    public IActionResult Decide([FromBody] DecideGambitActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
            return BadRequest(new { error = "EntityId cannot be empty" });

        var stateResult = _combatSystem.GetCombatState(request.CombatId);
        if (stateResult.IsFailure)
            return NotFound(new { error = stateResult.Error });

        var state = stateResult.Value;
        if (state.GetEntity(request.EntityId) == null)
            return NotFound(new { error = $"Entity not found: {request.EntityId}" });

        var entity = new Core.Entity.Entity { EntityId = request.EntityId };
        var decision = _gambitEngine.DecideAction(entity, state, request.GambitIds);
        return decision.IsSuccess
            ? Ok(GambitDecisionResponse.FromAction(request.EntityId, decision.Value))
            : BadRequest(new { error = decision.Error });
    }
}
