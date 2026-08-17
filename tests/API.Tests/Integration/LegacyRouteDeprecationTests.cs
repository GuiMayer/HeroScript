using System.Net;
using API.Middleware;
using Xunit;

namespace API.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class LegacyRouteDeprecationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LegacyRouteDeprecationTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Legacy_route_announces_its_deprecation_without_changing_the_response()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LegacyRouteDeprecationMiddleware.DeprecationDate,
            response.Headers.GetValues("Deprecation").Single());
        Assert.Contains("</openapi/v1.json>; rel=\"successor-version\"",
            response.Headers.GetValues("Link").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versioned_route_does_not_receive_legacy_deprecation_metadata()
    {
        var response = await _client.GetAsync("/api/v1/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Deprecation"));
        Assert.False(response.Headers.Contains("Link"));
    }
}
