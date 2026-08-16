using Core.Abstractions.Persistence;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using Core.Determinism;
using Core.Config;

namespace API.Controllers;

[ApiController]
[Route("api/run")]
public sealed class RunController : BaseApiController
{
    private readonly IRunManager _runManager;
    private readonly IRunStateRepository _repository;
    private readonly IResourceCatalog<CardUpgradeDefinition>? _cardUpgrades;

    public RunController(
        IRunManager runManager,
        IRunStateRepository repository,
        ILogger<RunController> logger,
        IResourceCatalog<CardUpgradeDefinition>? cardUpgrades = null)
        : base(logger)
    {
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cardUpgrades = cardUpgrades;
    }

    [HttpPost("start")]
    [HttpPost("/api/v1/runs")]
    public IActionResult StartRun([FromBody] StartRunRequest? request)
    {
        try
        {
            var result = _runManager.StartRun(new RunStartOptions(
                request?.ConfigName ?? "default",
                request?.RunDefinitionId ?? "default_run",
                request?.PlayerEntityId ?? "player",
                request?.Seed,
                request?.ContentRevision,
                request?.ModeId));

            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapRun(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "start run");
        }
    }

    [HttpGet("{runId:guid}/state")]
    [HttpGet("/api/v1/runs/{runId:guid}")]
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

    [HttpGet("/api/v1/runs/{runId:guid}/map")]
    public IActionResult GetMap(Guid runId)
    {
        try
        {
            var result = _runManager.GetRun(runId);
            return result.IsFailure
                ? ApiNotFound(result.Error)
                : Ok(MapMap(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get run map", runId.ToString());
        }
    }

    [HttpGet("/api/v1/runs/{runId:guid}/available-commands")]
    public IActionResult GetAvailableCommands(Guid runId)
    {
        try
        {
            var run = _runManager.GetRun(runId);
            if (run.IsFailure)
                return ApiNotFound(run.Error);

            return Ok(new
            {
                runId,
                run.Value.Sequence,
                run.Value.Determinism.Step,
                commands = RunMapTransitions.GetAvailableCommands(run.Value)
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get available run commands", runId.ToString());
        }
    }

    [HttpGet("/api/v1/runs")]
    public async Task<IActionResult> ListRuns(
        [FromQuery] string? playerEntityId = null,
        [FromQuery] string? configName = null,
        [FromQuery] Guid? after = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
            return BadRequest(new { error = "Limit must be between 1 and 100" });

        try
        {
            var runIds = await _repository.ListRunIdsAsync(cancellationToken);
            var summaries = new List<RunSummaryResponse>();
            foreach (var runId in runIds.OrderBy(id => id))
            {
                if (after.HasValue && runId.CompareTo(after.Value) <= 0)
                    continue;

                var state = await _repository.LoadLatestAsync(runId, cancellationToken);
                if (state == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(playerEntityId) &&
                    !string.Equals(state.PlayerEntityId, playerEntityId, StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrWhiteSpace(configName) &&
                    !string.Equals(state.ConfigName, configName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var recovery = _runManager.GetRun(runId);
                summaries.Add(new RunSummaryResponse(
                    state.RunId,
                    state.Sequence,
                    state.ConfigName,
                    state.PlayerEntityId,
                    state.CurrentNodeId,
                    state.Determinism.Step,
                    state.Determinism.ContentRevision,
                    CanonicalJson.ComputeHash(state),
                    recovery.IsSuccess,
                    recovery.IsFailure ? recovery.Error : null));

                if (summaries.Count > limit)
                    break;
            }

            var hasMore = summaries.Count > limit;
            var items = summaries.Take(limit).ToList();
            return Ok(new
            {
                items,
                count = items.Count,
                nextCursor = hasMore ? items[^1].RunId : (Guid?)null
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "list persisted runs");
        }
    }

    [HttpGet("{runId:guid}/deck")]
    [HttpGet("/api/v1/runs/{runId:guid}/deck")]
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
    [HttpGet("/api/v1/runs/{runId:guid}/hand")]
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

    [HttpGet("/api/v1/runs/{runId:guid}/relics")]
    public IActionResult GetRelics(Guid runId)
    {
        var result = _runManager.GetRun(runId);
        return result.IsFailure
            ? ApiNotFound(result.Error)
            : Ok(new
            {
                runId,
                result.Value.Sequence,
                result.Value.Determinism.Step,
                relics = result.Value.Relics
                    .OrderBy(relic => relic.RelicInstanceId)
                    .ToArray()
            });
    }

    [HttpGet("/api/v1/runs/{runId:guid}/cards/{cardInstanceId:guid}")]
    public IActionResult GetCard(Guid runId, Guid cardInstanceId)
    {
        var result = _runManager.GetRun(runId);
        if (result.IsFailure)
            return ApiNotFound(result.Error);
        return result.Value.Deck.CardInstances.TryGetValue(cardInstanceId, out var card)
            ? Ok(MapCardInstance(result.Value.Deck, card))
            : ApiNotFound($"Card instance not found: {cardInstanceId}");
    }

    [HttpGet("/api/v1/runs/{runId:guid}/cards/{cardInstanceId:guid}/upgrade-options")]
    public IActionResult GetCardUpgradeOptions(Guid runId, Guid cardInstanceId)
    {
        var result = _runManager.GetRun(runId);
        if (result.IsFailure)
            return ApiNotFound(result.Error);
        if (!result.Value.Deck.CardInstances.TryGetValue(cardInstanceId, out var card))
            return ApiNotFound($"Card instance not found: {cardInstanceId}");
        if (_cardUpgrades == null)
        {
            return ApiProblem(
                StatusCodes.Status503ServiceUnavailable,
                API.Contracts.ApiErrorCodes.DependencyUnavailable,
                "Card upgrade catalog unavailable",
                "Card upgrade content catalog is not configured");
        }

        var options = _cardUpgrades.GetAll(result.Value.ConfigName)
            .Where(definition => definition.AppliesTo(card.DefinitionId))
            .Where(definition => card.Upgrades.Count(upgrade =>
                string.Equals(upgrade.UpgradeId, definition.UpgradeId, StringComparison.Ordinal))
                < Math.Max(1, definition.MaxApplications))
            .OrderBy(definition => definition.UpgradeId, StringComparer.Ordinal)
            .ToArray();
        return Ok(new
        {
            runId,
            cardInstanceId,
            card.DefinitionId,
            contentRevision = result.Value.Determinism.ContentRevision,
            options
        });
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
            run.ModeId,
            run.ChallengeId,
            run.ParentRunId,
            run.BranchFromSequence,
            run.BranchKey,
            run.Gold,
            run.PowerPoints,
            run.CurrentNodeId,
            run.Sequence,
            seed = run.Determinism.Seed,
            run.Determinism.ContentRevision,
            run.ContentManifest,
            run.Determinism.EngineVersion,
            run.Determinism.Step,
            stateHash = CanonicalJson.ComputeHash(run),
            deck = MapDeck(run.Deck),
            map = MapMap(run),
            run.ActiveEncounterId,
            run.Encounters,
            run.CardSelections,
            run.Shops,
            run.Preparations,
            run.Relics,
            run.Metadata
        };
    }

    private static object MapMap(RunState run)
    {
        var visited = run.Map.VisitedNodeIds.ToHashSet(StringComparer.Ordinal);
        var resolved = run.Map.ResolvedNodeIds.ToHashSet(StringComparer.Ordinal);
        return new
        {
            run.RunId,
            run.CurrentNodeId,
            visitedNodeIds = run.Map.VisitedNodeIds,
            resolvedNodeIds = run.Map.ResolvedNodeIds,
            legalNextNodeIds = RunMapTransitions.GetLegalNextNodeIds(run),
            nodes = run.Map.Nodes.Select(node => new
            {
                node.NodeId,
                node.NodeType,
                node.NextNodeIds,
                visited = visited.Contains(node.NodeId),
                resolved = resolved.Contains(node.NodeId),
                node.Metadata
            }).ToList()
        };
    }

    private sealed record RunSummaryResponse(
        Guid RunId,
        int Sequence,
        string ConfigName,
        string PlayerEntityId,
        string? CurrentNodeId,
        ulong Step,
        string ContentRevision,
        string StateHash,
        bool Recoverable,
        string? RecoveryError);

    private static object MapDeck(DeckState deck)
    {
        return new
        {
            deck.DrawPile,
            deck.Hand,
            deck.DiscardPile,
            deck.ExhaustPile,
            cardInstances = deck.CardInstances.Values
                .OrderBy(card => card.CardInstanceId)
                .Select(card => MapCardInstance(deck, card))
                .ToArray(),
            counts = new
            {
                drawPile = deck.DrawPile.Count,
                hand = deck.Hand.Count,
                discardPile = deck.DiscardPile.Count,
                exhaustPile = deck.ExhaustPile.Count
            }
        };
    }

    private static object MapCardInstance(DeckState deck, CardInstanceState card)
    {
        var (zone, index) = FindCardZone(deck, card.CardInstanceId);
        return new
        {
            card.CardInstanceId,
            card.DefinitionId,
            card.Upgrades,
            zone,
            zoneIndex = index
        };
    }

    private static (string Zone, int Index) FindCardZone(DeckState deck, Guid cardInstanceId)
    {
        var index = deck.DrawPileInstanceIds.ToList().IndexOf(cardInstanceId);
        if (index >= 0)
            return ("draw", index);
        index = deck.HandInstanceIds.ToList().IndexOf(cardInstanceId);
        if (index >= 0)
            return ("hand", index);
        index = deck.DiscardPileInstanceIds.ToList().IndexOf(cardInstanceId);
        if (index >= 0)
            return ("discard", index);
        index = deck.ExhaustPileInstanceIds.ToList().IndexOf(cardInstanceId);
        return index >= 0 ? ("exhaust", index) : ("unknown", -1);
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

public sealed record StartRunRequest(
    string? ConfigName,
    string? RunDefinitionId,
    string? PlayerEntityId,
    ulong? Seed = null,
    string? ContentRevision = null,
    string? ModeId = null);
public sealed record CountRequest(int Count);
public sealed record CardIdsRequest(IReadOnlyList<string> CardIds);
public sealed record UndoRequest(int? Sequence);
