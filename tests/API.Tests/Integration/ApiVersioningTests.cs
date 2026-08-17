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

        Assert.Contains(response.StatusCode, new[]
        {
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed
        });
    }

    [Theory]
    [InlineData("/api/v1/runs/00000000-0000-0000-0000-000000000001/draw")]
    [InlineData("/api/v1/runs/00000000-0000-0000-0000-000000000001/discard")]
    [InlineData("/api/v1/runs/00000000-0000-0000-0000-000000000001/shops/open")]
    [InlineData("/api/v1/runs/00000000-0000-0000-0000-000000000001/card-selections/start")]
    [InlineData("/api/v1/runs/00000000-0000-0000-0000-000000000001/preparations/start")]
    public async Task Direct_run_mutation_routes_are_not_exposed(string path)
    {
        using var response = await _client.PostAsync(path, content: null);

        Assert.Contains(response.StatusCode, new[]
        {
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed
        });
    }

    [Theory]
    [InlineData("/api/v1/actions")]
    [InlineData("/api/v1/entities/definitions")]
    [InlineData("/api/v1/statuses/definitions")]
    [InlineData("/api/v1/gambits/definitions")]
    public async Task Direct_content_mutation_routes_are_not_exposed(string path)
    {
        using var response = await _client.PostAsync(path, content: null);

        Assert.Contains(response.StatusCode, new[]
        {
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed
        });
    }
}
