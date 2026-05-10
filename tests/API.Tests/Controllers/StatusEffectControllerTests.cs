using System.Net;
using System.Net.Http.Json;
using API.Models.StatusEffects;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace API.Tests.Controllers;

/// <summary>
/// Testes de integração para StatusEffectController
/// Testa os endpoints REST de ponta a ponta
/// </summary>
public class StatusEffectControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly Guid _combatId;
    private readonly Guid _targetId;

    public StatusEffectControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _combatId = Guid.NewGuid();
        _targetId = Guid.NewGuid();
    }

    [Fact]
    public async Task ApplyStatus_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new ApplyStatusRequest
        {
            StatusId = "burn",
            Stacks = 2,
            Duration = 3
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StatusEffectResponse>();
        Assert.NotNull(result);
        Assert.Equal("burn", result.StatusId);
        Assert.Equal(2, result.Stacks);
        Assert.Equal(3, result.Duration);
    }

    [Fact]
    public async Task ApplyStatus_WithInvalidStatusId_ReturnsBadRequest()
    {
        // Arrange
        var request = new ApplyStatusRequest
        {
            StatusId = "",
            Stacks = 1,
            Duration = 3
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetActiveStatus_ReturnsAllActiveEffects()
    {
        // Arrange - Apply multiple status effects
        var burnRequest = new ApplyStatusRequest { StatusId = "burn", Stacks = 2, Duration = 3 };
        var shieldRequest = new ApplyStatusRequest { StatusId = "shield", Stacks = 1, Duration = 2 };
        
        await _client.PostAsJsonAsync($"/api/combat/{_combatId}/entities/{_targetId}/status", burnRequest);
        await _client.PostAsJsonAsync($"/api/combat/{_combatId}/entities/{_targetId}/status", shieldRequest);

        // Act
        var response = await _client.GetAsync($"/api/combat/{_combatId}/entities/{_targetId}/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<StatusEffectResponse>>();
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task RemoveStatus_WithValidInstanceId_ReturnsSuccess()
    {
        // Arrange - Apply a status effect first
        var request = new ApplyStatusRequest { StatusId = "burn", Stacks = 2, Duration = 3 };
        var applyResponse = await _client.PostAsJsonAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status",
            request);
        var appliedStatus = await applyResponse.Content.ReadFromJsonAsync<StatusEffectResponse>();

        // Act
        var response = await _client.DeleteAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status/{appliedStatus!.InstanceId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddStacks_WithValidRequest_ReturnsUpdatedStatus()
    {
        // Arrange - Apply a status effect first
        var request = new ApplyStatusRequest { StatusId = "burn", Stacks = 2, Duration = 3 };
        var applyResponse = await _client.PostAsJsonAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status",
            request);
        var appliedStatus = await applyResponse.Content.ReadFromJsonAsync<StatusEffectResponse>();

        var addStacksRequest = new ModifyStacksRequest { Stacks = 3 };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status/{appliedStatus!.InstanceId}/add-stacks",
            addStacksRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StatusEffectResponse>();
        Assert.NotNull(result);
        Assert.Equal(5, result.Stacks); // 2 + 3
    }

    [Fact]
    public async Task RemoveStacks_WithValidRequest_ReturnsUpdatedStatus()
    {
        // Arrange - Apply a status effect first
        var request = new ApplyStatusRequest { StatusId = "burn", Stacks = 5, Duration = 3 };
        var applyResponse = await _client.PostAsJsonAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status",
            request);
        var appliedStatus = await applyResponse.Content.ReadFromJsonAsync<StatusEffectResponse>();

        var removeStacksRequest = new ModifyStacksRequest { Stacks = 2 };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status/{appliedStatus!.InstanceId}/remove-stacks",
            removeStacksRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StatusEffectResponse>();
        Assert.NotNull(result);
        Assert.Equal(3, result.Stacks); // 5 - 2
    }

    [Fact]
    public async Task RefreshDuration_WithValidRequest_ReturnsUpdatedStatus()
    {
        // Arrange - Apply a status effect first
        var request = new ApplyStatusRequest { StatusId = "burn", Stacks = 2, Duration = 2 };
        var applyResponse = await _client.PostAsJsonAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status",
            request);
        var appliedStatus = await applyResponse.Content.ReadFromJsonAsync<StatusEffectResponse>();

        var refreshRequest = new RefreshDurationRequest { Duration = 5 };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status/{appliedStatus!.InstanceId}/refresh",
            refreshRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StatusEffectResponse>();
        Assert.NotNull(result);
        Assert.Equal(5, result.Duration);
    }

    [Fact]
    public async Task TickDurations_DecrementsDurations()
    {
        // Arrange - Apply a status effect first
        var request = new ApplyStatusRequest { StatusId = "burn", Stacks = 2, Duration = 3 };
        await _client.PostAsJsonAsync($"/api/combat/{_combatId}/entities/{_targetId}/status", request);

        // Act
        var response = await _client.PostAsync(
            $"/api/combat/{_combatId}/entities/{_targetId}/status/tick",
            null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Verify duration was decremented
        var getResponse = await _client.GetAsync($"/api/combat/{_combatId}/entities/{_targetId}/status");
        var results = await getResponse.Content.ReadFromJsonAsync<List<StatusEffectResponse>>();
        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal(2, results[0].Duration); // 3 - 1
    }

    [Fact]
    public async Task RemoveAllStatus_RemovesAllEffects()
    {
        // Arrange - Apply multiple status effects
        var burnRequest = new ApplyStatusRequest { StatusId = "burn", Stacks = 2, Duration = 3 };
        var shieldRequest = new ApplyStatusRequest { StatusId = "shield", Stacks = 1, Duration = 2 };
        
        await _client.PostAsJsonAsync($"/api/combat/{_combatId}/entities/{_targetId}/status", burnRequest);
        await _client.PostAsJsonAsync($"/api/combat/{_combatId}/entities/{_targetId}/status", shieldRequest);

        // Act
        var response = await _client.DeleteAsync($"/api/combat/{_combatId}/entities/{_targetId}/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Verify all status effects were removed
        var getResponse = await _client.GetAsync($"/api/combat/{_combatId}/entities/{_targetId}/status");
        var results = await getResponse.Content.ReadFromJsonAsync<List<StatusEffectResponse>>();
        Assert.NotNull(results);
        Assert.Empty(results);
    }
}
