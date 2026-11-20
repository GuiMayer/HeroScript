using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
public sealed class McpSandboxController(IHostEnvironment environment, IConfiguration configuration, IToolAccessPolicy access) : ControllerBase
{
    [HttpGet("/api/v1/diagnostics/mcp-sandbox")]
    public IActionResult Status()
    {
        var root = configuration["McpSandbox:PersistenceRoot"];
        if (!environment.IsEnvironment("McpSandbox") || string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            return NotFound(new { error = "Not a dedicated MCP sandbox instance" });
        var sandboxRoot = Path.GetFullPath(root);
        var expected = new Dictionary<string, string> { ["RunStatePath"] = "runs", ["ContentStorePath"] = "content", ["OperationalTelemetryPath"] = "telemetry" };
        foreach (var pair in expected)
        {
            var configured = configuration[$"Persistence:{pair.Key}"];
            if (configured is null || !Path.IsPathFullyQualified(configured) ||
                !string.Equals(Path.GetFullPath(configured), Path.Combine(sandboxRoot, pair.Value), StringComparison.OrdinalIgnoreCase))
                return Conflict(new { error = "Sandbox persistence configuration is not isolated" });
        }
        if (!access.Allows(ToolCapabilities.ScenarioAuthor) || !access.Allows(ToolCapabilities.SimulationRun))
            return StatusCode(403, new { error = "Sandbox capabilities denied" });
        return Ok(new { isolated = true, environment = "McpSandbox" });
    }
}
