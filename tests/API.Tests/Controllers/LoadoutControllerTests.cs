using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using API.Controllers;
using Core.Common;
using Core.Run.Loadouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class LoadoutControllerTests
{
    [Fact]
    public void ControllersDelegateQueriesAndMapStalePreviewToConflict()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<ILoadoutQueryService>(MockBehavior.Strict);
        var snapshot = new LoadoutSnapshot(runId, 7, 9, "revision", "hero", "actor:hero", new(), true, 0, ["hero"], [], ImmutableSortedDictionary<string, Guid>.Empty);
        service.Setup(value => value.Read(runId, "hero")).Returns(Result<LoadoutSnapshot>.Success(snapshot));
        var draft = new LoadoutPreviewRequest("hero", ImmutableSortedDictionary<string, Guid>.Empty, 6, 8);
        service.Setup(value => value.Preview(runId, draft)).Returns(Result<LoadoutPreview>.Failure("VERSION_CONFLICT: stale draft"));
        var controller = new LoadoutController(service.Object, Mock.Of<ILogger<LoadoutController>>());
        Assert.Same(snapshot, Assert.IsType<OkObjectResult>(controller.Read(runId, "hero")).Value);
        Assert.Equal(409, Assert.IsType<ObjectResult>(controller.Preview(runId, draft)).StatusCode);
        service.VerifyAll();
    }

    [Fact]
    public async Task RealRoutesAndOpenApiExposeOnlyQueriesAndCanonicalCommandPayload()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        using var read = await client.GetAsync($"/api/v1/runs/{runId}/loadout?actorId=hero");
        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        using var preview = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/loadout/preview", new { actorId = "hero", assignments = new { }, expectedSequence = 0, expectedStep = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        using var contract = await client.GetAsync("/openapi/v1.json");
        var openapi = await contract.Content.ReadAsStringAsync();
        Assert.True(contract.IsSuccessStatusCode, openapi);
        Assert.Contains("/api/v1/runs/{runId}/loadout", openapi);
        Assert.Contains("/api/v1/runs/{runId}/loadout/preview", openapi);
        Assert.Contains("ApplyLoadoutCommand", openapi);
    }
}
