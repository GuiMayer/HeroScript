using System.Net;
using HeroScript.Mcp;
using Xunit;

namespace HeroScript.Mcp.Tests;

public sealed class ReplayToolsTests
{
    [Fact]
    public async Task ReplayChecksHeadAndReturnsDivergence()
    {
        int heads = 0;
        using var client = new HeroScriptApiClient(new Uri("http://localhost:5271"), new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith("/verify", path);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isValid\":false,\"errors\":[\"sequence 2 mismatch\"]}") };
            }
            if (path.EndsWith("/timeline")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            heads++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"stateHash\":\"immutable\",\"contentRevision\":\"r\"}") };
        }));
        var result = await new EngineTools(client).VerifyReplay(default, runId: Guid.NewGuid());
        Assert.Equal(2, heads);
        Assert.Contains("sequence 2 mismatch", result.ToString());
        Assert.Contains("stateUnchanged = True", result.ToString());
        await Assert.ThrowsAsync<ArgumentException>(() => new EngineTools(client).VerifyReplay(default));
    }
}
