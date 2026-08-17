using API.Models.Gambits;
using Core.Combat;
using Core.Combat.Gambits;
using Core.Combat.Models;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/gambits")]
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
    [API.Attributes.AdminEndpoint]
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

    // Métodos de mapeamento
    private GambitDefinition MapFromDto(GambitDefinitionDto dto)
    {
        return new GambitDefinition
        {
            GambitId = dto.GambitId,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Priority = dto.Priority,
            Conditions = dto.Conditions?.Select(c => new GambitCondition
            {
                Type = Enum.TryParse<GambitConditionType>(c.Type, true, out var type)
                    ? type
                    : GambitConditionType.ALWAYS,
                Target = c.Target,
                ResourceId = c.ResourceId,
                LessThanOrEqual = c.LessThanOrEqual,
                GreaterThanOrEqual = c.GreaterThanOrEqual
            }).ToList() ?? new List<GambitCondition>(),
            Action = new GambitActionDefinition
            {
                ActionType = Enum.TryParse<ActionType>(dto.Action?.ActionType, true, out var actionType)
                    ? actionType
                    : ActionType.PASS,
                PowerId = dto.Action?.PowerId,
                Target = dto.Action?.Target,
                CostOptionId = dto.Action?.CostOptionId
            },
            Intent = new GambitIntentDefinition
            {
                DisplayName = dto.Intent?.DisplayName,
                Description = dto.Intent?.Description,
                TelegraphType = dto.Intent?.TelegraphType,
                Tags = dto.Intent?.Tags ?? new List<string>()
            },
            Tags = dto.Tags ?? new List<string>()
        };
    }
}
