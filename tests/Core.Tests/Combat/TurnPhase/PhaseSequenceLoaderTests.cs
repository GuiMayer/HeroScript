using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Config;
using Core.Logging;
using Moq;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public sealed class PhaseSequenceLoaderTests
{
    private readonly PhaseSequenceLoader _loader;

    public PhaseSequenceLoaderTests()
    {
        _loader = new PhaseSequenceLoader(
            new ConsoleLogger(nameof(PhaseSequenceLoaderTests)),
            Mock.Of<IConfigManager>(),
            Mock.Of<IResourceLoader>());
    }

    [Fact]
    public void LoadFromJson_AcceptsArbitraryIdsWithRequiredSemanticRoles()
    {
        var result = _loader.LoadFromJson(ValidJson);

        Assert.True(result.IsSuccess);
        Assert.Equal(["upkeep_custom", "planning_window", "cleanup_custom"],
            result.Value.Phases.Select(phase => phase.PhaseId));
        Assert.Equal(PhaseRole.Middle, result.Value.Find("planning_window")?.Role);
        Assert.Contains(ActionType.POWER, result.Value.Find("planning_window")!.AllowedActions);
    }

    [Fact]
    public void LoadFromJson_RejectsSequenceWithoutEverySemanticRole()
    {
        var json = ValidJson.Replace("\"role\": \"End\"", "\"role\": \"Middle\"");

        var result = _loader.LoadFromJson(json);

        Assert.True(result.IsFailure);
        Assert.Contains("END", result.Error);
    }

    [Fact]
    public void LoadFromJson_RejectsUnknownTransitionTarget()
    {
        var json = ValidJson.Replace("cleanup_custom\"]", "missing\"]");

        var result = _loader.LoadFromJson(json);

        Assert.True(result.IsFailure);
        Assert.Contains("invalid next phase", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromJson_RejectsDuplicateOrder()
    {
        var json = ValidJson.Replace("\"order\": 20", "\"order\": 10");

        var result = _loader.LoadFromJson(json);

        Assert.True(result.IsFailure);
        Assert.Contains("unique and ascending", result.Error);
    }

    internal const string ValidJson = """
    {
      "sequenceId": "custom",
      "name": "Custom",
      "phases": [
        { "phaseId": "upkeep_custom", "role": "Start", "order": 10, "allowedActions": [], "validNextPhaseIds": ["planning_window"], "autoTransition": true },
        { "phaseId": "planning_window", "role": "Middle", "order": 20, "allowedActions": ["POWER", "END_TURN"], "validNextPhaseIds": ["cleanup_custom"] },
        { "phaseId": "cleanup_custom", "role": "End", "order": 30, "allowedActions": [], "validNextPhaseIds": ["upkeep_custom"], "autoTransition": true }
      ]
    }
    """;
}
