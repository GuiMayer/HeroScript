using API.Contracts;
using Core.Run;
using Core.Run.Branching;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/combats/{combatId:guid}/timeline/{sequence:int}/branches")]
public sealed class CombatTimelineBranchController : BaseApiController
{
    private readonly IRunManager _runs;
    private readonly IRunBranchService _branches;

    public CombatTimelineBranchController(
        IRunManager runs,
        IRunBranchService branches,
        ILogger<CombatTimelineBranchController> logger)
        : base(logger)
    {
        _runs = runs;
        _branches = branches;
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
        var branch = await _branches.CreateAsync(source.Value.RunId, sequence, request.BranchKey, cancellationToken);
        return branch.IsSuccess
            ? Ok(new
            {
                branch.Value.RunId,
                branch.Value.ParentRunId,
                branch.Value.ParentCombatId,
                branch.Value.ActiveEncounterId,
                branch.Value.BranchFromSequence,
                branch.Value.BranchKey,
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
