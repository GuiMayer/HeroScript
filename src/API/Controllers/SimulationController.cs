using API.Contracts;
using API.Services;
using Core.Abstractions.Persistence;
using Core.Run.Branching;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/simulations")]
public sealed class SimulationController : BaseApiController
{
    private readonly IRunSimulationService _simulations;
    private readonly IRunCommitReader _commits;
    private readonly IToolAccessPolicy _toolAccess;

    public SimulationController(
        IRunSimulationService simulations,
        IRunCommitReader commits,
        IToolAccessPolicy toolAccess,
        ILogger<SimulationController> logger)
        : base(logger)
    {
        _simulations = simulations;
        _commits = commits;
        _toolAccess = toolAccess;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSimulationRequest request,
        CancellationToken cancellationToken)
    {
        var source = await _commits.LoadStateAsync(request.SourceRunId, request.SourceSequence, cancellationToken);
        if (source == null)
            return ApiNotFound($"Run commit not found: {request.SourceRunId}/{request.SourceSequence}");
        if (!_toolAccess.Allows(source, ToolCapabilities.SimulationRun))
            return ToolDenied(ToolCapabilities.SimulationRun);
        var result = await _simulations.ExecuteAsync(
            request.SourceRunId,
            request.SourceSequence,
            request.Commands,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Simulation rejected",
                result.Error);
    }

    [HttpGet("{simulationId:guid}")]
    public async Task<IActionResult> Get(Guid simulationId, CancellationToken cancellationToken)
    {
        var result = await _simulations.GetAsync(simulationId, cancellationToken);
        if (result.IsSuccess && !_toolAccess.Allows(result.Value.FinalState, ToolCapabilities.SimulationRun))
            return ToolDenied(ToolCapabilities.SimulationRun);
        return result.IsSuccess
            ? Ok(new
            {
                result.Value.SimulationId,
                result.Value.SourceRunId,
                result.Value.SourceSequence,
                result.Value.Status,
                result.Value.CommandsExecuted,
                result.Value.FinalStateHash,
                result.Value.Timeline
            })
            : ApiNotFound(result.Error);
    }

    [HttpGet("{simulationId:guid}/result")]
    public async Task<IActionResult> GetResult(Guid simulationId, CancellationToken cancellationToken)
    {
        var result = await _simulations.GetAsync(simulationId, cancellationToken);
        if (result.IsSuccess && !_toolAccess.Allows(result.Value.FinalState, ToolCapabilities.SimulationRun))
            return ToolDenied(ToolCapabilities.SimulationRun);
        return result.IsSuccess ? Ok(result.Value) : ApiNotFound(result.Error);
    }

    private IActionResult ToolDenied(string capability) => ApiProblem(
        StatusCodes.Status403Forbidden,
        ApiErrorCodes.Forbidden,
        "Tool capability denied",
        $"The active access profile and game mode do not allow '{capability}'");
}

public sealed record CreateSimulationRequest(
    Guid SourceRunId,
    int SourceSequence,
    IReadOnlyList<SimulationCommand> Commands);
