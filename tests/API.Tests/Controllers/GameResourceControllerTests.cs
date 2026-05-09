using System.Net;
using System.Net.Http.Json;
using API.Models.Resources;
using Xunit;

namespace API.Tests.Controllers;

public class GameResourceControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public GameResourceControllerTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAllResources_ReturnsSuccessAndResourceList()
    {
        // Act
        var response = await _client.GetAsync("/api/game-resources");

        // Assert
        response.EnsureSuccessStatusCode();
        var resources = await response.Content.ReadFromJsonAsync<List<ResourceSummaryDto>>();
        Assert.NotNull(resources);
        Assert.NotEmpty(resources);
    }

    [Fact]
    public async Task GetResource_WithValidId_ReturnsResourceDetails()
    {
        // Arrange - First get all resources to find a valid ID
        var allResourcesResponse = await _client.GetAsync("/api/game-resources");
        var resources = await allResourcesResponse.Content.ReadFromJsonAsync<List<ResourceSummaryDto>>();
        var firstResourceId = resources!.First().ResourceId;

        // Act
        var response = await _client.GetAsync($"/api/game-resources/{firstResourceId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var resource = await response.Content.ReadFromJsonAsync<ResourceDefinitionDto>();
        Assert.NotNull(resource);
        Assert.Equal(firstResourceId, resource.ResourceId);
    }

    [Fact]
    public async Task GetResource_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/game-resources/invalid_resource_id");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetResourcesByCategory_ReturnsFilteredResources()
    {
        // Act
        var response = await _client.GetAsync("/api/game-resources/by-category/VITAL");

        // Assert
        response.EnsureSuccessStatusCode();
        var resources = await response.Content.ReadFromJsonAsync<List<ResourceSummaryDto>>();
        Assert.NotNull(resources);
        Assert.All(resources, r => Assert.Equal("VITAL", r.Category));
    }

    [Fact]
    public async Task CreatePool_WithValidResourceId_ReturnsPool()
    {
        // Arrange
        var request = new CreatePoolRequest
        {
            ResourceId = "health",
            InitialCurrent = 50
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/game-resources/create-pool", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var pool = await response.Content.ReadFromJsonAsync<ResourcePoolDto>();
        Assert.NotNull(pool);
        Assert.Equal("health", pool.ResourceId);
        Assert.Equal(50, pool.Current);
    }

    [Fact]
    public async Task ValidateCost_WithSufficientResources_ReturnsCanAfford()
    {
        // Arrange
        var request = new ValidateCostRequest
        {
            ResourceId = "health",
            Cost = 30,
            CurrentAmount = 100
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/game-resources/validate-cost", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ValidateCostResponse>();
        Assert.NotNull(result);
        Assert.True(result.CanAfford);
    }

    [Fact]
    public async Task ValidateCost_WithInsufficientResources_ReturnsCannotAfford()
    {
        // Arrange
        var request = new ValidateCostRequest
        {
            ResourceId = "health",
            Cost = 150,
            CurrentAmount = 100
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/game-resources/validate-cost", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ValidateCostResponse>();
        Assert.NotNull(result);
        Assert.False(result.CanAfford);
        Assert.NotNull(result.Error);
    }
}
