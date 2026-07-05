using API.Controllers;
using Core.Common;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Unit")]

public sealed class ShopControllerTests
{
    private readonly Mock<IRunManager> _runManager = new();
    private readonly ShopController _controller;

    public ShopControllerTests()
    {
        _controller = new ShopController(_runManager.Object, Mock.Of<ILogger<ShopController>>());
    }

    [Fact]
    public void Open_ReturnsShopItems()
    {
        var runId = Guid.NewGuid();
        var shop = new ShopState { RunId = runId, ShopId = "basic_shop" };
        _runManager.Setup(m => m.CreateShop(runId, "basic_shop")).Returns(Result<ShopState>.Success(shop));

        var result = _controller.Open(runId, new OpenShopRequest("basic_shop"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public void Buy_ReturnsBadRequestWhenPurchaseFails()
    {
        var runId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _runManager.Setup(m => m.BuyShopItem(runId, shopId, "x")).Returns(Result<ShopItemState>.Failure("no gold"));

        var result = _controller.Buy(runId, shopId, "x");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Reroll_DelegatesToRunManager()
    {
        var runId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var shop = new ShopState
        {
            RunId = runId,
            ShopInstanceId = shopId,
            ShopId = "basic_shop",
            CardPoolId = "basic_rewards",
            OfferCount = 3,
            RerollsUsed = 1,
            RerollCostGold = 15
        };
        _runManager.Setup(m => m.RerollShop(runId, shopId)).Returns(Result<ShopState>.Success(shop));

        var result = _controller.Reroll(runId, shopId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _runManager.Verify(m => m.RerollShop(runId, shopId), Times.Once);
    }
}
