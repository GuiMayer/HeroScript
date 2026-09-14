using API.Contracts;
using API.Services;
using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/capabilities")]
public sealed class RunCapabilityController : BaseApiController
{
    private readonly IRunQueryService _runs;
    private readonly IToolAccessPolicy _toolAccess;

    public RunCapabilityController(
        IRunQueryService runs,
        IToolAccessPolicy toolAccess,
        ILogger<RunCapabilityController> logger)
        : base(logger)
    {
        _runs = runs;
        _toolAccess = toolAccess;
    }

    [HttpGet]
    public IActionResult Get(Guid runId)
    {
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);
        var granted = _toolAccess.EffectiveFor(run.Value);
        return Ok(new
        {
            runId,
            profile = _toolAccess.Snapshot.Profile,
            granted,
            denied = ToolCapabilities.All.Except(granted, StringComparer.Ordinal).OrderBy(item => item),
            modeCapabilityPolicyId = run.Value.ResolvedMode?.CapabilityPolicy.CapabilityPolicyId,
            replayPolicyId = run.Value.ResolvedMode?.ReplayPolicy.ReplayPolicyId,
            timelinePolicyId = run.Value.ResolvedMode?.TimelinePolicy.TimelinePolicyId
        });
    }
}
