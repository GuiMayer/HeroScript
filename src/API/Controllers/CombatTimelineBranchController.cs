using API.Contracts;
using API.Services;
using Core.Run;
using Core.Run.Branching;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/combats/{combatId:guid}/timeline/{sequence:int}/branches")]
public sealed class CombatTimelineBranchController : BaseApiController
{
    private readonly IRunQueryService _runs;
    private readonly IRunBranchService _branches;
    private readonly IToolAccessPolicy _toolAccess;

    public CombatTimelineBranchController(
        IRunQueryService runs,
        IRunBranchService branches,
        IToolAccessPolicy toolAccess,
        ILogger<CombatTimelineBranchController> logger)
        : base(logger)
    {
        _runs = runs;
        _branches = branches;
        _toolAccess = toolAccess;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid combatId,
        int sequence,
        [FromBody] CreateCombatTimelineBranchRequest request,
        CancellationToken cancellationToken)
    {
        var source = _runs.GetRunByCombat(combatId);
        if (source.IsFailure)
            return ApiNotFound(source.Error);
        if (!_toolAccess.Allows(source.Value, ToolCapabilities.BranchCreate))
        {
            return ApiProblem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "Tool capability denied",
                $"The active access profile and game mode do not allow '{ToolCapabilities.BranchCreate}'");
        }
        var branch = await _branches.CreateAsync(source.Value.RunId, sequence, request.BranchKey, cancellationToken);
        return branch.IsSuccess
            ? Ok(new
            {
                branch.Value.RunId,
                rootRunId = branch.Value.Lineage?.RootRunId,
                parentRunId = branch.Value.Lineage?.ParentRunId,
                sourceCombatId = branch.Value.Lineage?.SourceCombatId,
                branch.Value.ActiveEncounterId,
                sourceSequence = branch.Value.Lineage?.SourceSequence,
                sourceStateHash = branch.Value.Lineage?.SourceStateHash,
                branchKey = branch.Value.Lineage?.BranchKey,
                stateHash = Core.Determinism.CanonicalJson.ComputeHash(branch.Value)
            })
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Combat timeline branch rejected",
                branch.Error);
    }
}

public sealed record CreateCombatTimelineBranchRequest(string BranchKey);
