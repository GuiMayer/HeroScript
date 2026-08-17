using System.Net;
using Xunit;

namespace API.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class ApiVersioningTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiVersioningTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/run/start")]
    [InlineData("/api/combat/start")]
    public async Task Unversioned_routes_are_not_exposed(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
