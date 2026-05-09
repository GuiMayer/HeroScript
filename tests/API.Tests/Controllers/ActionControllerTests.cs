using System.Net;
using System.Net.Http.Json;
using API.Models.Actions;
using Xunit;

namespace API.Tests.Controllers;

public class ActionControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ActionControllerTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAllActions_ReturnsSuccessAndActionList()
    {
        // Act
        var response = await _client.GetAsync("/api/action");

        // Assert
        response.EnsureSuccessStatusCode();
        var actions = await response.Content.ReadFromJsonAsync<List<ActionSummaryDto>>();
        Assert.NotNull(actions);
        Assert.NotEmpty(actions);
    }

    [Fact]
    public async Task GetAction_WithValidId_ReturnsActionDetails()
    {
        // Arrange - First get all actions to find a valid ID
        var allActionsResponse = await _client.GetAsync("/api/action");
        var actions = await allActionsResponse.Content.ReadFromJsonAsync<List<ActionSummaryDto>>();
        var firstActionId = actions!.First().ActionId;

        // Act
        var response = await _client.GetAsync($"/api/action/{firstActionId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var action = await response.Content.ReadFromJsonAsync<ActionDefinitionDto>();
        Assert.NotNull(action);
        Assert.Equal(firstActionId, action.ActionId);
    }

    [Fact]
    public async Task GetAction_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/action/invalid_action_id");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetActionsByType_ReturnsFilteredActions()
    {
        // Act
        var response = await _client.GetAsync("/api/action/by-type/ATTACK");

        // Assert
        response.EnsureSuccessStatusCode();
        var actions = await response.Content.ReadFromJsonAsync<List<ActionSummaryDto>>();
        Assert.NotNull(actions);
        Assert.All(actions, a => Assert.Equal("ATTACK", a.ActionType));
    }

    [Fact]
    public async Task ValidateAction_WithValidDefinition_ReturnsValid()
    {
        // Arrange
        var request = new ActionValidationRequest
        {
            Definition = new ActionDefinitionDto
            {
                ActionId = "test_action",
                DisplayName = "Test Action",
                ActionType = "ATTACK",
                Cooldown = 0,
                BaseDamage = 10,
                Tags = new List<string> { "test" },
                Costs = new ActionCostsDto
                {
                    Costs = new List<ResourceCostDto>
                    {
                        new() { ResourceId = "energy", Amount = 10, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/action/validate", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ActionValidationResponse>();
        Assert.NotNull(result);
        Assert.True(result.IsValid);
    }
}
