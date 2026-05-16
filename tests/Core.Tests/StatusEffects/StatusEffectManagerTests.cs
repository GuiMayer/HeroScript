using Core.Config;
using Core.Math;
using Core.Resources;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.StatusEffects;

public sealed class StatusEffectManagerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StatusEffectManager _manager;

    public StatusEffectManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"heroscript-status-tests-{Guid.NewGuid():N}");
        var statusDirectory = Path.Combine(_tempRoot, "StatusEffects");
        Directory.CreateDirectory(statusDirectory);
        File.WriteAllText(Path.Combine(statusDirectory, "status_effects.json"), TestStatusDefinitionsJson);

        var configManager = new Mock<IConfigManager>();
        configManager.Setup(m => m.GetConfigPath("test")).Returns(_tempRoot);

        var resourceManager = new Mock<IResourceManager>();
        var mathEngine = new Mock<IMathEngine>();

        _manager = new StatusEffectManager(configManager.Object, resourceManager.Object, mathEngine.Object);
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
        var targetId = Guid.NewGuid();

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
    public void ProcessStatusEffects_ForMatchingTiming_ReturnsFormulaValue()
    {
        var targetId = Guid.NewGuid();
        var apply = _manager.ApplyStatus(targetId, "burning", stacks: 2);

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
        var targetId = Guid.NewGuid();
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
        var targetId = Guid.NewGuid();
        _manager.ApplyStatus(targetId, "strength", stacks: 2);
        _manager.ApplyStatus(targetId, "rage", stacks: 1);

        var modifiers = _manager.GetPipelineModifiers(targetId);

        Assert.True(modifiers.TryGetValue("increased_damage_total", out var value));
        Assert.Equal(0.75f, value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
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
