using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Run;
using Core.Run.Branching;
using Xunit;

namespace Core.Tests.Persistence;

public sealed class RunStateDeltaTests
{
    [Fact]
    public void Delta_RoundTripsNestedObjectsArraysAndEscapedKeys()
    {
        var runId = Guid.NewGuid();
        var previous = new RunState
        {
            RunId = runId,
            Sequence = 1,
            Lineage = RunLineage.Root(runId),
            CurrentNodeId = "start",
            CompletedActivityNodeIds = ["intro"],
            Metadata = new Dictionary<string, JsonElement>
            {
                ["a/b~c"] = JsonSerializer.SerializeToElement(new { value = 1 }),
                ["removed"] = JsonSerializer.SerializeToElement(true)
            }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            Determinism = DeterministicContext.Create(91, "revision").AdvanceStep()
        };
        var current = previous with
        {
            Sequence = 2,
            CurrentNodeId = "combat",
            CompletedActivityNodeIds = ["intro", "start"],
            Metadata = new Dictionary<string, JsonElement>
            {
                ["a/b~c"] = JsonSerializer.SerializeToElement(new { value = 2 }),
                ["added"] = JsonSerializer.SerializeToElement("yes")
            }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            Determinism = previous.Determinism.AdvanceStep()
        };

        var delta = RunStateDelta.Create(previous, current);
        var restored = RunStateDelta.Apply(previous, delta.Operations);

        Assert.NotEmpty(delta.Operations);
        Assert.Contains(delta.Operations, operation => operation.Path.Contains("~1", StringComparison.Ordinal));
        Assert.Contains(delta.Operations, operation => operation.Path.Contains("~0", StringComparison.Ordinal));
        Assert.Equal(CanonicalJson.ComputeHash(current), CanonicalJson.ComputeHash(restored));
    }

    [Fact]
    public void Delta_RejectsInvalidPaths()
    {
        var runId = Guid.NewGuid();
        var state = new RunState
        {
            RunId = runId,
            Sequence = 1,
            Lineage = RunLineage.Root(runId),
            Determinism = DeterministicContext.Create(7, "revision")
        };

        Assert.Throws<InvalidDataException>(() => RunStateDelta.Apply(
            state,
            [new RunStatePatchOperation { Operation = "remove", Path = "/missing/path" }]));
    }
}
