using Core.Abstractions.Persistence;
using Core.Config;
using Core.Determinism;
using API.Contracts;
using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class SystemController : ControllerBase
{
    private readonly IConfigManager _configManager;
    private readonly IRunCommitReader _runRepository;
    private readonly IToolAccessPolicy _toolAccess;

    public SystemController(
        IConfigManager configManager,
        IRunCommitReader runRepository,
        IToolAccessPolicy toolAccess)
    {
        _configManager = configManager;
        _runRepository = runRepository;
        _toolAccess = toolAccess;
    }

    [HttpGet("health/live")]
    public IActionResult Live() => Ok(new { status = "healthy" });

    [HttpGet("health/ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> persistedRuns;
        try
        {
            persistedRuns = await _runRepository.ListRunIdsAsync(cancellationToken);
        }
        catch (Exception)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.DependencyUnavailable,
                "Service unavailable",
                "The run store is not ready.");
            var result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        return Ok(new
        {
            status = "ready",
            checks = new
            {
                content = new
                {
                    status = "ready",
                    currentConfig = _configManager.CurrentConfig,
                    defaultConfig = _configManager.DefaultConfig
                },
                runStore = new { status = "ready", persistedRuns = persistedRuns.Count }
            }
        });
    }

    [HttpGet("version")]
    public IActionResult Version()
    {
        var assemblyVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        return Ok(new
        {
            apiVersion = "1",
            engineVersion = DeterministicContext.CurrentEngineVersion,
            assemblyVersion
        });
    }

    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new
    {
        apiVersion = "1",
        capabilities = new[]
        {
            "deterministic-state",
            "content-manifests",
            "run-commits",
            "run-content-revision",
            "multi-setting-runs",
            "setting-scoped-profiles",
            "persistent-actor-resources",
            "calculated-random-inputs",
            "run-card-instances",
            "run-card-zones",
            "run-card-zone-tool-flows",
            "run-relics",
            "run-branches",
            "isolated-simulations",
            "profile-projections",
            "daily-challenge-proofs",
            "tcg-legality-reads",
            "sse-events"
        },
        toolAccess = _toolAccess.Snapshot
    });
}
