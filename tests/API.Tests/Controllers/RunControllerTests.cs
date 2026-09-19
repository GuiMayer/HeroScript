using API.Controllers;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.CardZones;
using Core.Run;
using Core.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Collections.Immutable;
using System.Text.Json;

namespace API.Tests.Controllers;

[Trait("Category", "Unit")]

public sealed class RunControllerTests
{
    private readonly Mock<IRunManager> _runManager = new();
    private readonly Mock<IRunCommitStore> _repository = new();
    private readonly RunController _controller;

    public RunControllerTests()
    {
        _controller = new RunController(_runManager.Object, _repository.Object, Mock.Of<ILogger<RunController>>());
    }

    [Fact]
    public void StartRun_ReturnsRunState()
    {
        var state = CreateRun();
        var expectedOptions = new RunStartOptions(
            "test", "default_run", "hero", null, "revision", "standard", SettingId: "test");
        _runManager
            .Setup(m => m.StartRun(expectedOptions))
            .Returns(Result<RunState>.Success(state));

        var result = _controller.StartRun(new StartRunRequest(
            "test", "default_run", "hero", ContentRevision: "revision", ModeId: "standard"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        var json = JsonSerializer.SerializeToElement(
            ok.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("stateHash").GetString()));
        Assert.Equal((ulong)0, json.GetProperty("step").GetUInt64());
        _runManager.Verify(
            m => m.StartRun(expectedOptions),
            Times.Once);
    }

    [Theory]
    [InlineData(null, "revision", "standard")]
    [InlineData("test", null, "standard")]
    [InlineData("test", "revision", null)]
    public void StartRun_RequiresSettingRevisionAndMode(
        string? settingId,
        string? contentRevision,
        string? modeId)
    {
        var result = _controller.StartRun(new StartRunRequest(
            settingId,
            "default_run",
            "hero",
            ContentRevision: contentRevision,
            ModeId: modeId));

        Assert.IsType<BadRequestObjectResult>(result);
        _runManager.Verify(manager => manager.StartRun(It.IsAny<RunStartOptions>()), Times.Never);
    }

    [Fact]
    public void GetCardZones_ReturnsAuthoredZonesForRun()
    {
        var state = CreateRun();
        _runManager.Setup(m => m.GetRun(state.RunId)).Returns(Result<RunState>.Success(state));

        var result = _controller.GetCardZones(state.RunId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public void RunProjection_RedactsHiddenActorCardsAndReadsPlayableActorZone()
    {
        var visibleId = Guid.Parse("10000000-0000-8000-8000-000000000001");
        var hiddenId = Guid.Parse("10000000-0000-8000-8000-000000000002");
        var prepared = new CardZoneAddress { ZoneId = "prepared", OwnerId = "hero" };
        var secret = new CardZoneAddress { ZoneId = "secret", OwnerId = "enemy" };
        var run = CreateRun() with
        {
            ResolvedMode = new ResolvedGameMode
            {
                CardZoneSystem = new CardZoneSystemDefinition
                {
                    CardZoneSystemId = "actor-graph",
                    Zones =
                    [
                        new CardZoneDefinition
                        {
                            ZoneId = "prepared", OwnerScope = CardZoneOwnerScope.Actor,
                            AllowsCardPlay = true, Visibility = new CardZoneVisibilityDefinition
                            {
                                Contents = CardZoneVisibility.Owner
                            }
                        },
                        new CardZoneDefinition
                        {
                            ZoneId = "secret", OwnerScope = CardZoneOwnerScope.Actor,
                            Visibility = new CardZoneVisibilityDefinition
                            {
                                Contents = CardZoneVisibility.None
                            }
                        }
                    ]
                }
            },
            Deck = new DeckState { Topology = new CardZoneTopologyState
            {
                Instances = new Dictionary<Guid, CardInstanceState>
                {
                    [visibleId] = new() { CardInstanceId = visibleId, DefinitionId = "shown", OwnerId = "hero" },
                    [hiddenId] = new() { CardInstanceId = hiddenId, DefinitionId = "hidden", OwnerId = "enemy" }
                },
                Zones = new Dictionary<string, CardZoneState>
                {
                    [prepared.Key] = new() { Address = prepared, InstanceIds = [visibleId] },
                    [secret.Key] = new() { Address = secret, InstanceIds = [hiddenId] }
                }
            } }
        };
        _runManager.Setup(manager => manager.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));

        var state = JsonSerializer.SerializeToElement(
            Assert.IsType<OkObjectResult>(_controller.GetState(run.RunId)).Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var zones = JsonSerializer.SerializeToElement(
            Assert.IsType<OkObjectResult>(_controller.GetCardZones(run.RunId)).Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("shown", Assert.Single(state.GetProperty("cardZones").GetProperty("zones")
                .EnumerateArray().SelectMany(zone => zone.GetProperty("cards").EnumerateArray()))
            .GetProperty("definitionId").GetString());
        var preparedZone = zones.GetProperty("zones").EnumerateArray()
            .Single(zone => zone.GetProperty("zoneId").GetString() == "prepared");
        Assert.Equal("shown", Assert.Single(preparedZone.GetProperty("cards").EnumerateArray())
            .GetProperty("definitionId").GetString());
        Assert.False(preparedZone.GetProperty("orderVisible").GetBoolean());
        Assert.IsType<NotFoundObjectResult>(_controller.GetCard(run.RunId, hiddenId));
        Assert.IsType<NotFoundObjectResult>(_controller.GetCardUpgradeOptions(run.RunId, hiddenId));
    }

    [Fact]
    public void GetState_ReturnsAllReconnectableSubstates()
    {
        var state = CreateRun() with
        {
            CardSelections = new[]
            {
                new CardSelectionState { SelectionInstanceId = Guid.NewGuid(), SelectionId = "reward" }
            }.ToImmutableArray(),
            Shops = new[]
            {
                new ShopState { ShopInstanceId = Guid.NewGuid(), ShopId = "shop" }
            }.ToImmutableArray(),
            Preparations = new[]
            {
                new PreparationState { PreparationInstanceId = Guid.NewGuid(), PreparationId = "camp" }
            }.ToImmutableArray()
        };
        _runManager.Setup(m => m.GetRun(state.RunId)).Returns(Result<RunState>.Success(state));

        var result = _controller.GetState(state.RunId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.SerializeToElement(
            ok.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Single(json.GetProperty("cardSelections").EnumerateArray());
        Assert.Single(json.GetProperty("shops").EnumerateArray());
        Assert.Single(json.GetProperty("preparations").EnumerateArray());
    }

    [Fact]
    public void MapReadEndpoints_ReturnPinnedMapAndLegalCommands()
    {
        var map = RunMapTransitions.Create(
        [
            new RunMapNodeDefinition { NodeId = "start", NextNodeIds = ["reward"] },
            new RunMapNodeDefinition
            {
                NodeId = "reward",
                Activity = new RunActivityDefinition
                {
                    Type = RunActivityType.CardSelection,
                    DefinitionId = "reward"
                }
            }
        ]).Value;
        var state = CreateRun() with { CurrentNodeId = "start", Map = map };
        _runManager.Setup(manager => manager.GetRun(state.RunId))
            .Returns(Result<RunState>.Success(state));
        _runManager.Setup(manager => manager.GetAvailableCommands(state.RunId))
            .Returns(Result<IReadOnlyList<RunAvailableCommand>>.Success(
                RunMapTransitions.GetAvailableCommands(state)));

        var mapResult = Assert.IsType<OkObjectResult>(_controller.GetMap(state.RunId));
        var commandsResult = Assert.IsType<OkObjectResult>(
            _controller.GetAvailableCommands(state.RunId));
        var mapJson = JsonSerializer.SerializeToElement(
            mapResult.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var commandsJson = JsonSerializer.SerializeToElement(
            commandsResult.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("start", mapJson.GetProperty("currentNodeId").GetString());
        Assert.Equal(2, mapJson.GetProperty("nodes").GetArrayLength());
        Assert.Empty(mapJson.GetProperty("legalNextNodeIds").EnumerateArray());
        Assert.Equal(
            RunCommandTypes.StartEncounter,
            commandsJson.GetProperty("commands")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task ListRuns_ReturnsPersistedRunsWithStablePagination()
    {
        var first = CreateRun() with
        {
            RunId = Guid.Parse("00000000-0000-0000-0000-000000000001")
        };
        var second = CreateRun() with
        {
            RunId = Guid.Parse("00000000-0000-0000-0000-000000000002")
        };
        _repository.Setup(repository => repository.ListRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { second.RunId, first.RunId });
        _repository.Setup(repository => repository.LoadLatestStateAsync(first.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);
        _repository.Setup(repository => repository.LoadLatestStateAsync(second.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(second);
        _runManager.Setup(manager => manager.GetRun(first.RunId)).Returns(Result<RunState>.Success(first));
        _runManager.Setup(manager => manager.GetRun(second.RunId)).Returns(Result<RunState>.Success(second));

        var result = await _controller.ListRuns(limit: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.SerializeToElement(
            ok.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var item = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal(first.RunId, item.GetProperty("runId").GetGuid());
        Assert.True(item.GetProperty("recoverable").GetBoolean());
        Assert.Equal(first.RunId, json.GetProperty("nextCursor").GetGuid());
    }

    [Fact]
    public async Task ListRuns_SkipsUnreadableStreamsAndReturnsValidRuns()
    {
        var unreadableId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var valid = CreateRun() with
        {
            RunId = Guid.Parse("00000000-0000-0000-0000-000000000002")
        };
        _repository.Setup(repository => repository.ListRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { unreadableId, valid.RunId });
        _repository.Setup(repository => repository.LoadLatestStateAsync(unreadableId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("invalid persisted hash"));
        _repository.Setup(repository => repository.LoadLatestStateAsync(valid.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(valid);
        _runManager.Setup(manager => manager.GetRun(valid.RunId)).Returns(Result<RunState>.Success(valid));

        var result = await _controller.ListRuns();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.SerializeToElement(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(valid.RunId, Assert.Single(json.GetProperty("items").EnumerateArray()).GetProperty("runId").GetGuid());
    }

    private static RunState CreateRun()
    {
        return new RunState
        {
            RunId = Guid.NewGuid(),
            ConfigName = "test",
            PlayerEntityId = "hero",
            ResourceState = new ResourceSet
            {
                OwnerId = "test",
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["credits"] = new()
                    {
                        ResourceId = "credits",
                        Current = 10,
                        Maximum = 100,
                        Definition = new ResourceDefinition
                        {
                            ResourceId = "credits",
                            DisplayName = "Credits",
                            DefaultMax = 100
                        }
                    }
                }
            },
            Deck = new DeckState()
        };
    }
}
