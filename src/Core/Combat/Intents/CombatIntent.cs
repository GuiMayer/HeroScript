using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Models;
using Core.Effects;

namespace Core.Combat.Intents;

public sealed record CombatIntent
{
    private ImmutableArray<string> _targetIds = [];
    private ImmutableArray<string> _tags = [];
    private ImmutableArray<EffectApplicationRecord> _previewApplications = [];
    private ImmutableArray<CalculationResult> _previewCalculations = [];

    public string ActorId { get; init; } = string.Empty;
    public ActionType ActionType { get; init; } = ActionType.PASS;
    public string? ActionId { get; init; }
    public Guid? CardInstanceId { get; init; }
    public string? CardDefinitionId { get; init; }
    public IReadOnlyList<string> TargetIds
    {
        get => _targetIds;
        init => _targetIds = value?.ToImmutableArray() ?? [];
    }
    public string? CostOptionId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string TelegraphType { get; init; } = "Unknown";
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectApplicationRecord> PreviewApplications
    {
        get => _previewApplications;
        init => _previewApplications = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CalculationResult> PreviewCalculations
    {
        get => _previewCalculations;
        init => _previewCalculations = value?.ToImmutableArray() ?? [];
    }
    public bool PreviewUncertain { get; init; }
    public string PreviewFingerprint { get; init; } = string.Empty;
    public string DecisionFingerprint { get; init; } = string.Empty;
    public string StateFingerprint { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string PolicyId { get; init; } = string.Empty;
    public string RuleId { get; init; } = string.Empty;
    public int ActorActionCount { get; init; }

    public CombatActionCommand ToCommand(Guid runId) => new()
    {
        RunId = runId,
        ActorId = ActorId,
        ActionType = ActionType,
        PowerId = ActionId,
        CardInstanceId = CardInstanceId,
        TargetId = TargetIds.FirstOrDefault(),
        TargetIds = TargetIds,
        CostOptionId = CostOptionId
    };
}
