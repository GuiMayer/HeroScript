using Microsoft.AspNetCore.Mvc;
using Core.Combat;
using Core.Combat.Models;
using Core.Effects;
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
    private readonly IActionManager _actionManager;
    private readonly IActionAffordabilityService _affordabilityService;

    public CombatController(
        ICombatSystem combatSystem, 
        IActionManager actionManager,
        IActionAffordabilityService affordabilityService,
        ILogger<CombatController> logger)
        : base(logger)
    {
        _combatSystem = combatSystem ?? throw new ArgumentNullException(nameof(combatSystem));
        _actionManager = actionManager ?? throw new ArgumentNullException(nameof(actionManager));
        _affordabilityService = affordabilityService ?? throw new ArgumentNullException(nameof(affordabilityService));
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
                request.TargetId,
                request.CostOptionId);

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
    /// Obtém opções de custo disponíveis para uma ação.
    /// </summary>
    [HttpGet("{combatId}/actions/{actionId}/cost-options")]
    public IActionResult GetCostOptions(Guid combatId, string actionId)
    {
        try
        {
            var stateResult = _combatSystem.GetCombatState(combatId);
            if (stateResult.IsFailure)
                return NotFound(new { error = stateResult.Error });

            var actionDefResult = _actionManager.GetDefinition(actionId);
            if (actionDefResult.IsFailure)
                return NotFound(new { error = $"Action {actionId} not found" });

            var actionDef = actionDefResult.Value;
            var heroResources = stateResult.Value.Hero.ResourceState.Resources;
            
            var costOptionsResult = _affordabilityService.GetCostOptions(actionDef, heroResources);
            
            if (costOptionsResult.IsFailure)
                return BadRequest(new { error = costOptionsResult.Error });

            var costOptions = costOptionsResult.Value;

            return Ok(new
            {
                actionId = costOptions.ActionId,
                normalCosts = costOptions.NormalCosts.Select(c => new
                {
                    resourceId = c.ResourceId,
                    amount = c.Amount,
                    allowOverdraft = c.AllowOverdraft
                }).ToList(),
                alternativeOptions = costOptions.AlternativeOptions.Select(opt => new
                {
                    optionId = opt.OptionId,
                    description = opt.Description,
                    costs = opt.Costs.Select(c => new { resourceId = c.ResourceId, amount = c.Amount }).ToList(),
                    affordable = opt.Affordable
                }).ToList(),
                affordableOptionIds = costOptions.AffordableOptionIds
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get cost options", combatId.ToString());
        }
    }

    /// <summary>
    /// Lista ações disponíveis para o herói no combate atual
    /// </summary>
    [HttpGet("{combatId}/available-actions")]
    public IActionResult GetAvailableActions(Guid combatId)
    {
        try
        {
            var stateResult = _combatSystem.GetCombatState(combatId);
            if (stateResult.IsFailure)
                return NotFound(new { error = stateResult.Error });

            var allActions = _actionManager.GetAllDefinitions();
            var heroResources = stateResult.Value.Hero.ResourceState.Resources;
            
            var availableActions = allActions.Select(action =>
            {
                var affordabilityResult = _affordabilityService.CanAfford(action, heroResources);
                var affordability = affordabilityResult.IsSuccess 
                    ? affordabilityResult.Value 
                    : new AffordabilityResult { ActionId = action.ActionId, CanAfford = false };
                
                return new
                {
                    actionId = action.ActionId,
                    displayName = action.DisplayName,
                    actionType = action.ActionType.ToString(),
                    baseDamage = action.Effects
                        .Where(e => e.Type == EffectType.DAMAGE)
                        .Sum(e => e.FlatValue ?? 0f),
                    tags = action.Tags,
                    canAfford = affordability.CanAfford,
                    affordableOptions = affordability.AffordableOptionIds
                };
            }).ToList();

            return Ok(new
            {
                combatId = combatId,
                totalActions = availableActions.Count,
                affordableActions = availableActions.Count(a => a.canAfford || a.affordableOptions.Any()),
                actions = availableActions
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get available actions", combatId.ToString());
        }
    }

    /// <summary>
    /// Verifica se o herói pode pagar por uma ação específica
    /// </summary>
    [HttpPost("{combatId}/actions/{actionId}/can-afford")]
    public IActionResult CanAffordAction(Guid combatId, string actionId)
    {
        try
        {
            var stateResult = _combatSystem.GetCombatState(combatId);
            if (stateResult.IsFailure)
                return NotFound(new { error = stateResult.Error });

            var actionDefResult = _actionManager.GetDefinition(actionId);
            if (actionDefResult.IsFailure)
                return NotFound(new { error = $"Action {actionId} not found" });

            var actionDef = actionDefResult.Value;
            var heroResources = stateResult.Value.Hero.ResourceState.Resources;
            
            var affordabilityResult = _affordabilityService.CanAfford(actionDef, heroResources);
            
            if (affordabilityResult.IsFailure)
                return BadRequest(new { error = affordabilityResult.Error });

            var affordability = affordabilityResult.Value;

            return Ok(new
            {
                actionId = affordability.ActionId,
                canAfford = affordability.CanAfford,
                affordableOptionIds = affordability.AffordableOptionIds,
                error = affordability.Error
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "check affordability", combatId.ToString());
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
