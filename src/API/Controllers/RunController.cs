using Core.Abstractions.Persistence;
using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/run")]
public sealed class RunController : BaseApiController
{
    private readonly IRunManager _runManager;
    private readonly IRunStateRepository _repository;

    public RunController(IRunManager runManager, IRunStateRepository repository, ILogger<RunController> logger)
        : base(logger)
    {
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
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
            run.Sequence,
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

    // Time-travel / snapshot endpoints

    [HttpGet("{runId:guid}/snapshots")]
    public async Task<IActionResult> ListSnapshots(Guid runId)
    {
        try
        {
            var snapshots = await _repository.ListSnapshotsAsync(runId);
            return Ok(new { runId, snapshots, count = snapshots.Count });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "list snapshots", runId.ToString());
        }
    }

    [HttpGet("{runId:guid}/snapshots/{sequence:int}")]
    public async Task<IActionResult> GetSnapshot(Guid runId, int sequence)
    {
        try
        {
            var state = await _repository.LoadAsync(runId, sequence);
            if (state == null)
                return NotFound(new { error = $"Snapshot {sequence} not found for run {runId}" });

            return Ok(MapRun(state));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get snapshot", $"{runId}/{sequence}");
        }
    }

    [HttpPost("{runId:guid}/undo")]
    public async Task<IActionResult> UndoToSnapshot([FromRoute] Guid runId, [FromBody] UndoRequest? request)
    {
        try
        {
            var targetSequence = request?.Sequence;
            
            RunState? targetState;
            if (targetSequence.HasValue)
            {
                targetState = await _repository.LoadAsync(runId, targetSequence.Value);
                if (targetState == null)
                    return NotFound(new { error = $"Snapshot {targetSequence} not found for run {runId}" });
            }
            else
            {
                // Undo to previous snapshot (current - 1)
                var currentRun = _runManager.GetRun(runId);
                if (currentRun.IsFailure)
                    return NotFound(new { error = currentRun.Error });

                var targetSeq = currentRun.Value.Sequence - 1;
                if (targetSeq < 0)
                    return BadRequest(new { error = "No previous snapshot available" });

                targetState = await _repository.LoadAsync(runId, targetSeq);
                if (targetState == null)
                    return NotFound(new { error = $"Previous snapshot {targetSeq} not found" });
            }

            // Restore state without triggering new snapshot
            var restoreResult = _runManager.RestoreState(targetState);
            if (restoreResult.IsFailure)
                return BadRequest(new { error = restoreResult.Error });

            return Ok(new
            {
                message = $"Run state restored to snapshot {targetState.Sequence}",
                state = MapRun(restoreResult.Value)
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "undo to snapshot", runId.ToString());
        }
    }
}

public sealed record StartRunRequest(string? ConfigName, string? RunDefinitionId, string? PlayerEntityId);
public sealed record CountRequest(int Count);
public sealed record CardIdsRequest(IReadOnlyList<string> CardIds);
public sealed record UndoRequest(int? Sequence);
