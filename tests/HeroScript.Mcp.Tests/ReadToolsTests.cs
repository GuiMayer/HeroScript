using System.Net;
using HeroScript.Mcp;
using Xunit;

namespace HeroScript.Mcp.Tests;

public sealed class ReadToolsTests
{
    [Fact]
    public async Task ReadToolsOnlyIssueGetAndUseExpectedEndpoints()
    {
        var routes = new List<string>();
        using var client = new HeroScriptApiClient(new Uri("http://localhost:5271"), new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.PathAndQuery;
            routes.Add(path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(path.EndsWith("/settings")
                ? "{\"items\":[{\"settingId\":\"alisyum\"}]}"
                : path.Contains("/combats/") && !path.Contains("attributes") ? "{\"contentRevision\":\"r\",\"actors\":[{\"instanceId\":\"enemy\"}]}" : "{}") };
        }));
        var tools = new EngineTools(client);
        var id = Guid.NewGuid();
        await tools.Settings(default, "alisyum", limit: 1);
        await tools.Run(id, default);
        await tools.Combat(id, default, "enemy");
        await tools.Entity(id, "enemy", default);
        await tools.Card(id, id, default, "enemy", ["player"]);
        Assert.Contains($"/api/v1/runs/{id}/capabilities", routes);
        Assert.Contains($"/api/v1/combats/{id}/legal-actions?actorId=enemy", routes);
        Assert.Contains($"/api/v1/combats/{id}/actors/enemy/attributes", routes);
        Assert.Contains($"/api/v1/combats/{id}/cards/{id}/evaluation?actorId=enemy&targetIds=player", routes);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tools.Settings(default, limit: 101));
    }
}
