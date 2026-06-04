using Core.Combat.Activation;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/combat/{combatId:guid}/activation")]
public sealed class CombatActivationController : BaseApiController
{
    private readonly ICombatActivationCoordinator _activationCoordinator;

    public CombatActivationController(
        ICombatActivationCoordinator activationCoordinator,
        ILogger<CombatActivationController> logger)
        : base(logger)
    {
        _activationCoordinator = activationCoordinator ?? throw new ArgumentNullException(nameof(activationCoordinator));
    }

    [HttpPost("start")]
    public IActionResult Start(Guid combatId, [FromBody] StartActivationRequest request)
    {
        try
        {
            var result = _activationCoordinator.StartActivationCycle(combatId, request.RunId, request.RulesId);
            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(MapResult(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "start combat activation", combatId.ToString());
        }
    }

    [HttpGet("state")]
    public IActionResult GetState(Guid combatId)
    {
        try
        {
            var result = _activationCoordinator.GetActivationState(combatId);
            if (result.IsFailure)
                return NotFound(new { error = result.Error });

            return Ok(MapResult(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get combat activation", combatId.ToString());
        }
    }

    [HttpGet("intents")]
    public IActionResult GetIntents(Guid combatId)
    {
        try
        {
            var result = _activationCoordinator.GetActivationState(combatId);
            if (result.IsFailure)
                return NotFound(new { error = result.Error });

            return Ok(new
            {
                combatId = result.Value.CombatState.CombatId,
                runId = result.Value.RunState.RunId,
                intents = result.Value.Intents
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get combat intents", combatId.ToString());
        }
    }

    [HttpPost("end")]
    public IActionResult End(Guid combatId, [FromBody] EndActivationRequest request)
    {
        try
        {
            var result = _activationCoordinator.EndCurrentActivation(combatId, request.RunId, request.ActorId);
            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(MapResult(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "end combat activation", combatId.ToString());
        }
    }

    [HttpPost("advance")]
    public IActionResult Advance(Guid combatId, [FromBody] AdvanceActivationRequest request)
    {
        try
        {
            var result = _activationCoordinator.AdvanceToNextActivation(combatId, request.RunId);
            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(MapResult(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "advance combat activation", combatId.ToString());
        }
    }

    [HttpPost("process-ai")]
    public IActionResult ProcessAi(Guid combatId, [FromBody] ProcessActivationAiRequest request)
    {
        try
        {
            var result = _activationCoordinator.ProcessCurrentAiActivation(combatId, request.RunId, request.GambitIds);
            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(MapResult(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "process combat activation AI", combatId.ToString());
        }
    }

    private static object MapResult(CombatActivationResult result)
    {
        return new
        {
            combatId = result.CombatState.CombatId,
            runId = result.RunState.RunId,
            activation = new
            {
                activeActorId = result.ActivationState.ActiveActorId,
                round = result.ActivationState.Round,
                activationIndex = result.ActivationState.ActivationIndex,
                activationNumber = result.ActivationState.ActivationNumber,
                activationOrder = result.ActivationState.ActivationOrder,
                completedActorIds = result.ActivationState.CompletedActorIds,
                waitingForInput = result.ActivationState.WaitingForInput,
                intents = result.ActivationState.Intents,
                rulesId = result.ActivationState.RulesId,
                startedAtUtc = result.ActivationState.StartedAtUtc
            },
            deck = new
            {
                hand = result.RunState.Deck.Hand,
                drawPile = result.RunState.Deck.DrawPile,
                discardPile = result.RunState.Deck.DiscardPile,
                exhaustPile = result.RunState.Deck.ExhaustPile,
                handCount = result.RunState.Deck.Hand.Count,
                drawPileCount = result.RunState.Deck.DrawPile.Count,
                discardPileCount = result.RunState.Deck.DiscardPile.Count,
                exhaustPileCount = result.RunState.Deck.ExhaustPile.Count
            },
            drawnCardIds = result.DrawnCardIds,
            discardedCardIds = result.DiscardedCardIds
        };
    }
}

public sealed record StartActivationRequest(Guid RunId, string? RulesId = null);
public sealed record EndActivationRequest(Guid RunId, string ActorId);
public sealed record AdvanceActivationRequest(Guid RunId);
public sealed record ProcessActivationAiRequest(Guid RunId, IReadOnlyList<string>? GambitIds = null);
