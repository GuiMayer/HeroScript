using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Services;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Run;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class GameplayEventQueryTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CausalDetailsRequireBothCapabilitiesBeforeHistoryRead(bool historical, bool hidden)
    {
        var run = new RunState { RunId = Guid.NewGuid() };
        var queries = new Mock<IRunQueryService>();
        queries.Setup(item => item.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));
        var access = new Mock<IToolAccessPolicy>();
        access.Setup(item => item.Allows(run, ToolCapabilities.TimelineInspectState)).Returns(historical);
        access.Setup(item => item.Allows(run, ToolCapabilities.CardInspectFull)).Returns(hidden);
        var store = new Mock<IRunCommitReader>(MockBehavior.Strict);
        var service = new GameplayEventQueryService(Mock.Of<IContentRuntimeResolver>(), queries.Object, store.Object, access.Object);
        Assert.Equal(403, (await service.ReadAsync(run.RunId, 1)).Status);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RepeatedCausalQueriesAreReadOnlyAndRejectWrongRevisionOrAggregate()
    {
        var run = new RunState { RunId = Guid.NewGuid(), ConfigName = "default", Determinism = DeterministicContext.Create(1, "revision") };
        var identity = new RunCommandIdentity(Guid.NewGuid(), "TEST", 0, 0);
        var observation = GameplayCommandObservation.Create(run, null, identity).Value;
        var started = GameplayActionObservation.Begin(observation, "TEST", "hero", "command.started", "command.input").Value;
        var batch = GameplayEventBatch.Capture(started)!;
        var commit = new RunCommit { RunId = run.RunId, Sequence = 1, RootCommand = identity, GameplayEvents = batch };
        var queries = new Mock<IRunQueryService>();
        queries.Setup(item => item.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));
        var access = new Mock<IToolAccessPolicy>();
        access.Setup(item => item.Allows(run, It.IsAny<string>())).Returns(true);
        var store = new Mock<IRunCommitReader>();
        store.Setup(item => item.LoadCommitAsync(run.RunId, 1, It.IsAny<CancellationToken>())).ReturnsAsync(() => commit);
        var service = new GameplayEventQueryService(Mock.Of<IContentRuntimeResolver>(), queries.Object, store.Object, access.Object);
        var before = CanonicalJson.ComputeHash(run);
        for (var i = 0; i < 5; i++) Assert.Equal(200, (await service.ReadAsync(run.RunId, 1, "revision")).Status);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.Equal(409, (await service.ReadAsync(run.RunId, 1, "other")).Status);
        commit = commit with { GameplayEvents = batch with { Scope = batch.Scope with { SettingId = "other" } } };
        Assert.Equal(409, (await service.ReadAsync(run.RunId, 1)).Status);
        Assert.Equal(400, (await service.ReadAsync(run.RunId, 0)).Status);
        Assert.Single(batch.Occurrences);
    }

    [Fact]
    public void CatalogChecksBothRevisionAndSettingAndReturnsEmbeddedSchema()
    {
        var runtime = ContentRuntime.Create(new ContentBundle { Manifest = new() { ConfigName = "default", Revision = "revision" } }).Value;
        var resolver = new Mock<IContentRuntimeResolver>();
        resolver.Setup(item => item.Resolve(It.IsAny<string>(), It.IsAny<string>())).Returns(Result<ContentRuntime>.Success(runtime));
        var service = new GameplayEventQueryService(resolver.Object, Mock.Of<IRunQueryService>(), Mock.Of<IRunCommitReader>(), Mock.Of<IToolAccessPolicy>());
        Assert.Equal(409, service.Catalog("ascendant", "revision").Status);
        Assert.Equal(409, service.Catalog("default", "forged").Status);
        var schema = JsonSerializer.SerializeToElement(service.Catalog("default", "revision", true).Value);
        Assert.Equal("object", schema.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal(200, service.Catalog("default", "revision").Status);
    }

    [Fact]
    public async Task RealRestDiscoversPublishedCatalogAndSchemaAndHasNoFactInjectionRoute()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/settings");
        var setting = settings.GetProperty("items")[0];
        var prefix = $"/api/v1/content/{setting.GetProperty("settingId").GetString()}/revisions/{setting.GetProperty("currentRevision").GetString()}";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(prefix + "/gameplay-events/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(prefix + "/gameplay-responses/schema")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync(prefix + "/gameplay-events/catalog", new { kind = "forged.fact" })).StatusCode);
    }
}
