using API.Controllers;
using Core.Common;
using Core.Run;
using Core.Run.DeckLoadouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class DeckLoadoutControllerTests
{
    [Fact]
    public async Task LiveContractPublishesTheCompositionProjectionAndReadOnlyPreview()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var body = await client.GetStringAsync("/openapi/v1.json");
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/v1/runs/{runId}/deck-loadout").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/runs/{runId}/deck-loadout/preview").TryGetProperty("post", out _));
        var output = Environment.GetEnvironmentVariable("HEROSCRIPT_OPENAPI_EXPORT_PATH");
        if (!string.IsNullOrEmpty(output)) await File.WriteAllTextAsync(output, body);
    }

    [Fact]
    public void DelegatesProjectionAndMapsConflictWithoutPerformingGameplay()
    {
        var id = Guid.NewGuid();
        var service = new Mock<IDeckLoadoutQueryService>(MockBehavior.Strict);
        var snapshot = new DeckLoadoutSnapshot(id, "test", 3, 5, "revision", "hero", false, false, "Disabled", null, null, []);
        var request = new DeckLoadoutPreviewRequest(2, 5, new("hero", "arbitrary"));
        service.Setup(s => s.Read(id)).Returns(Result<DeckLoadoutSnapshot>.Success(snapshot));
        service.Setup(s => s.Preview(id, request)).Returns(Result<DeckLoadoutPreview>.Failure(RunCommandErrors.VersionConflictPrefix + " stale"));
        var controller = new DeckLoadoutController(service.Object, Mock.Of<ILogger<DeckLoadoutController>>());
        Assert.Same(snapshot, Assert.IsType<OkObjectResult>(controller.Read(id)).Value);
        Assert.Equal(409, Assert.IsType<ObjectResult>(controller.Preview(id, request)).StatusCode);
        service.VerifyAll();
    }
}
