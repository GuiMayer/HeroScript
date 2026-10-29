using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Run.Branching;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class RunLineageEqualityTests
{
    [Fact]
    public void DeserializedCausalAncestryUsesValueEqualityWithoutIgnoringMissingAncestors()
    {
        var root = Guid.NewGuid(); var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        var original = RunLineage.Branch(root, parent, 2, new string('a', 64), null, "test") with {
            CausalAncestorRunIds = ImmutableSortedSet.Create(root, parent) };
        var restored = JsonSerializer.Deserialize<RunLineage>(JsonSerializer.Serialize(original, RunCommitJson.Options), RunCommitJson.Options)!;
        Assert.NotSame(original.CausalAncestorRunIds, restored.CausalAncestorRunIds);
        Assert.Equal(original, restored); Assert.Equal(original.GetHashCode(), restored.GetHashCode());
        Assert.True(restored.Validate(child).IsSuccess);
        Assert.NotEqual(original, restored with { CausalAncestorRunIds = ImmutableSortedSet.Create(root) });
        Assert.NotEqual(original, restored with { CausalAncestorRunIds = null });
        Assert.NotEqual(original, restored with { SourceSequence = 3 });
    }
}
