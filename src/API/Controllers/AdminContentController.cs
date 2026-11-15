using API.Attributes;
using API.Contracts;
using API.Services;
using Core.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[AdminEndpoint]
[Route("api/v1/admin/content/drafts")]
public sealed class AdminContentController : BaseApiController
{
    private readonly IContentPublicationService _content;
    private readonly AuthoringWriteCoordinator _coordinator;
    private readonly CardAuthoringService _authoring;

    public AdminContentController(
        IContentPublicationService content,
        AuthoringWriteCoordinator coordinator,
        CardAuthoringService authoring,
        ILogger<AdminContentController> logger)
        : base(logger)
    {
        _content = content;
        _coordinator = coordinator;
        _authoring = authoring;
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
        CancellationToken cancellationToken = default) =>
        await _coordinator.ExecuteAsync(async () =>
    {
        if (_coordinator.Enabled)
        {
            if (_coordinator.RecoveryRequired)
                return ApiProblem(503, AuthoringErrorCodes.RecoveryRequired, "Authoring workspace recovering",
                    "Restart the engine to recover the interrupted write.");
            var draft = await _content.GetDraftAsync(draftId, cancellationToken);
            if (draft.IsSuccess && await _authoring.IsManagedAsync(draft.Value.ConfigName, cancellationToken))
                return ApiProblem(409, AuthoringErrorCodes.ChangeConflict, "Setting managed by source authoring",
                    "Use authoring changes or reload the current source files for this setting.");
        }
        var result = await _content.PublishDraftAsync(draftId, request.ExpectedVersion, cancellationToken);
        if (result.IsSuccess)
            return Ok(new { revision = result.Value.Manifest.Revision, result.Value.Manifest });
        if (result.Error.StartsWith("VERSION_CONFLICT:", StringComparison.Ordinal))
            return ApiProblem(StatusCodes.Status409Conflict, ApiErrorCodes.VersionConflict, "Draft version conflict", result.Error["VERSION_CONFLICT:".Length..].Trim());
        return ApiProblem(StatusCodes.Status422UnprocessableEntity, ApiErrorCodes.RuleViolation, "Publication rejected", result.Error);
    }, cancellationToken);

}

public sealed record VersionedDraftRequest(int ExpectedVersion);
