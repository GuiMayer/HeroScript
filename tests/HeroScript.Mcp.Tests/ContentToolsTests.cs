using System.Net;
using HeroScript.Mcp;
using Xunit;

namespace HeroScript.Mcp.Tests;

public sealed class ContentToolsTests
{
    [Fact]
    public async Task OldSaveReportsMismatchAndOnlyReads()
    {
        using var client = new HeroScriptApiClient(new Uri("http://localhost:5271"), new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(path.Contains("revisions")
                ? "{\"currentRevision\":\"new\"}" : path.EndsWith("capabilities") ? "{\"granted\":[]}" : path.EndsWith("journal") ? throw new Exception("Journal should not be requested without capability") : "{\"settingId\":\"alisyum\",\"contentRevision\":\"old\",\"sequence\":4,\"stateHash\":\"hash\"}") };
        }));
        var result = await new EngineTools(client).Save(Guid.NewGuid(), default);
        Assert.Contains("requiresReview = True", result.ToString());
    }

    [Fact]
    public async Task ValidationPreservesStructuredInvalidContentResponse()
    {
        using var client = new HeroScriptApiClient(new Uri("http://localhost:5271"), new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"configName\":\"alisyum\"}") };
            Assert.Equal("/api/v1/content/validate", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("{\"isValid\":false,\"errors\":[\"Missing card\"]}") };
        }));
        var result = await new EngineTools(client).ValidateContent("alisyum", "revision", default);
        Assert.Contains("Missing card", result.ToString());
    }
}
