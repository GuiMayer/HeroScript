using Core.Common;
using Core.Config;
using Core.Math;
using Core.Resources;
using Core.StatusEffects;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.StatusEffects;

public sealed class StatusEffectManagerTests : IDisposable
{
    private readonly StatusEffectManager _manager;
    private readonly Mock<IRuntimeFormulaEvaluator> _formulaEvaluator = new();

    public StatusEffectManagerTests()
    {
        _manager = CreateManagerWithJson(TestStatusDefinitionsJson, "test", _formulaEvaluator.Object);
        var loadResult = _manager.LoadStatusDefinitions("test");

        Assert.True(loadResult.IsSuccess, loadResult.IsFailure ? loadResult.Error : null);
    }

    [Fact]
    public void LoadStatusDefinitions_WithStringEnums_LoadsDefinitions()
    {
        var result = _manager.GetDefinition("burning");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(StatusEffectType.BURNING, result.Value!.Type);
        Assert.Equal(StatusEffectBehavior.DAMAGE_OVER_TIME, result.Value.Behavior);
        Assert.Equal(StatusEffectTiming.END_OF_TURN, result.Value.Timing);
    }

    [Fact]
    public void ApplyStatus_WithExistingStatus_ClampsStacksAndDoesNotDuplicate()
    {
        const string targetId = "enemy_1";

        var first = _manager.ApplyStatus(targetId, "burning", stacks: 2);
        var second = _manager.ApplyStatus(targetId, "burning", stacks: 3);
        var active = _manager.GetActiveStatus(targetId);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.True(active.IsSuccess, active.IsFailure ? active.Error : null);
        Assert.Single(active.Value!);
        Assert.Equal(4, second.Value!.Stacks);
        Assert.Equal(first.Value!.InstanceId, second.Value.InstanceId);
    }

    [Fact]
    public async Task ApplyStatus_ConcurrentCalls_PublishOneAtomicStatusSnapshot()
    {
        const string targetId = "concurrent_enemy";
        var tasks = Enumerable.Range(1, 32)
            .Select(index => Task.Run(() => _manager.ApplyStatus(
                targetId,
                "burning",
                new Guid(index, 0, 0, new byte[8]),
                DateTime.UnixEpoch,
                stacks: 1)))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var active = _manager.GetActiveStatus(targetId);

        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        var status = Assert.Single(active.Value!);
        Assert.Equal(4, status.Stacks);
    }

    [Fact]
    public void ProcessStatusEffects_ForMatchingTiming_ReturnsFormulaValue()
    {
        const string targetId = "enemy_1";
        var apply = _manager.ApplyStatus(targetId, "burning", stacks: 2);
        _formulaEvaluator
            .Setup(m => m.Evaluate("stacks * 3", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Result<float>.Success(6f));

        var result = _manager.ProcessStatusEffects(targetId, StatusEffectTiming.END_OF_TURN, currentTurn: 1);

        Assert.True(apply.IsSuccess, apply.IsFailure ? apply.Error : null);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var tick = Assert.Single(result.Value!.TickResults);
        Assert.Equal("burning", tick.StatusId);
        Assert.Equal(StatusEffectType.BURNING, tick.Type);
        Assert.Equal(6f, tick.Value);
    }

    [Fact]
    public void TickDurations_DecrementsFiniteStatusesAndKeepsPermanentStatuses()
    {
        const string targetId = "enemy_1";
        _manager.ApplyStatus(targetId, "burning", duration: 2);
        _manager.ApplyStatus(targetId, "strength", stacks: 1);

        var firstTick = _manager.TickDurations(targetId);
        var afterFirstTick = _manager.GetActiveStatus(targetId).Value!;
        var secondTick = _manager.TickDurations(targetId);
        var afterSecondTick = _manager.GetActiveStatus(targetId).Value!;

        Assert.True(firstTick.IsSuccess, firstTick.IsFailure ? firstTick.Error : null);
        Assert.Contains(afterFirstTick, s => s.StatusId == "burning" && s.Duration == 1);
        Assert.Contains(afterFirstTick, s => s.StatusId == "strength" && s.Duration == -1);
        Assert.True(secondTick.IsSuccess, secondTick.IsFailure ? secondTick.Error : null);
        Assert.DoesNotContain(afterSecondTick, s => s.StatusId == "burning");
        Assert.Contains(afterSecondTick, s => s.StatusId == "strength" && s.Duration == -1);
    }

    [Fact]
    public void GetPipelineModifiers_UsesModifierFormulaAndAccumulatesByKey()
    {
        const string targetId = "hero_1";
        _manager.ApplyStatus(targetId, "strength", stacks: 2);
        _manager.ApplyStatus(targetId, "rage", stacks: 1);
        _formulaEvaluator
            .Setup(m => m.Evaluate("stacks * 0.25", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns<string, Dictionary<string, float>?, float>((_, variables, _) =>
                Result<float>.Success(variables!["stacks"] * 0.25f));

        var modifiers = _manager.GetPipelineModifiers(targetId);

        Assert.True(modifiers.TryGetValue("increased_damage_total", out var value));
        Assert.Equal(0.75f, value);
        _formulaEvaluator.Verify(m => m.Evaluate(
            "stacks * 0.25",
            It.Is<Dictionary<string, float>>(vars => vars.ContainsKey("stacks") && vars.ContainsKey("duration")),
            0f), Times.Exactly(2));
    }

    [Fact]
    public void ProcessStatusEffects_WhenFormulaFails_FallsBackToBaseValue()
    {
        const string targetId = "enemy_1";
        _manager.ApplyStatus(targetId, "burning", stacks: 2);
        _formulaEvaluator
            .Setup(m => m.Evaluate("stacks * 3", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Result<float>.Failure("bad formula"));

        var result = _manager.ProcessStatusEffects(targetId, StatusEffectTiming.END_OF_TURN, currentTurn: 1);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var tick = Assert.Single(result.Value!.TickResults);
        Assert.Equal(6f, tick.Value);
    }

    public void Dispose()
    {
    }

    private static StatusEffectManager CreateManagerWithJson(
        string json,
        string configName = "legacy",
        IRuntimeFormulaEvaluator? formulaEvaluator = null)
    {
        var configManager = new Mock<IConfigManager>();
        var resourceLoader = new Mock<IResourceLoader>();
        configManager.Setup(m => m.ResolveInheritanceChain(configName)).Returns(new[] { configName });
        resourceLoader
            .Setup(m => m.LoadResource("StatusEffects/status_effects.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(ParseResource(json));

        var manager = new StatusEffectManager(
            configManager.Object,
            resourceLoader.Object,
            new Mock<IResourceManager>().Object,
            formulaEvaluator ?? new Mock<IRuntimeFormulaEvaluator>().Object);

        var load = manager.LoadStatusDefinitions(configName);
        Assert.True(load.IsSuccess, load.IsFailure ? load.Error : null);
        return manager;
    }

    private static Dictionary<string, JsonElement> ParseResource(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private const string TestStatusDefinitionsJson = """
    {
      "burning": {
        "StatusId": "burning",
        "Type": "BURNING",
        "DisplayName": "Burning",
        "Description": "Takes fire damage at end of turn",
        "Behavior": "DAMAGE_OVER_TIME",
        "DefaultDuration": 3,
        "DefaultStacks": 1,
        "MaxStacks": 4,
        "BaseValue": 3.0,
        "FormulaValue": "stacks * 3",
        "ScalesWithStacks": true,
        "TargetResource": "health",
        "Timing": "END_OF_TURN"
      },
      "strength": {
        "StatusId": "strength",
        "Type": "STRENGTH",
        "DisplayName": "Strength",
        "Description": "Increases damage",
        "Behavior": "STAT_MODIFIER",
        "DefaultDuration": -1,
        "DefaultStacks": 1,
        "MaxStacks": 99,
        "BaseValue": 0.25,
        "ScalesWithStacks": true,
        "ModifierKey": "increased_damage_total",
        "ModifierFormula": "stacks * 0.25",
        "Timing": "PERMANENT"
      },
      "rage": {
        "StatusId": "rage",
        "Type": "CUSTOM",
        "DisplayName": "Rage",
        "Description": "Also increases damage",
        "Behavior": "STAT_MODIFIER",
        "DefaultDuration": -1,
        "DefaultStacks": 1,
        "MaxStacks": 99,
        "BaseValue": 0.25,
        "ScalesWithStacks": true,
        "ModifierKey": "increased_damage_total",
        "ModifierFormula": "stacks * 0.25",
        "Timing": "PERMANENT"
      }
    }
    """;
}
