using Microsoft.AspNetCore.Mvc;
using API.Contracts;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using API.Models.Combat;
using Core.Run;
using Core.Determinism;
using Core.Combat.TurnPhase;

namespace API.Controllers;

/// <summary>
/// Read-only combat projections and legality queries. Gameplay mutations are
/// owned exclusively by CombatCommandController and RunCommandController.
/// </summary>
[ApiController]
[Route("api/v1/combats")]
public class CombatController : BaseApiController
{
    private readonly ICombatSystem _combatSystem;
    private readonly IActionManager _actionManager;
    private readonly IActionAffordabilityService _affordabilityService;
    private readonly ICombatRunCoordinator _combatRunCoordinator;
    private readonly IRunManager _runManager;

    public CombatController(
        ICombatSystem combatSystem, 
        IActionManager actionManager,
        IActionAffordabilityService affordabilityService,
        ICombatRunCoordinator combatRunCoordinator,
        IRunManager runManager,
        ILogger<CombatController> logger)
        : base(logger)
    {
        _combatSystem = combatSystem ?? throw new ArgumentNullException(nameof(combatSystem));
        _actionManager = actionManager ?? throw new ArgumentNullException(nameof(actionManager));
        _affordabilityService = affordabilityService ?? throw new ArgumentNullException(nameof(affordabilityService));
        _combatRunCoordinator = combatRunCoordinator ?? throw new ArgumentNullException(nameof(combatRunCoordinator));
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
    }

    [HttpGet("/api/v1/runs/{runId:guid}/encounters/current")]
    public IActionResult GetCurrentRunEncounter(Guid runId)
    {
        try
        {
            var result = _combatRunCoordinator.GetCurrentEncounter(runId);
            return result.IsFailure
                ? NotFound(new { error = result.Error })
                : Ok(MapToStateResponse(result.Value.CombatState));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get current run encounter", runId.ToString());
        }
    }

    /// <summary>
    /// Obtém estado atual do combate.
    /// </summary>
    [HttpGet("/api/v1/combats/{combatId:guid}")]
    public IActionResult GetState(Guid combatId)
    {
        try
        {
            var result = GetCombatStateIncludingOwned(combatId);

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
            {
                var recovered = GetCombatStateIncludingOwned(combatId);
                if (recovered.IsSuccess)
                    result = Result<IReadOnlyList<CombatAction>>.Success(recovered.Value.ActionHistory);
            }

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
            var stateResult = GetCombatStateIncludingOwned(combatId);
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
    /// Lista ações disponíveis para um ator no combate atual.
    /// </summary>
    [HttpGet("{combatId}/available-actions")]
    public IActionResult GetAvailableActions(Guid combatId, [FromQuery] string? actorId = null, [FromQuery] Guid? runId = null)
    {
        try
        {
            var stateResult = GetCombatStateIncludingOwned(combatId);
            if (stateResult.IsFailure)
                return NotFound(new { error = stateResult.Error });

            var state = stateResult.Value;
            var actor = ResolveActor(state, actorId);
            if (actor == null)
                return NotFound(new { error = $"Actor {actorId} not found" });

            var allActions = _actionManager.GetAllDefinitions();
            var hand = GetRunHand(runId, out var runError);
            if (runError != null)
                return runError;
            var handCounts = hand?.GroupBy(cardId => cardId).ToDictionary(g => g.Key, g => g.Count()) ?? new Dictionary<string, int>();
            
            var availableActions = allActions.Select(action =>
            {
                var affordabilityResult = _affordabilityService.CanAfford(action, actor.ResourceState.Resources);
                var affordability = affordabilityResult.IsSuccess 
                    ? affordabilityResult.Value 
                    : new AffordabilityResult { ActionId = action.ActionId, CanAfford = false };
                var cardCountInHand = handCounts.GetValueOrDefault(action.ActionId);
                var inHand = hand == null || cardCountInHand > 0;
                
                return new
                {
                    actionId = action.ActionId,
                    displayName = action.DisplayName,
                    actionType = action.ActionType.ToString(),
                    requiresTarget = action.RequiresTarget,
                    multiTarget = action.MultiTarget,
                    effectCount = action.Effects.Count,
                    effects = action.Effects.Select(e => new
                    {
                        effectId = e.EffectId,
                        type = e.Type.ToString(),
                        target = e.Target.ToString(),
                        timing = e.Timing.ToString(),
                        flatValue = e.FlatValue,
                        formulaValue = e.FormulaValue,
                        targetResource = e.TargetResource,
                        statusId = e.StatusId
                    }).ToList(),
                    tags = action.Tags,
                    canAfford = affordability.CanAfford,
                    affordableOptions = affordability.AffordableOptionIds,
                    inHand,
                    cardCountInHand,
                    willConsumeTo = ResolveConsumeDestination(action).ToString()
                };
            }).Where(action => hand == null || action.inHand).ToList();

            return Ok(new
            {
                combatId = combatId,
                actorId = actor.EntityId,
                runId,
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

    [HttpGet("/api/v1/combats/{combatId:guid}/legal-actions")]
    public IActionResult GetLegalActions(Guid combatId, [FromQuery] string? actorId = null)
    {
        var stateResult = GetCombatStateIncludingOwned(combatId);
        if (stateResult.IsFailure)
            return ApiNotFound(stateResult.Error);

        var state = stateResult.Value;
        var actor = ResolveActor(state, actorId);
        if (actor == null)
            return ApiNotFound($"Actor {actorId} not found");
        IReadOnlyList<string>? hand = null;
        if (state.RunId is { } runId)
        {
            var run = _runManager.GetRun(runId);
            if (run.IsFailure)
                return ApiNotFound(run.Error);
            hand = run.Value.Deck.Hand;
        }

        var legal = _actionManager.GetAllDefinitions()
            .Where(action => state.IsActionAllowedInCurrentPhase(action.ActionType))
            .Where(action => hand == null || hand.Contains(action.ActionId, StringComparer.Ordinal))
            .Select(action => new
            {
                definition = action,
                affordability = _affordabilityService.CanAfford(action, actor.ResourceState.Resources)
            })
            .Where(item => item.affordability.IsSuccess &&
                (item.affordability.Value.CanAfford || item.affordability.Value.AffordableOptionIds.Count > 0))
            .OrderBy(item => item.definition.ActionId, StringComparer.Ordinal)
            .Select(item => new
            {
                actionId = item.definition.ActionId,
                actionType = item.definition.ActionType.ToString(),
                item.definition.RequiresTarget,
                item.definition.MultiTarget,
                affordableOptionIds = item.affordability.Value.AffordableOptionIds
            })
            .ToArray();

        return Ok(new
        {
            combatId,
            actorId = actor.EntityId,
            phase = state.PhaseState?.CurrentPhaseId,
            priorityActorId = state.GetCurrentPriorityPlayer(),
            actions = legal
        });
    }

    [HttpGet("/api/v1/combats/{combatId:guid}/legal-targets")]
    public IActionResult GetLegalTargets(
        Guid combatId,
        [FromQuery] string actionId,
        [FromQuery] string? actorId = null)
    {
        var stateResult = GetCombatStateIncludingOwned(combatId);
        if (stateResult.IsFailure)
            return ApiNotFound(stateResult.Error);
        var action = _actionManager.GetDefinition(actionId);
        if (action.IsFailure)
            return ApiNotFound(action.Error);
        var actor = ResolveActor(stateResult.Value, actorId);
        if (actor == null)
            return ApiNotFound($"Actor {actorId} not found");

        var targets = action.Value.RequiresTarget
            ? stateResult.Value.GetAllEntities()
                .Where(entity => entity.IsAlive && entity.EntityId != actor.EntityId)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray()
            : [];
        return Ok(new
        {
            combatId,
            actorId = actor.EntityId,
            actionId = action.Value.ActionId,
            action.Value.RequiresTarget,
            action.Value.MultiTarget,
            targetIds = targets
        });
    }

    [HttpGet("/api/v1/combats/{combatId:guid}/stack")]
    public IActionResult GetStack(Guid combatId)
    {
        var state = GetCombatStateIncludingOwned(combatId);
        if (state.IsFailure)
            return ApiNotFound(state.Error);
        var stack = state.Value.PhaseState?.ActionStack ?? new ActionStack();
        return Ok(new
        {
            combatId,
            phase = state.Value.PhaseState?.CurrentPhaseId,
            priorityActorId = state.Value.GetCurrentPriorityPlayer(),
            stack.IsResolving,
            stack.CurrentlyResolving,
            actions = stack.Actions
                .OrderByDescending(action => action.StackPosition)
                .ToArray()
        });
    }

    /// <summary>
    /// Verifica se um ator pode pagar por uma ação específica.
    /// </summary>
    [HttpPost("{combatId}/actions/{actionId}/can-afford")]
    public IActionResult CanAffordAction(Guid combatId, string actionId, [FromQuery] string? actorId = null, [FromQuery] Guid? runId = null)
    {
        try
        {
            var stateResult = GetCombatStateIncludingOwned(combatId);
            if (stateResult.IsFailure)
                return NotFound(new { error = stateResult.Error });

            var actionDefResult = _actionManager.GetDefinition(actionId);
            if (actionDefResult.IsFailure)
                return NotFound(new { error = $"Action {actionId} not found" });

            var state = stateResult.Value;
            var actor = ResolveActor(state, actorId);
            if (actor == null)
                return NotFound(new { error = $"Actor {actorId} not found" });

            var actionDef = actionDefResult.Value;
            var hand = GetRunHand(runId, out var runError);
            if (runError != null)
                return runError;
            var cardCountInHand = hand?.Count(cardId => cardId == actionId) ?? 0;
            
            var affordabilityResult = _affordabilityService.CanAfford(actionDef, actor.ResourceState.Resources);
            
            if (affordabilityResult.IsFailure)
                return BadRequest(new { error = affordabilityResult.Error });

            var affordability = affordabilityResult.Value;

            return Ok(new
            {
                actionId = affordability.ActionId,
                actorId = actor.EntityId,
                runId,
                canAfford = affordability.CanAfford,
                affordableOptionIds = affordability.AffordableOptionIds,
                inHand = hand == null || cardCountInHand > 0,
                cardCountInHand,
                willConsumeTo = ResolveConsumeDestination(actionDef).ToString(),
                error = affordability.Error
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "check affordability", combatId.ToString());
        }
    }

    private Result<CombatState> GetCombatStateIncludingOwned(Guid combatId)
    {
        var current = _combatSystem.GetCombatState(combatId);
        if (current.IsSuccess && !current.Value.RunId.HasValue)
            return current;

        var recovered = _combatRunCoordinator.GetCombatState(combatId);
        return recovered.IsSuccess
            ? Result<CombatState>.Success(recovered.Value.CombatState)
            : current.IsSuccess
                ? Result<CombatState>.Failure(recovered.Error)
                : current;
    }

    // Mappers
    private CombatStateResponse MapToStateResponse(CombatState state)
    {
        return new CombatStateResponse
        {
            CombatId = state.CombatId,
            RunId = state.RunId,
            RunNodeId = state.RunNodeId,
            Seed = state.Determinism.Seed,
            Step = state.Determinism.Step,
            ContentRevision = state.Determinism.ContentRevision,
            EngineVersion = state.Determinism.EngineVersion,
            StateHash = CanonicalJson.ComputeHash(state),
            Status = state.Status.ToString(),
            CurrentTurn = state.CurrentTurn,
            Hero = new HeroStateDto
            {
                EntityId = state.Hero.EntityId,
                Name = state.Hero.Name,
                CurrentHp = (int)(state.Hero.GetResource("health")?.Current ?? 0f),
                MaxHp = (int)(state.Hero.GetResource("health")?.Maximum ?? 0f),
                IsAlive = state.Hero.IsAlive,
                Resources = MapResources(state.Hero)
            },
            Enemies = state.Enemies.Select(e => new EnemyStateDto
            {
                EntityId = e.EntityId,
                Name = e.Name,
                CurrentHp = (int)(e.GetResource("health")?.Current ?? 0f),
                MaxHp = (int)(e.GetResource("health")?.Maximum ?? 0f),
                IsAlive = e.IsAlive,
                Resources = MapResources(e)
            }).ToList(),
            Energy = new EnergyDto
            {
                Current = (int)(state.GetHeroResource("energy")?.Current ?? 0f),
                Maximum = (int)(state.GetHeroResource("energy")?.Maximum ?? 0f)
            },
            TotalActions = state.ActionHistory.Count,
            Board = state.Board,
            Phase = state.PhaseState,
            Activation = state.ActivationState
        };
    }

    private static IReadOnlyDictionary<string, ResourcePoolDto> MapResources(CombatEntity entity)
    {
        return entity.ResourceState.Resources
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                pair => pair.Key,
                pair => new ResourcePoolDto
                {
                    Current = pair.Value.Current,
                    Maximum = pair.Value.Maximum,
                    Minimum = pair.Value.Minimum
                },
                StringComparer.Ordinal);
    }

    private CombatEntity? ResolveActor(CombatState state, string? actorId)
    {
        return string.IsNullOrWhiteSpace(actorId)
            ? state.Hero
            : state.GetEntity(actorId);
    }

    private IReadOnlyList<string>? GetRunHand(Guid? runId, out IActionResult? error)
    {
        error = null;
        if (!runId.HasValue)
            return null;

        var runResult = _runManager.GetRun(runId.Value);
        if (runResult.IsFailure)
        {
            error = NotFound(new { error = runResult.Error });
            return null;
        }

        return runResult.Value.Deck.Hand;
    }

    private static CardConsumeDestination ResolveConsumeDestination(ActionDefinition actionDefinition)
    {
        if (actionDefinition.Tags.Any(tag => string.Equals(tag, "retain", StringComparison.OrdinalIgnoreCase)))
            return CardConsumeDestination.None;

        if (actionDefinition.Tags.Any(tag => string.Equals(tag, "exhaust", StringComparison.OrdinalIgnoreCase)))
            return CardConsumeDestination.Exhaust;

        return CardConsumeDestination.Discard;
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
