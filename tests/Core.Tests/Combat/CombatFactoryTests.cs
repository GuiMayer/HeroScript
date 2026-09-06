using Core.Combat;
using Core.Combat.TurnOrder;
using Core.Determinism;
using Core.Logging;
using Core.Tests.Combat.TurnOrder;
using Xunit;

namespace Core.Tests.Combat;

public sealed class CombatFactoryTests
{
    [Fact]
    public void Create_WithSameExplicitInputs_ProducesIdenticalSnapshot()
    {
        var participants = TurnOrderTestHelper.CreateTestCombatState("hero", ["enemy"]);
        var factory = new CombatFactory(
            TestDataBuilders.MockResourceManager().Object,
            new FixedTurnOrderCalculator(NullLogger.Instance));
        var options = new CombatStartOptions(
            Seed: 123,
            ContentRevision: new string('a', 64),
            RunId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RunNodeId: "encounter",
            IdScope: "factory-test");

        var first = factory.Create(participants.Hero, participants.Enemies, options);
        var replay = factory.Create(participants.Hero, participants.Enemies, options);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(replay.IsSuccess, replay.IsFailure ? replay.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(first.Value), CanonicalJson.ComputeHash(replay.Value));
    }

    [Fact]
    public void Create_WithoutSeed_RejectsNondeterministicBoundary()
    {
        var participants = TurnOrderTestHelper.CreateTestCombatState("hero", ["enemy"]);
        var factory = new CombatFactory(
            TestDataBuilders.MockResourceManager().Object,
            new FixedTurnOrderCalculator(NullLogger.Instance));

        var result = factory.Create(
            participants.Hero,
            participants.Enemies,
            new CombatStartOptions(ContentRevision: new string('a', 64)));

        Assert.True(result.IsFailure);
        Assert.Contains("seed is required", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
