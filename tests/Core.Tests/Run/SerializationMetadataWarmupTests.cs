using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.Run;
public class SerializationMetadataWarmupTests
{
    [Fact]
    public void PreparingTypesDoesNotChangeSerializationOrCanonicalHash()
    {
        var state = new RunState { RunId=Guid.NewGuid(), Sequence=1, Determinism=DeterministicContext.Create(17,"fixture") };
        var fresh = new JsonSerializerOptions(RunCommitJson.Options);
        var json = JsonSerializer.Serialize(state, fresh);
        var hash = CanonicalJson.ComputeHash(state);
        SerializationMetadataWarmup.Prepare(fresh, typeof(RunState), typeof(RunCommit));
        CanonicalJson.PrepareMetadata(typeof(RunState));
        Assert.Equal(json, JsonSerializer.Serialize(state, fresh));
        Assert.Equal(hash, CanonicalJson.ComputeHash(state));
    }
}
