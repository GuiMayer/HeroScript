using Core.Common;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Core.Run.Branching;

/// <summary>
/// Immutable ancestry anchored to the first commit of a run. Root runs point
/// to themselves; branches also pin the exact parent commit and state hash.
/// </summary>
public sealed record RunLineage
{
    public Guid RootRunId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableSortedSet<Guid>? CausalAncestorRunIds { get; init; }
    public Guid? ParentRunId { get; init; }
    public int? SourceSequence { get; init; }
    public string? SourceStateHash { get; init; }
    public Guid? SourceCombatId { get; init; }
    public string? BranchKey { get; init; }
    public bool InternalSimulation { get; init; }

    public bool Equals(RunLineage? other) => other != null && RootRunId == other.RootRunId &&
        ParentRunId == other.ParentRunId && SourceSequence == other.SourceSequence && SourceStateHash == other.SourceStateHash &&
        SourceCombatId == other.SourceCombatId && BranchKey == other.BranchKey && InternalSimulation == other.InternalSimulation &&
        (CausalAncestorRunIds == null ? other.CausalAncestorRunIds == null
            : other.CausalAncestorRunIds != null && CausalAncestorRunIds.SetEquals(other.CausalAncestorRunIds));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RootRunId); hash.Add(ParentRunId); hash.Add(SourceSequence); hash.Add(SourceStateHash, StringComparer.Ordinal);
        hash.Add(SourceCombatId); hash.Add(BranchKey, StringComparer.Ordinal); hash.Add(InternalSimulation);
        hash.Add(CausalAncestorRunIds != null);
        if (CausalAncestorRunIds != null) foreach (var ancestor in CausalAncestorRunIds) hash.Add(ancestor);
        return hash.ToHashCode();
    }

    public static RunLineage Root(Guid runId) => new() { RootRunId = runId };

    public static RunLineage Branch(
        Guid rootRunId,
        Guid parentRunId,
        int sourceSequence,
        string sourceStateHash,
        Guid? sourceCombatId,
        string branchKey) => new()
        {
            RootRunId = rootRunId,
            ParentRunId = parentRunId,
            SourceSequence = sourceSequence,
            SourceStateHash = sourceStateHash,
            SourceCombatId = sourceCombatId,
            BranchKey = branchKey,
            InternalSimulation = branchKey.StartsWith("simulation:", StringComparison.Ordinal)
        };

    public Result Validate(Guid runId)
    {
        if (CausalAncestorRunIds is { } ancestors && (ancestors.Count > 256 || ancestors.Contains(Guid.Empty) ||
            ancestors.Contains(runId) || ParentRunId == null || !ancestors.Contains(ParentRunId.Value) || !ancestors.Contains(RootRunId)))
            return Result.Failure("Run causal ancestry is invalid or exceeds its budget");
        if (runId == Guid.Empty || RootRunId == Guid.Empty)
            return Result.Failure("Run lineage requires run and root ids");
        if (ParentRunId == null)
        {
            return RootRunId == runId && SourceSequence == null && SourceStateHash == null &&
                   SourceCombatId == null && BranchKey == null && !InternalSimulation
                ? Result.Success()
                : Result.Failure("Root run lineage is inconsistent");
        }
        if (RootRunId == runId || ParentRunId == Guid.Empty || ParentRunId == runId ||
            SourceSequence is null or < 1 || SourceStateHash?.Length != 64 ||
            string.IsNullOrWhiteSpace(BranchKey))
        {
            return Result.Failure("Branch lineage is incomplete");
        }
        if (BranchKey.Length > 128)
            return Result.Failure("Branch key cannot exceed 128 characters");
        if (InternalSimulation != BranchKey.StartsWith("simulation:", StringComparison.Ordinal))
            return Result.Failure("Simulation lineage marker does not match its branch key");
        return Result.Success();
    }
}

/// <summary>
/// Explicit branch-copy policy. Existing gameplay instances keep their IDs;
/// run-scoped ownership and the active combat identity move to the child.
/// Source command resolutions remain available through lineage, not by copying
/// their command history into the new aggregate.
/// </summary>
public sealed record RunBranchRebasePolicy
{
    public bool PreserveCardInstanceIds { get; init; } = true;
    public bool PreserveRelicInstanceIds { get; init; } = true;
    public bool PreserveCompletedCombatIds { get; init; } = true;
    public bool RebaseActiveCombatId { get; init; } = true;
    public bool RebaseRunScopedOwners { get; init; } = true;
    public bool ClearInheritedCommandResolutions { get; init; } = true;

    public static RunBranchRebasePolicy Canonical { get; } = new();
}
