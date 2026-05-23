using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/run")]
public sealed class RunController : BaseApiController
{
    private readonly IRunManager _runManager;

    public RunController(IRunManager runManager, ILogger<RunController> logger)
        : base(logger)
    {
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
    }

    [HttpPost("start")]
    public IActionResult StartRun([FromBody] StartRunRequest? request)
    {
        try
        {
            var result = _runManager.StartRun(
                request?.ConfigName ?? "default",
                request?.RunDefinitionId ?? "default_run",
                request?.PlayerEntityId ?? "player");

            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapRun(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "start run");
        }
    }

    [HttpGet("{runId:guid}/state")]
    public IActionResult GetState(Guid runId)
    {
        try
        {
            var result = _runManager.GetRun(runId);
            return result.IsFailure ? NotFound(new { error = result.Error }) : Ok(MapRun(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get run state", runId.ToString());
        }
    }

    [HttpGet("{runId:guid}/deck")]
    public IActionResult GetDeck(Guid runId)
    {
        try
        {
            var result = _runManager.GetRun(runId);
            return result.IsFailure ? NotFound(new { error = result.Error }) : Ok(MapDeck(result.Value.Deck));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get run deck", runId.ToString());
        }
    }

    [HttpGet("{runId:guid}/hand")]
    public IActionResult GetHand(Guid runId)
    {
        try
        {
            var result = _runManager.GetRun(runId);
            return result.IsFailure ? NotFound(new { error = result.Error }) : Ok(new { runId, hand = result.Value.Deck.Hand });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get run hand", runId.ToString());
        }
    }

    [HttpPost("{runId:guid}/draw")]
    public IActionResult Draw(Guid runId, [FromBody] CountRequest? request)
    {
        try
        {
            var result = _runManager.DrawCards(runId, request?.Count ?? 1);
            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return RunWithCards(runId, result.Value, "drawn");
        }
        catch (Exception ex)
        {
            return HandleException(ex, "draw run cards", runId.ToString());
        }
    }

    [HttpPost("{runId:guid}/discard")]
    public IActionResult Discard(Guid runId, [FromBody] CardIdsRequest request)
    {
        try
        {
            var result = _runManager.DiscardCards(runId, request.CardIds);
            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return RunWithCards(runId, result.Value, "discarded");
        }
        catch (Exception ex)
        {
            return HandleException(ex, "discard run cards", runId.ToString());
        }
    }

    [HttpPost("{runId:guid}/shuffle")]
    public IActionResult Shuffle(Guid runId)
    {
        try
        {
            var result = _runManager.ShuffleDiscardIntoDrawPile(runId);
            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            var run = _runManager.GetRun(runId);
            return run.IsFailure ? NotFound(new { error = run.Error }) : Ok(MapDeck(run.Value.Deck));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "shuffle run discard", runId.ToString());
        }
    }

    private IActionResult RunWithCards(Guid runId, IReadOnlyList<string> cards, string propertyName)
    {
        var run = _runManager.GetRun(runId);
        if (run.IsFailure)
            return NotFound(new { error = run.Error });

        return Ok(new Dictionary<string, object>
        {
            ["runId"] = runId,
            [propertyName] = cards,
            ["deck"] = MapDeck(run.Value.Deck)
        });
    }

    private static object MapRun(RunState run)
    {
        return new
        {
            run.RunId,
            run.ConfigName,
            run.PlayerEntityId,
            run.Gold,
            run.PowerPoints,
            run.CurrentNodeId,
            deck = MapDeck(run.Deck),
            run.Metadata
        };
    }

    private static object MapDeck(DeckState deck)
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
}

public sealed record StartRunRequest(string? ConfigName, string? RunDefinitionId, string? PlayerEntityId);
public sealed record CountRequest(int Count);
public sealed record CardIdsRequest(IReadOnlyList<string> CardIds);
