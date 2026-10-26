using System.Net;
using System.Net.Http.Json;
using API.Controllers;
using Core.Common;
using Core.Run;
using Core.Run.DeckCompartments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class DeckCompartmentControllerTests
{
    [Fact]
    public void ControllerDelegatesAndMapsVersionConflict()
    {
        var service = new Mock<IDeckCompartmentQueryService>(MockBehavior.Strict);
        var id = Guid.NewGuid();
        var request = new DeckCompartmentPreviewRequest(4, 0, []);
        service.Setup(s => s.Preview(id, request)).Returns(Result<DeckCompartmentPreview>.Failure(RunCommandErrors.VersionConflictPrefix + " stale"));
        var controller = new DeckCompartmentController(service.Object, Mock.Of<ILogger<DeckCompartmentController>>());
        Assert.Equal(409, Assert.IsType<ObjectResult>(controller.Preview(id, request)).StatusCode);
        service.VerifyAll();
    }

    [Fact]
    public async Task RoutesAndOpenApiUseQueriesAndCanonicalCommandPayload()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        using var read = await client.GetAsync($"/api/v1/runs/{id}/deck-compartments");
        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        using var preview = await client.PostAsJsonAsync($"/api/v1/runs/{id}/deck-compartments/preview", new { expectedSequence = 0, expectedStep = 0, allocations = new object[0] });
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        var schema = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/v1/runs/{runId}/deck-compartments/preview", schema);
        Assert.Contains("ApplyDeckCompartmentsCommand", schema);
    }
}
