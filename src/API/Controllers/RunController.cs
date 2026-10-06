using Core.Abstractions.Persistence;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using Core.Determinism;
using Core.Config;
using API.Contracts;
using System.Text.Json;
using Core.CardZones;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs")]
public sealed class RunController : BaseApiController
{
    private readonly IRunManager _runManager;
    private readonly IRunCommitReader _repository;

    public RunController(
        IRunManager runManager,
        IRunCommitReader repository,
        ILogger<RunController> logger)
        : base(logger)
    {
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    [HttpPost("/api/v1/runs")]
    public IActionResult StartRun([FromBody] StartRunRequest? request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SettingId) ||
                string.IsNullOrWhiteSpace(request.ContentRevision) ||
                string.IsNullOrWhiteSpace(request.ModeId))
            {
                return BadRequest(new
                {
                    error = "settingId, contentRevision and modeId are required"
                });
            }
            var result = _runManager.StartRun(new RunStartOptions(
                request.SettingId,
                request.RunDefinitionId ?? "default_run",
                request.PlayerEntityId ?? "player",
                request.Seed,
                request.ContentRevision,
                request.ModeId,
                SettingId: request.SettingId));

            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapRun(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "start run");
        }
    }

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
            var commands = _runManager.GetAvailableCommands(runId);
            if (commands.IsFailure)
                return ApiBadRequest(ApiErrorCodes.InvalidOperation, "Available commands failed", commands.Error);

            return Ok(new
            {
                runId,
                run.Value.Sequence,
                run.Value.Determinism.Step,
                commands = commands.Value
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

                RunState? state;
                try
                {
                    state = await _repository.LoadLatestStateAsync(runId, cancellationToken);
                }
                catch (Exception exception) when (exception is InvalidOperationException or JsonException or IOException)
                {
                    _logger.LogWarning(exception, "Skipping unreadable run {RunId} while listing persisted runs", runId);
                    continue;
                }
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
                    state.Lifecycle,
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

    [HttpGet("/api/v1/runs/{runId:guid}/card-zones")]
    public IActionResult GetCardZones(Guid runId)
    {
        var result = _runManager.GetRun(runId);
        return result.IsFailure
            ? ApiNotFound(result.Error)
            : Ok(CardZoneReadModel.Project(result.Value));
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
        var snapshot = CardZoneReadModel.Project(result.Value);
        return VisibleCardIds(snapshot).Contains(cardInstanceId) &&
            result.Value.Deck.Topology.Instances.TryGetValue(cardInstanceId, out var card)
            ? Ok(MapCardInstance(result.Value.Deck, snapshot, card))
            : ApiNotFound($"Card instance not found: {cardInstanceId}");
    }

    [HttpGet("/api/v1/runs/{runId:guid}/cards/{cardInstanceId:guid}/upgrade-options")]
    public IActionResult GetCardUpgradeOptions(Guid runId, Guid cardInstanceId)
    {
        var result = _runManager.GetRun(runId);
        if (result.IsFailure)
            return ApiNotFound(result.Error);
        if (!VisibleCardIds(CardZoneReadModel.Project(result.Value)).Contains(cardInstanceId) ||
            !result.Value.Deck.Topology.Instances.TryGetValue(cardInstanceId, out var card))
            return ApiNotFound($"Card instance not found: {cardInstanceId}");
        var options = _runManager.GetCardTransformationOptions(runId, cardInstanceId);
        if (options.IsFailure) return ApiProblem(StatusCodes.Status503ServiceUnavailable,
            ApiErrorCodes.DependencyUnavailable, "Transformation options unavailable", options.Error);
        return Ok(new
        {
            runId,
            cardInstanceId,
            card.DefinitionId,
            contentRevision = result.Value.Determinism.ContentRevision,
            options = options.Value
        });
    }

    [HttpGet("/api/v1/runs/{runId:guid}/cards/{cardInstanceId:guid}/transformation-preview")]
    public IActionResult PreviewTransformation(Guid runId, Guid cardInstanceId,
        [FromQuery] CardTransformationOperation operation = CardTransformationOperation.Apply,
        [FromQuery] ulong? transformationId = null, [FromQuery] string? upgradeId = null)
    {
        var result = _runManager.AssessCardTransformation(runId, cardInstanceId, operation, transformationId, upgradeId);
        return result.IsSuccess ? Ok(result.Value) :
            ApiBadRequest(ApiErrorCodes.InvalidOperation, "Transformation preview unavailable", result.Error);
    }

    private static object MapRun(RunState run)
    {
        return new
        {
            run.RunId,
            run.ConfigName,
            run.SettingId,
            run.PlayerEntityId,
            run.PlayerEntity,
            run.ModeId,
            run.ResolvedMode,
            run.ChallengeId,
            rootRunId = run.Lineage?.RootRunId,
            parentRunId = run.Lineage?.ParentRunId,
            sourceCombatId = run.Lineage?.SourceCombatId,
            sourceSequence = run.Lineage?.SourceSequence,
            sourceStateHash = run.Lineage?.SourceStateHash,
            branchKey = run.Lineage?.BranchKey,
            resources = run.ResourceState.Resources.ToDictionary(
                pair => pair.Key,
                pair => new
                {
                    pair.Value.Current,
                    pair.Value.Minimum,
                    pair.Value.Maximum
                },
                StringComparer.OrdinalIgnoreCase),
            run.CurrentNodeId,
            run.Sequence,
            run.Lifecycle,
            seed = run.Determinism.Seed,
            run.Determinism.ContentRevision,
            run.ContentManifest,
            run.Determinism.EngineVersion,
            run.Determinism.Step,
            stateHash = CanonicalJson.ComputeHash(run),
            cardZones = CardZoneReadModel.Project(run),
            map = MapMap(run),
            run.ActiveEncounterId,
            run.Encounters,
            run.CardSelections,
            run.Shops,
            run.Preparations,
            dialogues = run.Dialogues.Select(dialogue => Core.Run.Dialogue.DialogueTransitions.View(run, dialogue)),
            run.NarrativeFlags,
            run.Relics,
            run.Modifiers,
            run.CompletedActivityNodeIds,
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
                node.Activity,
                node.CompletionPolicy,
                node.EntryEffects,
                node.ExitEffects,
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
        RunLifecycleState Lifecycle,
        ulong Step,
        string ContentRevision,
        string StateHash,
        bool Recoverable,
        string? RecoveryError);

    private static object MapCardInstance(DeckState deck, CardZoneSnapshot snapshot, CardInstanceState card)
    {
        var (zone, index) = FindCardZone(deck, card.CardInstanceId);
        var address = deck.Topology.FindZone(card.CardInstanceId)?.Address;
        var view = snapshot.Zones.FirstOrDefault(item => item.ZoneId == address?.ZoneId &&
            item.OwnerId == address?.OwnerId && item.ScopeId == address?.ScopeId);
        return new
        {
            card.CardInstanceId,
            card.DefinitionId,
            upgrades = CardTransformationLedger.Project(card.Upgrades).Value,
            transformationLedger = card.Upgrades,
            zone,
            zoneIndex = view?.OrderVisible == true ? index : -1
        };
    }

    private static HashSet<Guid> VisibleCardIds(CardZoneSnapshot snapshot) => snapshot.Zones
        .SelectMany(zone => zone.Cards.Select(card => card.CardInstanceId)).ToHashSet();

    private static (string Zone, int Index) FindCardZone(DeckState deck, Guid cardInstanceId)
    {
        var zone = deck.Topology.FindZone(cardInstanceId);
        if (zone == null) return ("unknown", -1);
        return (zone.Address.ZoneId, zone.InstanceIds.ToList().IndexOf(cardInstanceId));
    }

}

public sealed record StartRunRequest(
    string? SettingId,
    string? RunDefinitionId,
    string? PlayerEntityId,
    ulong? Seed = null,
    string? ContentRevision = null,
    string? ModeId = null);
