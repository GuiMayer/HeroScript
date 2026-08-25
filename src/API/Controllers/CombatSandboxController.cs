using API.Contracts;
using Core.Run.Sandbox;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/sandbox")]
public sealed class CombatSandboxController : BaseApiController
{
    private readonly ICombatSandboxService _sandbox;
    private readonly ICombatSandboxSnapshotService _snapshots;

    public CombatSandboxController(
        ICombatSandboxService sandbox,
        ICombatSandboxSnapshotService snapshots,
        ILogger<CombatSandboxController> logger)
        : base(logger)
    {
        _sandbox = sandbox;
        _snapshots = snapshots;
    }

    [HttpPost("scenarios/validate")]
    public IActionResult Validate(
        [FromBody] CombatScenarioDefinition scenario,
        [FromQuery] string configName = "default")
    {
        var compiled = _sandbox.Validate(scenario, configName);
        return compiled.IsSuccess
            ? Ok(new
            {
                scenario = compiled.Value.Scenario,
                compiled.Value.ScenarioHash,
                contentRevision = compiled.Value.ContentManifest.Revision,
                deck = compiled.Value.RunStart.StartingDeck,
                hero = compiled.Value.Hero,
                enemies = compiled.Value.Enemies
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
        var scenario = _sandbox.GetScenario(runId);
        return scenario.IsSuccess
            ? Ok(scenario.Value)
            : ApiNotFound(scenario.Error);
    }

    [HttpGet("runs/{runId:guid}/snapshot")]
    public IActionResult GetSnapshot(Guid runId)
    {
        var snapshot = _snapshots.Get(runId);
        return snapshot.IsSuccess
            ? Ok(snapshot.Value)
            : ApiNotFound(snapshot.Error);
    }
}
