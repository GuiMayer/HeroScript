using API.Contracts;
using Core.Run.Branching;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/branches")]
public sealed class RunBranchController : BaseApiController
{
    private readonly IRunBranchService _branches;

    public RunBranchController(IRunBranchService branches, ILogger<RunBranchController> logger)
        : base(logger)
    {
        _branches = branches;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid runId,
        [FromBody] CreateRunBranchRequest request,
        CancellationToken cancellationToken)
    {
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
                result.Value.ParentRunId,
                result.Value.ParentCombatId,
                result.Value.ActiveEncounterId,
                result.Value.BranchFromSequence,
                result.Value.BranchKey,
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
        var branches = await _branches.ListAsync(runId, cancellationToken);
        return Ok(new { runId, branches, count = branches.Count });
    }

    [HttpGet("/api/v1/runs/{runId:guid}/branch-tree")]
    public async Task<IActionResult> GetTree(Guid runId, CancellationToken cancellationToken)
    {
        var tree = await _branches.GetTreeAsync(runId, cancellationToken);
        return tree.IsSuccess ? Ok(tree.Value) : ApiNotFound(tree.Error);
    }
}

public sealed record CreateRunBranchRequest(int SourceSequence, string BranchKey);
