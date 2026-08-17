using API.Controllers;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Run;
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
    private readonly Mock<IRunStateRepository> _repository = new();
    private readonly RunController _controller;

    public RunControllerTests()
    {
        _controller = new RunController(_runManager.Object, _repository.Object, Mock.Of<ILogger<RunController>>());
    }

    [Fact]
    public void StartRun_ReturnsRunState()
    {
        var state = CreateRun();
        _runManager
            .Setup(m => m.StartRun(new RunStartOptions("test", "default_run", "hero", null, null)))
            .Returns(Result<RunState>.Success(state));

        var result = _controller.StartRun(new StartRunRequest("test", "default_run", "hero"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        var json = JsonSerializer.SerializeToElement(
            ok.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("stateHash").GetString()));
        Assert.Equal((ulong)0, json.GetProperty("step").GetUInt64());
        _runManager.Verify(m => m.StartRun(new RunStartOptions("test", "default_run", "hero", null, null)), Times.Once);
    }

    [Fact]
    public void GetHand_ReturnsHandForRun()
    {
        var state = CreateRun();
        _runManager.Setup(m => m.GetRun(state.RunId)).Returns(Result<RunState>.Success(state));

        var result = _controller.GetHand(state.RunId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
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
            new RunMapNodeDefinition { NodeId = "reward", NodeType = "card_selection" }
        ]).Value;
        var state = CreateRun() with { CurrentNodeId = "start", Map = map };
        _runManager.Setup(manager => manager.GetRun(state.RunId))
            .Returns(Result<RunState>.Success(state));

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
        _repository.Setup(repository => repository.LoadLatestAsync(first.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);
        _repository.Setup(repository => repository.LoadLatestAsync(second.RunId, It.IsAny<CancellationToken>()))
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

    private static RunState CreateRun()
    {
        return new RunState
        {
            RunId = Guid.NewGuid(),
            ConfigName = "test",
            PlayerEntityId = "hero",
            Gold = 10,
            Deck = new DeckState
            {
                DrawPile = new List<string> { "c" },
                Hand = new List<string> { "a", "b" }
            }
        };
    }
}
