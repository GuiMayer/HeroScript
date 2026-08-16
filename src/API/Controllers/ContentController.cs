using API.Contracts;
using Core.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/content")]
public sealed class ContentController : ControllerBase
{
    private readonly IContentManifestProvider _manifests;

    public ContentController(IContentManifestProvider manifests)
    {
        _manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
    }

    [HttpGet("revisions")]
    public IActionResult GetRevisions([FromQuery] string configName = "default")
    {
        var current = _manifests.GetManifest(configName);
        if (current.IsFailure)
            return ContentProblem(StatusCodes.Status503ServiceUnavailable, current.Error);

        return Ok(new
        {
            currentRevision = current.Value.Revision,
            revisions = _manifests.GetKnownManifests()
        });
    }

    [HttpGet("revisions/{revision}")]
    public IActionResult GetRevision(string revision, [FromQuery] string configName = "default")
    {
        var manifest = _manifests.GetByRevision(revision);
        if (manifest.IsSuccess)
            return Ok(manifest.Value);

        var current = _manifests.GetManifest(configName);
        if (current.IsSuccess && string.Equals(current.Value.Revision, revision, StringComparison.Ordinal))
            return Ok(current.Value);

        return ContentProblem(StatusCodes.Status404NotFound, $"Content revision not found: {revision}");
    }

    private ObjectResult ContentProblem(int statusCode, string detail)
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext,
            statusCode,
            statusCode == StatusCodes.Status404NotFound
                ? ApiErrorCodes.ResourceNotFound
                : ApiErrorCodes.DependencyUnavailable,
            statusCode == StatusCodes.Status404NotFound
                ? "Content revision not found"
                : "Content unavailable",
            detail);
        var result = new ObjectResult(problem) { StatusCode = statusCode };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
