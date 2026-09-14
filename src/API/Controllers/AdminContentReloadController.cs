using API.Attributes;
using API.Contracts;
using API.Helpers;
using API.Models;
using Core.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[AdminEndpoint]
[Route("api/v1/admin/content")]
public sealed class AdminContentReloadController : BaseApiController
{
    private readonly IContentReloadService _reloads;
    private readonly ConfigReloadSettings _settings;

    public AdminContentReloadController(
        IContentReloadService reloads,
        ConfigReloadSettings settings,
        ILogger<AdminContentReloadController> logger)
        : base(logger)
    {
        _reloads = reloads ?? throw new ArgumentNullException(nameof(reloads));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    [HttpPost("reload")]
    [ProducesResponseType(typeof(ContentReloadReceipt), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Reload(
        [FromBody] ReloadContentRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return ApiProblem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "Content reload disabled",
                "Set AllowConfigReload=true (or the matching environment variable) to enable authoring reloads");
        }

        var settingId = request?.SettingId ?? "default";
        if (!ValidationHelper.IsValidConfigName(settingId))
        {
            return ApiProblem(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.InvalidRequest,
                "Invalid setting",
                "SettingId contains unsupported characters");
        }

        var result = await _reloads.ReloadAsync(settingId, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Content reload rejected",
                result.Error);
    }
}

public sealed record ReloadContentRequest(string? SettingId);
