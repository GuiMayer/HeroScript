using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/shops")]
public sealed class ShopController : BaseApiController
{
    private readonly IRunManager _runManager;

    public ShopController(IRunManager runManager, ILogger<ShopController> logger)
        : base(logger)
    {
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
    }

    [HttpGet]
    public IActionResult List(Guid runId)
    {
        var run = _runManager.GetRun(runId);
        return run.IsFailure
            ? NotFound(new { error = run.Error })
            : Ok(run.Value.Shops.Select(MapShop).ToList());
    }

    [HttpGet("{shopInstanceId:guid}")]
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
            shop.RerollCosts,
            shop.Pricing,
            shop.Items
        };
    }
}
