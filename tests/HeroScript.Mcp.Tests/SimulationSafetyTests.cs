using System.Net;
using System.Text.Json;
using HeroScript.Mcp;
using Xunit;

namespace HeroScript.Mcp.Tests;

public sealed class SimulationSafetyTests
{
    [Fact]
    public async Task SandboxDisabledAndUnmarkedApiDoNotGetMutationRequests()
    {
        var disabled = new SimulationTools(new SandboxSession(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.Branch(Guid.NewGuid(), 1, [], default));
        using var session = new SandboxSession(new HeroScriptApiClient(new Uri("http://localhost:5271"), new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.EndsWith("/mcp-sandbox", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SimulationTools(session).Branch(Guid.NewGuid(), 1, [], default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new SimulationTools(session).Encounter("default", "r", 1, JsonSerializer.SerializeToElement(new { }), default, 101));
    }
}
