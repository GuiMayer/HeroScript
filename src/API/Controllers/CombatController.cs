using Microsoft.AspNetCore.Mvc;
using API.Contracts;
using Core.Combat;
using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using API.Models.Combat;
using API.Models.Gambits;
using Core.Run;
using Core.Determinism;
using Core.Combat.TurnPhase;

namespace API.Controllers;

/// <summary>
/// Controller para gerenciamento de combates.
/// </summary>
[ApiController]
[Route("api/v1/combats")]
public class CombatController : BaseApiController
{
    private readonly ICombatSystem _combatSystem;
    private readonly IActionManager _actionManager;
    private readonly IActionAffordabilityService _affordabilityService;
    private readonly IGambitEngine _gambitEngine;
    private readonly ICombatRunCoordinator _combatRunCoordinator;
    private readonly IRunManager _runManager;

    public CombatController(
        ICombatSystem combatSystem, 
        IActionManager actionManager,
        IActionAffordabilityService affordabilityService,
        IGambitEngine gambitEngine,
        ICombatRunCoordinator combatRunCoordinator,
        IRunManager runManager,
        ILogger<CombatController> logger)
        : base(logger)
    {
        _combatSystem = combatSystem ?? throw new ArgumentNullException(nameof(combatSystem));
        _actionManager = actionManager ?? throw new ArgumentNullException(nameof(actionManager));
        _affordabilityService = affordabilityService ?? throw new ArgumentNullException(nameof(affordabilityService));
        _gambitEngine = gambitEngine ?? throw new ArgumentNullException(nameof(gambitEngine));
        _combatRunCoordinator = combatRunCoordinator ?? throw new ArgumentNullException(nameof(combatRunCoordinator));
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
    }

    /// <summary>
    /// Inicia novo combate.
    /// </summary>
    [HttpPost("start")]
    public IActionResult StartCombat([FromBody] StartCombatRequest request)
    {
        try
        {
            if (request.RunId.HasValue)
                return RunCombatCommandsRequired();

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
    /// Executa ação em combate.
    /// </summary>
    [HttpPost("{combatId}/action")]
    public IActionResult ExecuteAction(Guid combatId, [FromBody] ExecuteActionRequest request)
    {
        try
        {
            var current = GetCombatStateIncludingOwned(combatId);
            if (current.IsFailure)
                return NotFound(new { error = current.Error });
            if (current.Value.RunId.HasValue || request.RunId.HasValue)
                return RunCombatCommandsRequired();

            var resolveResult = ResolveExecutionRequest(request, out var actionType, out var powerId);
            if (resolveResult != null)
                return resolveResult;

            if (string.IsNullOrWhiteSpace(request.ActorId))
                return BadRequest(new { error = "ActorId is required" });

            var command = new CombatActionCommand
            {
                ActorId = request.ActorId,
                ActionType = actionType,
                PowerId = powerId,
                TargetId = request.TargetId,
                CostOptionId = request.CostOptionId,
                RunId = null,
                CardId = request.CardId
            };

            var result = _combatSystem.ExecuteAction(combatId, command);

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
    /// Encerra o turno atual usando a ação data-driven END_TURN.
    /// </summary>
    [HttpPost("{combatId}/end-turn")]
    public IActionResult EndTurn(Guid combatId)
    {
        try
        {
            var stateResult = GetCombatStateIncludingOwned(combatId);
            if (stateResult.IsFailure)
                return NotFound(new { error = stateResult.Error });
            if (stateResult.Value.RunId.HasValue)
                return RunCombatCommandsRequired();

            var command = new CombatActionCommand
            {
                ActorId = stateResult.Value.Hero.EntityId,
                ActionType = ActionType.END_TURN,
                RunId = null
            };
            var result = _combatSystem.ExecuteAction(combatId, command);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(MapToStateResponse(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "end turn", combatId.ToString());
        }
    }

    /// <summary>
    /// Processa decisões de IA via Gambits para inimigos vivos.
    /// </summary>
    [HttpPost("{combatId}/process-ai-turns")]
    public IActionResult ProcessAiTurns(Guid combatId, [FromBody] ProcessAiTurnsRequest? request = null)
    {
        try
        {
            var stateResult = GetCombatStateIncludingOwned(combatId);
            if (stateResult.IsFailure)
                return NotFound(new { error = stateResult.Error });
            if (stateResult.Value.RunId.HasValue)
                return RunCombatCommandsRequired();

            var state = stateResult.Value;
            var decisions = new List<object>();

            foreach (var enemyId in state.Enemies.Where(e => e.IsAlive).Select(e => e.EntityId).ToList())
            {
                var currentEnemy = state.GetEntity(enemyId);
                if (currentEnemy == null || !currentEnemy.IsAlive || !state.IsActive)
                    continue;

                var decision = ExecuteAiAction(combatId, currentEnemy, state, request?.GambitIds, out var updatedState);
                decisions.Add(decision);
                if (updatedState != null)
                    state = updatedState;
            }

            return Ok(new
            {
                combatId,
                processedEnemies = decisions.Count,
                executed = decisions.Count > 0,
                decisions,
                state = MapToStateResponse(state)
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "process AI turns", combatId.ToString());
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

    /// <summary>
    /// Finaliza combate.
    /// </summary>
    [HttpPost("{combatId}/end")]
    public IActionResult EndCombat(Guid combatId)
    {
        try
        {
            var state = GetCombatStateIncludingOwned(combatId);
            if (state.IsSuccess && state.Value.RunId is { } runId)
                return RunCombatCommandsRequired();

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

    /// <summary>
    /// Simula combate completo automaticamente até conclusão.
    /// Processa turnos do herói (via gambits se disponível) e inimigos até o combate terminar.
    /// </summary>
    [HttpPost("{combatId}/auto-play")]
    public IActionResult AutoPlayCombat(Guid combatId)
    {
        try
        {
            // Validar que combate existe e obter estado inicial
            var stateResult = GetCombatStateIncludingOwned(combatId);
            if (stateResult.IsFailure)
                return NotFound(new { error = stateResult.Error });

            var state = stateResult.Value;
            if (state.RunId.HasValue)
                return RunCombatCommandsRequired();
            var turnsProcessed = 0;
            var maxTurns = 100; // Limite de segurança para evitar loops infinitos
            var actionsExecuted = 0;

            // Loop principal: processar turnos até combate terminar
            while (state.IsActive && turnsProcessed < maxTurns)
            {
                // Processar turno de inimigos (IA)
                var aiResult = ProcessAiTurnsInternal(combatId, state);
                if (aiResult.actionsExecuted > 0)
                {
                    actionsExecuted += aiResult.actionsExecuted;
                    state = aiResult.state;
                }

                // Verificar se combate terminou após turno de inimigos
                if (!state.IsActive)
                    break;

                // Processar turno do herói (via end-turn que processa efeitos e reinicia energia)
                var endTurnCommand = new CombatActionCommand
                {
                    ActorId = state.Hero.EntityId,
                    ActionType = ActionType.END_TURN,
                    RunId = null
                };
                var endTurnResult = _combatSystem.ExecuteAction(combatId, endTurnCommand);

                if (endTurnResult.IsSuccess)
                {
                    state = endTurnResult.Value;
                    turnsProcessed++;
                }

                // Verificar se combate terminou
                if (!state.IsActive)
                    break;

                // Break adicional para evitar stalemate (ninguém causa dano)
                if (turnsProcessed > 50 && actionsExecuted == 0)
                {
                    _logger.LogWarning("Auto-play stalemate detected: no actions executed in last 50 turns");
                    break;
                }
            }

            // Finalizar combate se ainda estiver ativo
            if (state.IsActive)
            {
                var endResult = _combatSystem.EndCombat(combatId);
                if (endResult.IsSuccess)
                {
                    var combatResult = endResult.Value;
                    return Ok(new
                    {
                        combatId = combatResult.CombatId,
                        status = combatResult.Status.ToString(),
                        totalTurns = combatResult.TotalTurns,
                        totalActions = combatResult.TotalActions,
                        turnsProcessed,
                        actionsExecuted,
                        reachedMaxTurns = turnsProcessed >= maxTurns,
                        damageDealt = combatResult.DamageDealt,
                        damageTaken = combatResult.DamageTaken,
                        duration = combatResult.Duration.TotalSeconds
                    });
                }
            }

            // Retornar estado final
            var finalStateResult = GetCombatStateIncludingOwned(combatId);
            if (finalStateResult.IsSuccess)
            {
                return Ok(new
                {
                    combatId,
                    status = finalStateResult.Value.Status.ToString(),
                    currentTurn = finalStateResult.Value.CurrentTurn,
                    totalActions = finalStateResult.Value.ActionHistory.Count,
                    turnsProcessed,
                    actionsExecuted,
                    reachedMaxTurns = turnsProcessed >= maxTurns,
                    heroAlive = finalStateResult.Value.Hero.IsAlive,
                    enemiesAlive = finalStateResult.Value.Enemies.Count(e => e.IsAlive),
                    state = MapToStateResponse(finalStateResult.Value)
                });
            }

            return Ok(new
            {
                combatId,
                turnsProcessed,
                actionsExecuted,
                reachedMaxTurns = turnsProcessed >= maxTurns,
                message = "Auto-play completed"
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "auto-play combat", combatId.ToString());
        }
    }

    private (CombatState state, int actionsExecuted) ProcessAiTurnsInternal(Guid combatId, CombatState state)
    {
        var actionsExecuted = 0;

        foreach (var enemyId in state.Enemies.Where(e => e.IsAlive).Select(e => e.EntityId).ToList())
        {
            var currentEnemy = state.GetEntity(enemyId);
            if (currentEnemy == null || !currentEnemy.IsAlive || !state.IsActive)
                continue;

            var decision = ExecuteAiAction(combatId, currentEnemy, state, null, out var updatedState);
            if (updatedState != null)
            {
                state = updatedState;
                actionsExecuted++;
            }
        }

        return (state, actionsExecuted);
    }

    private IActionResult RunCombatCommandsRequired()
    {
        return ApiProblem(
            StatusCodes.Status422UnprocessableEntity,
            ApiErrorCodes.RuleViolation,
            "Run-owned combat command required",
            "Use the run or combat command endpoint for any mutation of a run-owned combat.");
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
    private IActionResult? ResolveExecutionRequest(ExecuteActionRequest request, out ActionType actionType, out string? powerId)
    {
        actionType = ActionType.PASS;
        powerId = request.PowerId;

        if (!string.IsNullOrWhiteSpace(request.ActionId))
        {
            var actionResult = _actionManager.GetDefinition(request.ActionId);
            if (actionResult.IsFailure)
                return NotFound(new { error = $"Action {request.ActionId} not found" });

            var actionDefinition = actionResult.Value;
            actionType = actionDefinition.ActionType == ActionType.BASIC_ATTACK
                ? ActionType.BASIC_ATTACK
                : ActionType.POWER;
            powerId = actionType == ActionType.POWER ? actionDefinition.ActionId : null;
            return null;
        }

        if (!Enum.TryParse<ActionType>(request.ActionType, true, out actionType))
            return BadRequest(new { error = $"Invalid action type: {request.ActionType}" });

        return null;
    }

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

    private object MapCombatRunActionResponse(CombatRunActionResult result)
    {
        return new
        {
            combatId = result.CombatState.CombatId,
            combat = MapToStateResponse(result.CombatState),
            run = new
            {
                result.RunState.RunId,
                result.RunState.Gold,
                result.RunState.PowerPoints,
                deck = MapDeck(result.RunState.Deck)
            },
            consumedCardId = result.ConsumedCardId,
            destination = result.Destination.ToString()
        };
    }

    private static object MapDeck(Core.Run.DeckState deck)
    {
        return new
        {
            deck.DrawPile,
            deck.Hand,
            deck.DiscardPile,
            deck.ExhaustPile,
            counts = new
            {
                drawPile = deck.DrawPile.Count,
                hand = deck.Hand.Count,
                discardPile = deck.DiscardPile.Count,
                exhaustPile = deck.ExhaustPile.Count
            }
        };
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

    private object ExecuteAiAction(Guid combatId, CombatEntity enemy, CombatState state, IEnumerable<string>? gambitIds, out CombatState? updatedState)
    {
        updatedState = null;
        var entity = new Core.Entity.Entity
        {
            EntityId = enemy.EntityId,
            DisplayName = enemy.Name
        };

        var decisionResult = _gambitEngine.DecideAction(entity, state, gambitIds);
        if (decisionResult.IsFailure)
        {
            return new
            {
                entityId = enemy.EntityId,
                executed = false,
                error = decisionResult.Error
            };
        }

        var action = decisionResult.Value;
        var command = new CombatActionCommand
        {
            ActorId = enemy.EntityId,
            ActionType = action.ActionType,
            PowerId = action.PowerId,
            TargetId = action.TargetId,
            CostOptionId = action.CostOptionId?.ToString(),
            RunId = state.RunId
        };
        var executionResult = state.RunId.HasValue
            ? _combatRunCoordinator.ExecuteAction(combatId, command).Map(value => value.CombatState)
            : _combatSystem.ExecuteAction(combatId, command);

        if (executionResult.IsFailure)
        {
            return new
            {
                entityId = enemy.EntityId,
                executed = false,
                decision = GambitDecisionResponse.FromAction(enemy.EntityId, action),
                error = executionResult.Error
            };
        }

        updatedState = executionResult.Value;

        return new
        {
            entityId = enemy.EntityId,
            executed = true,
            decision = GambitDecisionResponse.FromAction(enemy.EntityId, action)
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

public sealed record ProcessAiTurnsRequest(IReadOnlyList<string>? GambitIds);
