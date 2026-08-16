using API.Contracts;
using Core.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/content")]
public sealed class ContentController : ControllerBase
{
    private readonly IContentManifestProvider _manifests;
    private readonly IContentPublicationService _publications;

    public ContentController(
        IContentManifestProvider manifests,
        IContentPublicationService publications)
    {
        _manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
        _publications = publications ?? throw new ArgumentNullException(nameof(publications));
    }

    [HttpGet("revisions")]
    public async Task<IActionResult> GetRevisions(
        [FromQuery] string configName = "default",
        CancellationToken cancellationToken = default)
    {
        var current = _manifests.GetManifest(configName);
        if (current.IsFailure)
            return ContentProblem(StatusCodes.Status503ServiceUnavailable, current.Error);

        var published = await _publications.GetPublishedManifestsAsync(cancellationToken);
        var revisions = _manifests.GetKnownManifests()
            .Concat(published)
            .GroupBy(manifest => manifest.Revision, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(manifest => manifest.Revision, StringComparer.Ordinal)
            .ToArray();
        return Ok(new
        {
            currentRevision = current.Value.Revision,
            revisions
        });
    }

    [HttpGet("revisions/{revision}")]
    public async Task<IActionResult> GetRevision(
        string revision,
        [FromQuery] string configName = "default",
        CancellationToken cancellationToken = default)
    {
        var manifest = _manifests.GetByRevision(revision);
        if (manifest.IsSuccess)
            return Ok(manifest.Value);

        var current = _manifests.GetManifest(configName);
        if (current.IsSuccess && string.Equals(current.Value.Revision, revision, StringComparison.Ordinal))
            return Ok(current.Value);

        var published = await _publications.GetPublishedAsync(revision, cancellationToken);
        if (published.IsSuccess)
            return Ok(published.Value.Manifest);

        return ContentProblem(StatusCodes.Status404NotFound, $"Content revision not found: {revision}");
    }

    [HttpGet("{kind}")]
    public async Task<IActionResult> GetDefinitions(
        string kind,
        [FromQuery] string? revision = null,
        [FromQuery] string configName = "default",
        [FromQuery] string? tag = null,
        [FromQuery] string? after = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            return ContentProblem(StatusCodes.Status400BadRequest, "Limit must be between 1 and 500");
        var result = await _publications.GetDefinitionsAsync(kind, revision, configName, cancellationToken);
        if (result.IsFailure)
            return ContentProblem(StatusCodes.Status404NotFound, result.Error);

        var definitions = result.Value
            .Where(item => string.IsNullOrWhiteSpace(after) || string.CompareOrdinal(item.Key, after) > 0)
            .Where(item => string.IsNullOrWhiteSpace(tag) || HasTag(item.Value, tag))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Take(limit + 1)
            .ToArray();
        var hasMore = definitions.Length > limit;
        var items = definitions.Take(limit).Select(item => new
        {
            definitionId = item.Key,
            definition = item.Value
        }).ToArray();
        return Ok(new
        {
            kind,
            revision,
            items,
            returned = items.Length,
            nextCursor = hasMore ? items[^1].definitionId : null
        });
    }

    [HttpGet("{kind}/{definitionId}")]
    public async Task<IActionResult> GetDefinition(
        string kind,
        string definitionId,
        [FromQuery] string? revision = null,
        [FromQuery] string configName = "default",
        CancellationToken cancellationToken = default)
    {
        var result = await _publications.GetDefinitionsAsync(kind, revision, configName, cancellationToken);
        if (result.IsFailure)
            return ContentProblem(StatusCodes.Status404NotFound, result.Error);
        return result.Value.TryGetValue(definitionId, out var definition)
            ? Ok(new { kind, definitionId, revision, definition })
            : ContentProblem(StatusCodes.Status404NotFound, $"Content definition not found: {kind}/{definitionId}");
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate(
        [FromBody] ValidateContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ContentBundle? candidate = request.Bundle;
        if (candidate == null && !string.IsNullOrWhiteSpace(request.Revision))
        {
            var published = await _publications.GetPublishedAsync(request.Revision, cancellationToken);
            if (published.IsFailure)
                return ContentProblem(StatusCodes.Status404NotFound, published.Error);
            candidate = published.Value;
        }

        if (candidate == null)
            return ContentProblem(
                StatusCodes.Status400BadRequest,
                "Either bundle or revision must be supplied");

        var validation = _publications.Validate(candidate);
        return validation.IsValid
            ? Ok(validation)
            : UnprocessableEntity(validation);
    }

    private static bool HasTag(System.Text.Json.JsonElement definition, string tag)
    {
        return definition.ValueKind == System.Text.Json.JsonValueKind.Object &&
               definition.TryGetProperty("tags", out var tags) &&
               tags.ValueKind == System.Text.Json.JsonValueKind.Array &&
               tags.EnumerateArray().Any(item =>
                   item.ValueKind == System.Text.Json.JsonValueKind.String &&
                   string.Equals(item.GetString(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private ObjectResult ContentProblem(int statusCode, string detail)
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext,
            statusCode,
            statusCode switch
            {
                StatusCodes.Status400BadRequest => ApiErrorCodes.InvalidRequest,
                StatusCodes.Status404NotFound => ApiErrorCodes.ResourceNotFound,
                _ => ApiErrorCodes.DependencyUnavailable
            },
            statusCode switch
            {
                StatusCodes.Status400BadRequest => "Invalid content request",
                StatusCodes.Status404NotFound => "Content not found",
                _ => "Content unavailable"
            },
            detail);
        var result = new ObjectResult(problem) { StatusCode = statusCode };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}

public sealed record ValidateContentRequest(string? Revision, ContentBundle? Bundle = null);
