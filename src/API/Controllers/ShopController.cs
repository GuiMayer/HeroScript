using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/run/{runId:guid}/shop")]
public sealed class ShopController : BaseApiController
{
    private readonly IRunManager _runManager;

    public ShopController(IRunManager runManager, ILogger<ShopController> logger)
        : base(logger)
    {
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
    }

    [HttpGet("/api/v1/runs/{runId:guid}/shops")]
    public IActionResult List(Guid runId)
    {
        var run = _runManager.GetRun(runId);
        return run.IsFailure
            ? NotFound(new { error = run.Error })
            : Ok(run.Value.Shops.Select(MapShop).ToList());
    }

    [HttpGet("/api/v1/runs/{runId:guid}/shops/{shopInstanceId:guid}")]
    public IActionResult Get(Guid runId, Guid shopInstanceId)
    {
        var run = _runManager.GetRun(runId);
        if (run.IsFailure)
            return NotFound(new { error = run.Error });

        var shop = run.Value.Shops.FirstOrDefault(item => item.ShopInstanceId == shopInstanceId);
        return shop == null
            ? NotFound(new { error = $"Shop not found: {shopInstanceId}" })
            : Ok(MapShop(shop));
    }

    [HttpPost("open")]
    public IActionResult Open(Guid runId, [FromBody] OpenShopRequest? request = null)
    {
        try
        {
            var result = _runManager.CreateShop(runId, request?.ShopId ?? "basic_shop");
            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapShop(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "open shop", runId.ToString());
        }
    }

    [HttpPost("{shopInstanceId:guid}/buy/{itemId}")]
    public IActionResult Buy(Guid runId, Guid shopInstanceId, string itemId)
    {
        try
        {
            var result = _runManager.BuyShopItem(runId, shopInstanceId, itemId);
            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(result.Value);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "buy shop item", runId.ToString());
        }
    }

    [HttpPost("{shopInstanceId:guid}/reroll")]
    public IActionResult Reroll(Guid runId, Guid shopInstanceId)
    {
        try
        {
            var result = _runManager.RerollShop(runId, shopInstanceId);
            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapShop(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "reroll shop", runId.ToString());
        }
    }

    private static object MapShop(ShopState shop)
    {
        return new
        {
            shop.ShopInstanceId,
            shop.RunId,
            shop.ShopId,
            shop.CardPoolId,
            shop.OfferCount,
            shop.RerollsUsed,
            shop.RerollCostGold,
            shop.Pricing,
            shop.Items
        };
    }
}

public sealed record OpenShopRequest(string? ShopId);
