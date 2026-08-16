using System.Collections.Immutable;
using System.Text.Json;
using Core.Common;
using Core.Determinism;

namespace Core.Run;

public sealed record RelicDefinition
{
    private ImmutableDictionary<string, JsonElement> _properties =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);

    public string RelicId { get; init; } = string.Empty;
    public int StackLimit { get; init; } = 1;

    public IReadOnlyDictionary<string, JsonElement> Properties
    {
        get => _properties;
        init => _properties = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public sealed record RunRelicState
{
    private ImmutableDictionary<string, JsonElement> _properties =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);

    public Guid RelicInstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public int Stacks { get; init; } = 1;
    public ulong AcquiredAtStep { get; init; }

    /// <summary>Immutable copy of the gameplay properties pinned at acquisition.</summary>
    public IReadOnlyDictionary<string, JsonElement> Properties
    {
        get => _properties;
        init => _properties = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public sealed record RelicTransition(RunState State, RunRelicState Relic);

public static class RelicTransitions
{
    public static Result<RelicTransition> Acquire(RunState state, RelicDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.RelicId))
            return Result<RelicTransition>.Failure("Relic definition id is required");
        if (definition.StackLimit < 1)
            return Result<RelicTransition>.Failure("Relic stack limit must be positive");

        var existingIndex = state.Relics.FindIndex(relic =>
            string.Equals(relic.DefinitionId, definition.RelicId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            var existing = state.Relics[existingIndex];
            if (existing.Stacks >= definition.StackLimit)
                return Result<RelicTransition>.Failure($"Relic stack limit reached: {definition.RelicId}");
            var stacked = existing with { Stacks = existing.Stacks + 1 };
            return Result<RelicTransition>.Success(new RelicTransition(
                state with
                {
                    Relics = state.Relics.SetItem(existingIndex, stacked),
                    Determinism = state.Determinism.AdvanceStep()
                },
                stacked));
        }

        var allocated = state.Determinism.AllocateId($"relic:{definition.RelicId}");
        var relic = new RunRelicState
        {
            RelicInstanceId = allocated.Value,
            DefinitionId = definition.RelicId,
            AcquiredAtStep = state.Determinism.Step,
            Properties = definition.Properties
        };
        return Result<RelicTransition>.Success(new RelicTransition(
            state with
            {
                Relics = state.Relics.Add(relic),
                Determinism = allocated.Context.AdvanceStep()
            },
            relic));
    }

    public static Result<RunState> Remove(RunState state, Guid relicInstanceId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var index = state.Relics.FindIndex(relic => relic.RelicInstanceId == relicInstanceId);
        if (index < 0)
            return Result<RunState>.Failure($"Relic instance not found: {relicInstanceId}");

        return Result<RunState>.Success(state with
        {
            Relics = state.Relics.RemoveAt(index),
            Determinism = state.Determinism.AdvanceStep()
        });
    }

    private static int FindIndex<T>(this ImmutableArray<T> items, Func<T, bool> predicate)
    {
        for (var index = 0; index < items.Length; index++)
        {
            if (predicate(items[index]))
                return index;
        }
        return -1;
    }
}
