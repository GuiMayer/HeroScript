using API.Contracts;
using API.Services;
using Core.Run;
using Core.Run.Sandbox;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/sandbox")]
public sealed class CombatSandboxController : BaseApiController
{
    private readonly ICombatSandboxService _sandbox;
    private readonly ICombatSandboxSnapshotService _snapshots;
    private readonly IToolAccessPolicy _toolAccess;
    private readonly IRunQueryService _runs;

    public CombatSandboxController(
        ICombatSandboxService sandbox,
        ICombatSandboxSnapshotService snapshots,
        IToolAccessPolicy toolAccess,
        IRunQueryService runs,
        ILogger<CombatSandboxController> logger)
        : base(logger)
    {
        _sandbox = sandbox;
        _snapshots = snapshots;
        _toolAccess = toolAccess;
        _runs = runs;
    }

    [HttpPost("scenarios/validate")]
    public IActionResult Validate(
        [FromBody] CombatScenarioDefinition scenario,
        [FromQuery] string configName = "default")
    {
        if (!_toolAccess.Allows(ToolCapabilities.ScenarioAuthor))
            return ToolDenied(ToolCapabilities.ScenarioAuthor);
        var compiled = _sandbox.Validate(scenario, configName);
        return compiled.IsSuccess
            ? Ok(new
            {
                scenario = compiled.Value.Scenario,
                compiled.Value.ScenarioHash,
                contentRevision = compiled.Value.ContentManifest.Revision,
                startingCards = compiled.Value.RunStart.StartingCards,
                actors = compiled.Value.Participants
            })
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Scenario rejected",
                compiled.Error);
    }

    [HttpPost("runs")]
    public IActionResult Launch(
        [FromBody] CombatScenarioDefinition scenario,
        [FromQuery] string configName = "default")
    {
        if (!_toolAccess.Allows(ToolCapabilities.ScenarioAuthor))
            return ToolDenied(ToolCapabilities.ScenarioAuthor);
        var launched = _sandbox.Launch(scenario, configName);
        return launched.IsSuccess
            ? Ok(new
            {
                launched.Value.ScenarioHash,
                launched.Value.Duplicate,
                run = launched.Value.Run,
                combat = launched.Value.Combat
            })
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Sandbox launch rejected",
                launched.Error);
    }

    [HttpGet("runs/{runId:guid}/scenario")]
    public IActionResult GetScenario(Guid runId)
    {
        var denied = AuthorizeRun(runId);
        if (denied != null)
            return denied;
        var scenario = _sandbox.GetScenario(runId);
        return scenario.IsSuccess
            ? Ok(scenario.Value)
            : ApiNotFound(scenario.Error);
    }

    [HttpGet("runs/{runId:guid}/snapshot")]
    public IActionResult GetSnapshot(Guid runId)
    {
        var denied = AuthorizeRun(runId);
        if (denied != null)
            return denied;
        var snapshot = _snapshots.Get(runId);
        return snapshot.IsSuccess
            ? Ok(snapshot.Value)
            : ApiNotFound(snapshot.Error);
    }

    private IActionResult ToolDenied(string capability) => ApiProblem(
        StatusCodes.Status403Forbidden,
        ApiErrorCodes.Forbidden,
        "Tool capability denied",
        $"The active access profile does not allow '{capability}'");

    private IActionResult? AuthorizeRun(Guid runId)
    {
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);
        return _toolAccess.Allows(run.Value, ToolCapabilities.ScenarioAuthor)
            ? null
            : ToolDenied(ToolCapabilities.ScenarioAuthor);
    }
}
