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
    public async Task GetActions_ReturnsRevisionedContentContract()
    {
        using var response = await _client.GetAsync("/api/v1/content/actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("actions", document.RootElement.GetProperty("kind").GetString());

        var item = Assert.Single(
            document.RootElement.GetProperty("items").EnumerateArray(),
            value => value.GetProperty("definitionId").GetString() == "basic_attack");
        var action = item.GetProperty("definition");
        Assert.Equal("Strike", action.GetProperty("displayName").GetString());
        Assert.Equal("BASIC_ATTACK", action.GetProperty("actionType").GetString());
        Assert.True(action.GetProperty("requiresTarget").GetBoolean());
        Assert.Contains("starter", action.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public async Task GetMissingAction_ReturnsProblemDetails()
    {
        using var response = await _client.GetAsync("/api/v1/content/actions/missing_action");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Equal("RESOURCE_NOT_FOUND", document.RootElement.GetProperty("code").GetString());
        Assert.Contains("missing_action", document.RootElement.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task GetResources_ReturnsRawJsonContract()
    {
        using var response = await _client.GetAsync("/api/v1/resources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);

        var resource = Assert.Single(document.RootElement.EnumerateArray(), item => item.GetProperty("resourceId").GetString() == "health");
        Assert.Equal("Health Points", resource.GetProperty("displayName").GetString());
        Assert.Equal("VITAL", resource.GetProperty("category").GetString());
        Assert.Equal(100, resource.GetProperty("defaultMax").GetSingle());
        Assert.Contains("vital", resource.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public async Task GetResourceByCategory_ReturnsFilteredContract()
    {
        using var response = await _client.GetAsync("/api/v1/resources/by-category/TACTICAL");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var resources = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, resources.Length);
        Assert.All(resources, resource => Assert.Equal("TACTICAL", resource.GetProperty("category").GetString()));
        Assert.Contains(resources, resource => resource.GetProperty("resourceId").GetString() == "energy");
        Assert.Contains(resources, resource => resource.GetProperty("resourceId").GetString() == "mana");
    }
}
