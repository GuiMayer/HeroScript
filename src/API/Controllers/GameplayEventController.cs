using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class GameplayEventController(GameplayEventQueryService queries) : ControllerBase
{
    [HttpGet("content/{settingId}/revisions/{revision}/gameplay-events/catalog")]
    public IActionResult Catalog(string settingId, string revision) => Map(queries.Catalog(settingId, revision));

    [HttpGet("content/{settingId}/revisions/{revision}/gameplay-responses/schema")]
    public IActionResult Schema(string settingId, string revision) => Map(queries.Catalog(settingId, revision, schema: true));

    [HttpGet("runs/{runId:guid}/commits/{sequence:int}/gameplay-events")]
    public async Task<IActionResult> Inspect(Guid runId, int sequence, [FromQuery] string? contentRevision = null,
        CancellationToken cancellationToken = default) =>
        Map(await queries.ReadAsync(runId, sequence, contentRevision, cancellationToken).ConfigureAwait(false));

    private IActionResult Map(GameplayInspectionResult result) => result.Status == 200 ? Ok(result.Value)
        : Problem(statusCode: result.Status, title: "Gameplay event inspection failed", detail: result.Error);
}
