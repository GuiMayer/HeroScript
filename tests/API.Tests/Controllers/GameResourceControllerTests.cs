using API.Controllers;
using API.Models.Resources;
using Core.Common;
using Core.Resources;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using CoreLogger = Core.Logging.ILogger;

namespace API.Tests.Controllers;

public class GameResourceControllerTests
{
    private readonly Mock<IResourceManager> _resourceManager = new();
    private readonly GameResourceController _controller;

    public GameResourceControllerTests()
    {
        _controller = new GameResourceController(_resourceManager.Object, Mock.Of<CoreLogger>());
    }

    [Fact]
    public void GetAllResources_ReturnsSuccessAndResourceList()
    {
        _resourceManager.Setup(m => m.GetAllDefinitions()).Returns(new List<ResourceDefinition>
        {
            TestResource("health", ResourceCategory.VITAL)
        });

        var result = _controller.GetAllResources();

        var ok = Assert.IsType<OkObjectResult>(result);
        var resources = Assert.IsAssignableFrom<List<ResourceSummaryDto>>(ok.Value);
        var resource = Assert.Single(resources);
        Assert.Equal("health", resource.ResourceId);
        Assert.Equal("VITAL", resource.Category);
    }

    [Fact]
    public void GetResource_WithValidId_ReturnsResourceDetails()
    {
        _resourceManager.Setup(m => m.GetDefinition("energy"))
            .Returns(Result<ResourceDefinition>.Success(TestResource("energy", ResourceCategory.TACTICAL)));

        var result = _controller.GetResource("energy");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resource = Assert.IsType<ResourceDefinitionDto>(ok.Value);
        Assert.Equal("energy", resource.ResourceId);
        Assert.Equal("TACTICAL", resource.Category);
    }

    [Fact]
    public void GetResource_WithInvalidId_ReturnsNotFound()
    {
        _resourceManager.Setup(m => m.GetDefinition("invalid_resource_id"))
            .Returns(Result<ResourceDefinition>.Failure("Resource not found: invalid_resource_id"));

        var result = _controller.GetResource("invalid_resource_id");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetResourcesByCategory_ReturnsFilteredResources()
    {
        _resourceManager.Setup(m => m.GetDefinitionsByCategory(ResourceCategory.VITAL))
            .Returns(new List<ResourceDefinition> { TestResource("health", ResourceCategory.VITAL) });

        var result = _controller.GetResourcesByCategory("VITAL");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resources = Assert.IsAssignableFrom<List<ResourceSummaryDto>>(ok.Value);
        var resource = Assert.Single(resources);
        Assert.Equal("VITAL", resource.Category);
    }

    [Fact]
    public void CreatePool_WithValidResourceId_ReturnsPool()
    {
        _resourceManager.Setup(m => m.CreatePool("health", 50)).Returns(new ResourcePool
        {
            ResourceId = "health",
            Current = 50,
            Maximum = 100,
            Minimum = 0
        });

        var result = _controller.CreatePool(new CreatePoolRequest { ResourceId = "health", InitialCurrent = 50 });

        var ok = Assert.IsType<OkObjectResult>(result);
        var pool = Assert.IsType<ResourcePoolDto>(ok.Value);
        Assert.Equal("health", pool.ResourceId);
        Assert.Equal(50, pool.Current);
    }

    [Fact]
    public void ValidateCost_WithInsufficientResources_ReturnsCannotAfford()
    {
        _resourceManager.Setup(m => m.ValidateCost(It.IsAny<ResourcePool>(), 150))
            .Returns(Result.Failure("Insufficient resource"));

        var result = _controller.ValidateCost(new ValidateCostRequest
        {
            ResourceId = "health",
            Cost = 150,
            CurrentAmount = 100
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ValidateCostResponse>(ok.Value);
        Assert.False(response.CanAfford);
        Assert.NotNull(response.Error);
    }

    private static ResourceDefinition TestResource(string id, ResourceCategory category) => new()
    {
        ResourceId = id,
        DisplayName = id,
        ShortName = id[..Math.Min(2, id.Length)].ToUpperInvariant(),
        Category = category,
        DefaultMax = 100,
        Tags = new List<string> { "test" }
    };
}
