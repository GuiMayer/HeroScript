using API.Attributes;
using API.Contracts;
using Core.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[AdminEndpoint]
[Route("api/v1/admin/content/drafts")]
public sealed class AdminContentController : BaseApiController
{
    private readonly IContentPublicationService _content;

    public AdminContentController(
        IContentPublicationService content,
        ILogger<AdminContentController> logger)
        : base(logger)
    {
        _content = content;
    }

    [HttpGet("{draftId:guid}")]
    public async Task<IActionResult> Get(Guid draftId, CancellationToken cancellationToken = default)
    {
        var result = await _content.GetDraftAsync(draftId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiNotFound(result.Error);
    }

    [HttpPost("{draftId:guid}/validate")]
    public async Task<IActionResult> Validate(Guid draftId, CancellationToken cancellationToken = default)
    {
        var result = await _content.ValidateDraftAsync(draftId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{draftId:guid}/publish")]
    public async Task<IActionResult> Publish(
        Guid draftId,
        [FromBody] VersionedDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _content.PublishDraftAsync(draftId, request.ExpectedVersion, cancellationToken);
        if (result.IsSuccess)
            return Ok(new { revision = result.Value.Manifest.Revision, result.Value.Manifest });
        if (result.Error.StartsWith("VERSION_CONFLICT:", StringComparison.Ordinal))
            return ApiProblem(StatusCodes.Status409Conflict, ApiErrorCodes.VersionConflict, "Draft version conflict", result.Error["VERSION_CONFLICT:".Length..].Trim());
        return ApiProblem(StatusCodes.Status422UnprocessableEntity, ApiErrorCodes.RuleViolation, "Publication rejected", result.Error);
    }

}

public sealed record VersionedDraftRequest(int ExpectedVersion);
