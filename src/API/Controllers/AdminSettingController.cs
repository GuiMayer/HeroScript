using API.Attributes;
using API.Contracts;
using Core.Content;
using Microsoft.AspNetCore.Mvc;
using Mods;

namespace API.Controllers;

[ApiController]
[AdminEndpoint]
[Route("api/v1/admin/settings")]
public sealed class AdminSettingController : BaseApiController
{
    private readonly ISettingCompiler _settings;
    private readonly IContentPublicationService _content;

    public AdminSettingController(
        ISettingCompiler settings,
        IContentPublicationService content,
        ILogger<AdminSettingController> logger)
        : base(logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default)
    {
        var catalog = await _settings.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return Ok(catalog);
    }

    [HttpGet("{settingId}")]
    public async Task<IActionResult> Get(
        string settingId,
        CancellationToken cancellationToken = default)
    {
        var result = await _settings.CompileAsync(settingId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Ok(result.Value)
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Setting compilation rejected",
                result.Error);
    }

    [HttpPost("{settingId}/validate")]
    public async Task<IActionResult> Validate(
        string settingId,
        CancellationToken cancellationToken = default)
    {
        var compiled = await _settings.CompileAsync(settingId, cancellationToken).ConfigureAwait(false);
        if (compiled.IsFailure)
        {
            return ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Setting validation failed",
                compiled.Error);
        }

        var validation = _content.Validate(compiled.Value.Bundle);
        return validation.IsValid
            ? Ok(new
            {
                settingId,
                revision = compiled.Value.Bundle.Manifest.Revision,
                validation,
                compiled.Value.Packages,
                compiled.Value.Provenance
            })
            : UnprocessableEntity(validation);
    }

    [HttpPost("{settingId}/drafts")]
    public async Task<IActionResult> CreateDraft(
        string settingId,
        CancellationToken cancellationToken = default)
    {
        var compiled = await _settings.CompileAsync(settingId, cancellationToken).ConfigureAwait(false);
        if (compiled.IsFailure)
        {
            return ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Setting draft rejected",
                compiled.Error);
        }

        var draft = await _content.CreateDraftAsync(compiled.Value.Bundle, cancellationToken)
            .ConfigureAwait(false);
        return draft.IsSuccess
            ? Created($"/api/v1/admin/content/drafts/{draft.Value.DraftId}", draft.Value)
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Setting draft rejected",
                draft.Error);
    }
}
