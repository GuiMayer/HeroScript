using System.Net;
using HeroScript.Mcp;
using Xunit;

namespace HeroScript.Mcp.Tests;

public sealed class ApiClientTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("http://127.0.0.1:5271/path")]
    public void RejectsNonLocalOrNonOrigin(string origin) => Assert.Throws<ArgumentException>(() => McpSafetyPolicy.LocalUri(origin));

    [Fact]
    public async Task EngineStatusUsesReadOnlyRoute()
    {
        using var client = new HeroScriptApiClient(new Uri("http://127.0.0.1:5271"), new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/content/settings", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") };
        }));
        var result = await new EngineTools(client).Status(default);
        Assert.Contains("available = True", result.ToString());
    }

    [Fact]
    public async Task OfflineErrorHasRecoveryAdvice()
    {
        using var client = new HeroScriptApiClient(new Uri("http://localhost:5271"), new StubHandler(_ => throw new HttpRequestException()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new EngineTools(client).Status(default));
        Assert.Contains("Start the local engine", error.Message);
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(respond(request));
}
