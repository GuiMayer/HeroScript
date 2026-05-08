using Microsoft.AspNetCore.Mvc;
using Core.Combat;
using API.Models.Combat;

namespace API.Controllers;

/// <summary>
/// Controller para gerenciamento de combates.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CombatController : BaseApiController
{
    private readonly ICombatSystem _combatSystem;

    public CombatController(ICombatSystem combatSystem, ILogger<CombatController> logger)
        : base(logger)
    {
        _combatSystem = combatSystem ?? throw new ArgumentNullException(nameof(combatSystem));
    }

    /// <summary>
    /// Inicia novo combate.
    /// </summary>
    [HttpPost("start")]
    public IActionResult StartCombat([FromBody] StartCombatRequest request)
    {
        try
        {
            var result = _combatSystem.StartCombat(
                request.HeroId,
                request.Enemies,
                request.InitialEnergy);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            var state = result.Value;
            return Ok(MapToStateResponse(state));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "start combat");
        }
    }

    /// <summary>
    /// Executa ação em combate.
    /// </summary>
    [HttpPost("{combatId}/action")]
    public IActionResult ExecuteAction(Guid combatId, [FromBody] ExecuteActionRequest request)
    {
        try
        {
            if (!Enum.TryParse<ActionType>(request.ActionType, true, out var actionType))
                return BadRequest(new { error = $"Invalid action type: {request.ActionType}" });

            var result = _combatSystem.ExecuteAction(
                combatId,
                actionType,
                request.PowerId,
                request.TargetId);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            var state = result.Value;
            return Ok(MapToStateResponse(state));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "execute action", combatId.ToString());
        }
    }

    /// <summary>
    /// Obtém estado atual do combate.
    /// </summary>
    [HttpGet("{combatId}/state")]
    public IActionResult GetState(Guid combatId)
    {
        try
        {
            var result = _combatSystem.GetCombatState(combatId);

            if (result.IsFailure)
                return NotFound(new { error = result.Error });

            return Ok(MapToStateResponse(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get combat state", combatId.ToString());
        }
    }

    /// <summary>
    /// Obtém histórico de ações do combate.
    /// </summary>
    [HttpGet("{combatId}/history")]
    public IActionResult GetHistory(Guid combatId)
    {
        try
        {
            var result = _combatSystem.GetActionHistory(combatId);

            if (result.IsFailure)
                return NotFound(new { error = result.Error });

            var actions = result.Value;
            return Ok(new CombatHistoryResponse
            {
                CombatId = combatId,
                TotalActions = actions.Count,
                Actions = actions.Select(MapToActionDto).ToList()
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get combat history", combatId.ToString());
        }
    }

    /// <summary>
    /// Finaliza combate.
    /// </summary>
    [HttpPost("{combatId}/end")]
    public IActionResult EndCombat(Guid combatId)
    {
        try
        {
            var result = _combatSystem.EndCombat(combatId);

            if (result.IsFailure)
                return NotFound(new { error = result.Error });

            var combatResult = result.Value;
            return Ok(new
            {
                combatId = combatResult.CombatId,
                status = combatResult.Status.ToString(),
                totalTurns = combatResult.TotalTurns,
                totalActions = combatResult.TotalActions,
                damageDealt = combatResult.DamageDealt,
                damageTaken = combatResult.DamageTaken,
                duration = combatResult.Duration.TotalSeconds
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "end combat", combatId.ToString());
        }
    }

    // Mappers
    private CombatStateResponse MapToStateResponse(CombatState state)
    {
        return new CombatStateResponse
        {
            CombatId = state.CombatId,
            Status = state.Status.ToString(),
            CurrentTurn = state.CurrentTurn,
            Hero = new HeroStateDto
            {
                EntityId = state.Hero.EntityId,
                Name = state.Hero.Name,
                CurrentHp = state.Hero.CurrentHp,
                MaxHp = state.Hero.MaxHp,
                IsAlive = state.Hero.IsAlive
            },
            Enemies = state.Enemies.Select(e => new EnemyStateDto
            {
                EntityId = e.EntityId,
                Name = e.Name,
                CurrentHp = e.CurrentHp,
                MaxHp = e.MaxHp,
                IsAlive = e.IsAlive
            }).ToList(),
            Energy = new EnergyDto
            {
                Current = state.Energy.Current,
                Maximum = state.Energy.Maximum
            },
            TotalActions = state.ActionHistory.Count
        };
    }

    private ActionDto MapToActionDto(CombatAction action)
    {
        return new ActionDto
        {
            ActionId = action.ActionId,
            Timestamp = action.Timestamp,
            Turn = action.Turn,
            ActorId = action.ActorId,
            ActionType = action.ActionType.ToString(),
            PowerId = action.PowerId,
            TargetId = action.TargetId,
            DamageDealt = action.DamageDealt,
            EnergyChange = action.EnergyChange
        };
    }
}
