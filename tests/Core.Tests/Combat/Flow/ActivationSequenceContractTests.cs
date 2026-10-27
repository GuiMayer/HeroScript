using System.Text.Json;
using Core.Combat.Flow;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class ActivationSequenceContractTests
{
    [Fact]
    public void MissingPolicyIsNotAnImplicitPerRoundDefault() => Assert.True(ActivationSequencePolicy.Validate(null).IsFailure);

    [Theory]
    [InlineData("PerRound")]
    [InlineData("Circular")]
    public void AuthoredSequenceSurvivesPolicySerialization(string strategy)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var policy = JsonSerializer.Deserialize<CombatFlowPoliciesDefinition>(
            "{\"activationSequence\":{\"strategy\":\"" + strategy + "\"}}", options)!;
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(policy, options));
        Assert.True(serialized.RootElement.TryGetProperty("activationSequence", out var sequence),
            "The selected traversal must be part of the pinned policy, not ignored JSON");
        Assert.Equal(strategy, sequence.GetProperty("strategy").GetString());
    }
}
