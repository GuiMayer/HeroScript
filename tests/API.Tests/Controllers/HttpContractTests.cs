using System.Net;
using System.Text.Json;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Integration")]
public sealed class HttpContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HttpContractTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetActions_ReturnsRawJsonContract()
    {
        using var response = await _client.GetAsync("/api/v1/actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);

        var action = Assert.Single(document.RootElement.EnumerateArray(), item => item.GetProperty("actionId").GetString() == "basic_attack");
        Assert.Equal("Basic Attack", action.GetProperty("displayName").GetString());
        Assert.Equal("BASIC_ATTACK", action.GetProperty("actionType").GetString());
        Assert.True(action.GetProperty("requiresTarget").GetBoolean());
        Assert.True(action.GetProperty("effectCount").GetInt32() >= 0);
        Assert.Contains("starter", action.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public async Task GetMissingAction_ReturnsErrorEnvelope()
    {
        using var response = await _client.GetAsync("/api/v1/actions/missing_action");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Contains("missing_action", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetResources_ReturnsRawJsonContract()
    {
        using var response = await _client.GetAsync("/api/v1/resources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);

        var resource = Assert.Single(document.RootElement.EnumerateArray(), item => item.GetProperty("resourceId").GetString() == "health");
        Assert.Equal("Health", resource.GetProperty("displayName").GetString());
        Assert.Equal("VITAL", resource.GetProperty("category").GetString());
        Assert.Equal(30, resource.GetProperty("defaultMax").GetSingle());
        Assert.Contains("vital", resource.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public async Task GetResourceByCategory_ReturnsFilteredContract()
    {
        using var response = await _client.GetAsync("/api/v1/resources/by-category/TACTICAL");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var resource = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("energy", resource.GetProperty("resourceId").GetString());
        Assert.Equal("TACTICAL", resource.GetProperty("category").GetString());
    }
}
