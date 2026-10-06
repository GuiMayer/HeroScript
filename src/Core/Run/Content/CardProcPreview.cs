using System.Collections.Immutable;
using Core.Effects;

namespace Core.Run.Content;

/// <summary>Derived presentation facts. Never a new execution authority.</summary>
public sealed record CardProcPreview
{
    public string ProcId { get; init; } = string.Empty;
    public string? ParentProcId { get; init; }
    public bool IsCondensation { get; init; }
    public ImmutableArray<string> ImpactIds { get; init; } = [];
    public ImmutableArray<string> TargetEntityIds { get; init; } = [];
    public ImmutableArray<EffectStackChange> ConsumedStacks { get; init; } = [];
    public ImmutableArray<EffectContinuationTrace> Continuations { get; init; } = [];
}

public sealed record CardPreviewScope
{
    public bool HasExecutablePreview { get; init; }
    public bool DependsOnRandomInputs { get; init; }
    public ImmutableArray<string> SelectedTargetIds { get; init; } = [];
    public string? CostOptionId { get; init; }
    public string SnapshotHash { get; init; } = string.Empty;
    public string CombatSnapshotHash { get; init; } = string.Empty;
    public string Validity { get; init; } = "ExactForCapturedSnapshotAndInput";
}

internal static class CardProcPreviewProjector
{
    public static ImmutableArray<CardProcPreview> Project(IReadOnlyList<EffectExecutionStep> steps) => steps
        .Where(step => step.Identity != null).GroupBy(step => step.Identity!.ProcId, StringComparer.Ordinal)
        .Select(group => new CardProcPreview
        {
            ProcId = group.Key, ParentProcId = group.First().Identity!.ParentProcId,
            IsCondensation = group.Any(step => step.Condensation != null),
            ImpactIds = group.Select(step => step.Identity!.ImpactId).Distinct(StringComparer.Ordinal).ToImmutableArray(),
            TargetEntityIds = group.Select(step => step.TargetEntityId).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToImmutableArray(),
            ConsumedStacks = group.SelectMany(step => step.Applications).SelectMany(record => record.StackChanges)
                .Where(change => change.Reason == EffectStackChangeReason.Consume).ToImmutableArray(),
            Continuations = group.Select(step => step.Continuation).OfType<EffectContinuationTrace>().ToImmutableArray()
        }).ToImmutableArray();
}
