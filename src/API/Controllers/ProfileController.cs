using Core.Meta;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/profiles/{playerId}")]
public sealed class ProfileController : BaseApiController
{
    private readonly IPlayerProfileProjectionReader _profiles;

    public ProfileController(
        IPlayerProfileProjectionReader profiles,
        ILogger<ProfileController> logger)
        : base(logger)
    {
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> Get(string playerId, [FromQuery, Required] string settingId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.ReadAsync(playerId, settingId, cancellationToken);
        return Ok(profile);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(string playerId, [FromQuery, Required] string settingId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.ReadAsync(playerId, settingId, cancellationToken);
        return Ok(new
        {
            profile.PlayerId,
            profile.SettingId,
            profile.Revision,
            profile.TotalRuns,
            profile.CompletedRuns,
            profile.ActiveRuns
        });
    }

    [HttpGet("unlocks")]
    public async Task<IActionResult> GetUnlocks(string playerId, [FromQuery, Required] string settingId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.ReadAsync(playerId, settingId, cancellationToken);
        return Ok(new { profile.PlayerId, profile.SettingId, profile.Revision, items = profile.Unlocks });
    }

    [HttpGet("achievements")]
    public async Task<IActionResult> GetAchievements(string playerId, [FromQuery, Required] string settingId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.ReadAsync(playerId, settingId, cancellationToken);
        return Ok(new { profile.PlayerId, profile.SettingId, profile.Revision, items = profile.Achievements });
    }

    [HttpGet("runs")]
    public async Task<IActionResult> GetRuns(
        string playerId,
        [FromQuery, Required] string settingId,
        [FromQuery] Guid? after = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
            return ApiBadRequest("INVALID_REQUEST", "Invalid page size", "Limit must be between 1 and 100");
        var profile = await _profiles.ReadAsync(playerId, settingId, cancellationToken);
        var page = profile.Runs
            .Where(run => !after.HasValue || run.RunId.CompareTo(after.Value) > 0)
            .OrderBy(run => run.RunId)
            .Take(limit + 1)
            .ToArray();
        var hasMore = page.Length > limit;
        var items = page.Take(limit).ToArray();
        return Ok(new
        {
            profile.PlayerId,
            profile.SettingId,
            profile.Revision,
            items,
            nextCursor = hasMore ? items[^1].RunId : (Guid?)null
        });
    }
}
