using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Meta;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Integration")]
public sealed class SettingProfileTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public SettingProfileTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PublishedSettings_KeepIndependentRealRunHistoryForTheSamePlayer()
    {
        using var client = _factory.CreateClient();
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/settings");
        var player = "setting-profile-" + Guid.NewGuid().ToString("N");
        var ids = new Dictionary<string, Guid>();
        foreach (var settingId in new[] { "default", "ascendant" })
        {
            var setting = catalog.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("settingId").GetString() == settingId);
            var launch = setting.GetProperty("launch");
            using var response = await client.PostAsJsonAsync("/api/v1/runs", new
            {
                settingId, playerEntityId = player, seed = 424242UL,
                contentRevision = setting.GetProperty("currentRevision").GetString(),
                runDefinitionId = launch.GetProperty("runDefinitionId").GetString(),
                modeId = launch.GetProperty("modeId").GetString()
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ids[settingId] = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetGuid();
        }
        foreach (var (settingId, id) in ids)
        {
            var profile = await client.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{player}?settingId={settingId}");
            Assert.Equal(settingId, profile.GetProperty("settingId").GetString());
            Assert.Equal(1, profile.GetProperty("totalRuns").GetInt32());
            var summary = Assert.Single(profile.GetProperty("runs").EnumerateArray());
            Assert.Equal(id, summary.GetProperty("runId").GetGuid());
            Assert.Equal(settingId, summary.GetProperty("settingId").GetString());
        }
        Assert.NotEqual(ids["default"], ids["ascendant"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/stats")]
    [InlineData("/unlocks")]
    [InlineData("/achievements")]
    [InlineData("/runs")]
    public async Task EveryProfileSurface_RequiresSettingAndIsolatesDerivedProgress(string suffix)
    {
        var a = new RunState { RunId = Guid.NewGuid(), PlayerEntityId = "player", SettingId = "a",
            ConfigName = "a", Lifecycle = RunLifecycleState.Completed, Sequence = 5 };
        var b = a with { RunId = Guid.NewGuid(), SettingId = "b", ConfigName = "b", Lifecycle = RunLifecycleState.Active };
        var states = new[] { a, b }.ToDictionary(run => run.RunId);
        var commits = new Mock<IRunCommitReader>();
        commits.Setup(reader => reader.ListRunIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(states.Keys.ToArray());
        commits.Setup(reader => reader.LoadLatestStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => states[id]);
        using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPlayerProfileProjectionReader>();
            services.AddSingleton<IPlayerProfileProjectionReader>(new PlayerProfileProjectionReader(commits.Object));
        }));
        using var client = host.CreateClient();
        var path = $"/api/v1/profiles/player{suffix}";
        using var missing = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var blank = await client.GetAsync(path + "?settingId=%20");
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        using var responseA = await client.GetAsync(path + "?settingId=a");
        using var responseB = await client.GetAsync(path + "?settingId=b");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        var bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>();
        var bodyB = await responseB.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("a", bodyA.GetProperty("settingId").GetString());
        Assert.Equal("b", bodyB.GetProperty("settingId").GetString());
        Assert.NotEqual(bodyA.GetProperty("revision").GetString(), bodyB.GetProperty("revision").GetString());
        if (suffix == "/runs")
        {
            Assert.Equal(a.RunId, Assert.Single(bodyA.GetProperty("items").EnumerateArray()).GetProperty("runId").GetGuid());
            Assert.Equal(b.RunId, Assert.Single(bodyB.GetProperty("items").EnumerateArray()).GetProperty("runId").GetGuid());
            using var page = await client.GetAsync(path + $"?settingId=b&after={b.RunId}&limit=1");
            Assert.Empty((await page.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        }
        else if (suffix is "/unlocks" or "/achievements")
        {
            if (suffix == "/unlocks") Assert.Empty(bodyB.GetProperty("items").EnumerateArray());
            else Assert.DoesNotContain(bodyB.GetProperty("items").EnumerateArray(), item => item.GetString() == "first-completion");
            Assert.NotEmpty(bodyA.GetProperty("items").EnumerateArray());
        }
        else
        {
            Assert.Equal(1, bodyA.GetProperty("completedRuns").GetInt32());
            Assert.Equal(0, bodyB.GetProperty("completedRuns").GetInt32());
            Assert.Equal(1, bodyB.GetProperty("activeRuns").GetInt32());
        }
    }
}
