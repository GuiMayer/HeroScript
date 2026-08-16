using API.Contracts;
using Core.Run.Branching;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/simulations")]
public sealed class SimulationController : BaseApiController
{
    private readonly IRunSimulationService _simulations;

    public SimulationController(
        IRunSimulationService simulations,
        ILogger<SimulationController> logger)
        : base(logger)
    {
        _simulations = simulations;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSimulationRequest request,
        CancellationToken cancellationToken)
    {
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
        return result.IsSuccess
            ? Ok(new
            {
                result.Value.SimulationId,
                result.Value.SourceRunId,
                result.Value.SourceSequence,
                result.Value.Status,
                result.Value.CommandsExecuted,
                result.Value.FinalStateHash
            })
            : ApiNotFound(result.Error);
    }

    [HttpGet("{simulationId:guid}/result")]
    public async Task<IActionResult> GetResult(Guid simulationId, CancellationToken cancellationToken)
    {
        var result = await _simulations.GetAsync(simulationId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiNotFound(result.Error);
    }
}

public sealed record CreateSimulationRequest(
    Guid SourceRunId,
    int SourceSequence,
    IReadOnlyList<SimulationCommand> Commands);
