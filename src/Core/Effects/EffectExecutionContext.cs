using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Resources;

namespace Core.Effects;

/// <summary>Execution identity is diagnostic, not a selector of gameplay behavior.</summary>
public sealed record EffectExecutionIdentity
{
    public string ExecutionId { get; init; } = string.Empty;
    public string ProcId { get; init; } = string.Empty;
    public string ImpactId { get; init; } = string.Empty;
    public string? ParentProcId { get; init; }
    public string? ParentImpactId { get; init; }
    public string ComponentId { get; init; } = string.Empty;
    public string? OutputId { get; init; }
}

/// <summary>Observed mutation facts, not a damage calculation or transfer policy.</summary>
public sealed record EffectResourceOutcome
{
    public float RequestedValue { get; init; }
    public float PreviousValue { get; init; }
    public float CurrentValue { get; init; }
    public double RequestedChange => (double)RequestedValue - PreviousValue;
    public double AppliedChange => (double)CurrentValue - PreviousValue;
    public double LimitedChange => (double)RequestedValue - CurrentValue;
    public bool OwnerDefeatedBefore { get; init; }
    public bool OwnerDefeatedAfter { get; init; }
    public bool CausedDefeat => !OwnerDefeatedBefore && OwnerDefeatedAfter;
    public ImmutableArray<ResourceThresholdFact> ThresholdFacts { get; init; } = [];
}

public enum EffectStackStore { Status, Modifier }
public enum EffectStackChangeReason { Apply, Reapply, Remove, Dispel, Consume, Expire }

public sealed record EffectStackChange
{
    public EffectStackStore Store { get; init; }
    public Guid InstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public Core.Combat.Models.GameplayOwner? Owner { get; init; }
    public int PreviousStacks { get; init; }
    public int CurrentStacks { get; init; }
    public EffectStackChangeReason Reason { get; init; }
    public bool Removed => CurrentStacks == 0;
}

/// <summary>Pure, action-local index; public records, not this cache, are persisted.</summary>
internal sealed record EffectResultContext
{
    private readonly record struct Key(string OutputId, string TargetId);
    private sealed record Totals(float Requested, float Applied, float Limited, int Defeats, int Count);
    private sealed record Output(EffectApplicationRecord Last, Totals Totals);
    private ImmutableDictionary<Key, Output> Outputs { get; init; } = ImmutableDictionary<Key, Output>.Empty;

    public Result<EffectResultContext> Add(IReadOnlyList<EffectApplicationRecord> records)
    {
        var outputs = Outputs;
        foreach (var record in records)
        {
            // Settlements have their own units: never sum protection with the
            // primary resource merely because both share an output alias.
            if (record.Identity?.OutputId is not { } alias || record.CalculationInfluenceId != null) continue;
            var key = new Key(alias, record.TargetEntityId);
            var previous = outputs.GetValueOrDefault(key);
            if (previous != null && (previous.Last.ResourceId != record.ResourceId ||
                previous.Last.ResourceField != record.ResourceField))
                return Result<EffectResultContext>.Failure($"Output {alias} changed resource units within the action");
            var totals = previous?.Totals ?? new Totals(0, 0, 0, 0, 0);
            var outcome = record.ResourceOutcome;
            totals = new(totals.Requested + (float)(outcome?.RequestedChange ?? 0),
                totals.Applied + (float)(outcome?.AppliedChange ?? 0), totals.Limited + (float)(outcome?.LimitedChange ?? 0),
                totals.Defeats + (outcome?.CausedDefeat == true ? 1 : 0), totals.Count + 1);
            if (!float.IsFinite(totals.Requested) || !float.IsFinite(totals.Applied) || !float.IsFinite(totals.Limited))
                return Result<EffectResultContext>.Failure($"Output {alias} produced non-finite accumulated facts");
            outputs = outputs.SetItem(key, new(record, totals));
        }
        return Result<EffectResultContext>.Success(this with { Outputs = outputs });
    }

    public void AddVariables(IDictionary<string, float> variables, string targetId)
    {
        foreach (var (key, output) in Outputs)
        {
            if (key.TargetId != targetId) continue;
            var prefix = $"results.{key.OutputId}.target";
            var outcome = output.Last.ResourceOutcome;
            if (outcome != null)
            {
                variables[$"{prefix}.last.requested_change"] = (float)outcome.RequestedChange;
                variables[$"{prefix}.last.applied_change"] = (float)outcome.AppliedChange;
                variables[$"{prefix}.last.limited_change"] = (float)outcome.LimitedChange;
                variables[$"{prefix}.last.caused_defeat"] = outcome.CausedDefeat ? 1 : 0;
                variables[$"{prefix}.total.requested_change"] = output.Totals.Requested;
                variables[$"{prefix}.total.applied_change"] = output.Totals.Applied;
                variables[$"{prefix}.total.limited_change"] = output.Totals.Limited;
                variables[$"{prefix}.total.defeats"] = output.Totals.Defeats;
            }
            variables[$"{prefix}.total.applications"] = output.Totals.Count;
            variables[$"{prefix}.last.stack_delta"] = output.Last.StackChanges.Sum(change =>
                (float)change.CurrentStacks - change.PreviousStacks);
        }
    }

    public static string ExecutionId(EffectTriggerExecutionRequest request) => CanonicalJson.ComputeHash(new
    {
        state = CanonicalJson.ComputeHash(request.Combat),
        run = request.Run == null ? null : CanonicalJson.ComputeHash(request.Run),
        request.Trigger, request.Components, request.PrefixCommands,
        request.Provenance, request.ContentRevision, request.OwnerEntityId, request.SourceEntityId,
        request.Variables, request.Tags, card = request.Card?.Fingerprint,
        targets = request.SelectedTargetEntityIds
    });

    public static EffectExecutionIdentity Identity(string executionId, string componentId,
        string path, int repeat, string targetId, string? parentProcId, string? outputId = null,
        string? parentImpactId = null)
    {
        var procId = CanonicalJson.ComputeHash(new { executionId, path, repeat, parentProcId, parentImpactId });
        return new()
        {
            ExecutionId = executionId, ProcId = procId,
            ImpactId = CanonicalJson.ComputeHash(new { procId, targetId }),
            ParentProcId = parentProcId, ParentImpactId = parentImpactId, ComponentId = componentId, OutputId = outputId
        };
    }
}
