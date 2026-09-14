using API.Contracts;
using API.Services;
using Core.Run;
using Core.Run.Branching;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/branches")]
public sealed class RunBranchController : BaseApiController
{
    private readonly IRunBranchService _branches;
    private readonly IRunQueryService _runs;
    private readonly IToolAccessPolicy _toolAccess;

    public RunBranchController(
        IRunBranchService branches,
        IRunQueryService runs,
        IToolAccessPolicy toolAccess,
        ILogger<RunBranchController> logger)
        : base(logger)
    {
        _branches = branches;
        _runs = runs;
        _toolAccess = toolAccess;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid runId,
        [FromBody] CreateRunBranchRequest request,
        CancellationToken cancellationToken)
    {
        var denied = Authorize(runId, ToolCapabilities.BranchCreate);
        if (denied != null)
            return denied;
        var result = await _branches.CreateAsync(
            runId,
            request.SourceSequence,
            request.BranchKey,
            cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(new
            {
                result.Value.RunId,
                rootRunId = result.Value.Lineage?.RootRunId,
                parentRunId = result.Value.Lineage?.ParentRunId,
                sourceCombatId = result.Value.Lineage?.SourceCombatId,
                result.Value.ActiveEncounterId,
                sourceSequence = result.Value.Lineage?.SourceSequence,
                sourceStateHash = result.Value.Lineage?.SourceStateHash,
                branchKey = result.Value.Lineage?.BranchKey,
                result.Value.Sequence,
                result.Value.Determinism.Step,
                stateHash = Core.Determinism.CanonicalJson.ComputeHash(result.Value)
            });
        }
        if (result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return ApiNotFound(result.Error);
        return ApiProblem(
            StatusCodes.Status422UnprocessableEntity,
            ApiErrorCodes.RuleViolation,
            "Run branch rejected",
            result.Error);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid runId, CancellationToken cancellationToken)
    {
        var denied = Authorize(runId, ToolCapabilities.BranchRead);
        if (denied != null)
            return denied;
        var branches = await _branches.ListAsync(runId, cancellationToken);
        return Ok(new { runId, branches, count = branches.Count });
    }

    [HttpGet("/api/v1/runs/{runId:guid}/branch-tree")]
    public async Task<IActionResult> GetTree(Guid runId, CancellationToken cancellationToken)
    {
        var denied = Authorize(runId, ToolCapabilities.BranchRead);
        if (denied != null)
            return denied;
        var tree = await _branches.GetTreeAsync(runId, cancellationToken);
        return tree.IsSuccess ? Ok(tree.Value) : ApiNotFound(tree.Error);
    }

    private IActionResult? Authorize(Guid runId, string capability)
    {
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);
        return _toolAccess.Allows(run.Value, capability)
            ? null
            : ApiProblem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "Tool capability denied",
                $"The active access profile and game mode do not allow '{capability}'");
    }
}

public sealed record CreateRunBranchRequest(int SourceSequence, string BranchKey);
