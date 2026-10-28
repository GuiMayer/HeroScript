using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using System.Collections.Immutable;
using Xunit;
namespace Core.Tests.Effects;

/// <summary>Captured from the pre-migration dispatcher tree 6ca1ef6c3c930b72939929c46bf6d81dd78d3590.</summary>
public sealed class LegacyResourceResponseParityTests
{
    [Fact]
    public void WalletAdaptersKeepTheirLegacyBindingWithoutPublishingAFakeActorSubject()
    {
        var request = EffectReactionTests.Request(EffectReactionTests.Damage(2));
        var run = request.Run!;
        var catalog = GameplayCoreCatalog.Create(run.SettingId, run.Determinism.ContentRevision).Value;
        var observation = new GameplayEffectObservation
        {
            Events = GameplayEventTransaction.Create(new() { SettingId = run.SettingId, ContentRevision = run.Determinism.ContentRevision,
                RunId = (run.Lineage?.RootRunId ?? run.RunId).ToString("D"), BranchId = run.RunId.ToString("D"),
                ActionId = "action", CommandId = "command", CombatId = request.Combat.CombatId.ToString("D") }, catalog).Value,
            PublisherId = GameplayCoreCatalog.PublisherId, ReasonId = "effect.application",
            ResourceOwnerBindings = ImmutableDictionary<string, GameplayObjectReference>.Empty.Add("enemy", new()
                { Kind = "run", Id = "enemy", ScopeId = run.RunId.ToString("D") })
        };
        var executor = EffectReactionTests.Executor();
        var normal = executor.Execute(request);
        var observed = executor.ExecuteObserved(request, observation);
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(normal.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
        var facts = observed.Value.Observation.Events.Occurrences;
        Assert.Equal(2, facts.Count); Assert.Null(facts[0].SubjectActorId);
        Assert.Equal("run", ((ResourceChangePayload)facts[0].Payload).Owner!.Kind);
        Assert.Equal("hero", facts[1].SubjectActorId); Assert.Equal(facts[0].EventId, facts[1].ParentEventId);
    }

    [Fact]
    public void PrefixesRemainOutsideTheHistoricalReactionSlotEvenWhenTheirChannelMatches()
    {
        var request = EffectReactionTests.Request(EffectReactionTests.Damage(2));
        request = request with { PrefixCommands = [new()
        {
            EffectInstanceId = "prefix", SourceEntityId = "hero", TargetEntityIds = ["enemy"], ResolvedValue = 2,
            ContentRevision = "revision", SemanticReasonId = "effect.application",
            Definition = new() { EffectId = "prefix", Type = EffectType.MODIFY_RESOURCE, TargetResource = "focus",
                Operation = ResourceEffectOperation.SUBTRACT, FlatValue = 2, CalculationChannel = "magnitude" }
        }] };
        var original = CanonicalJson.ComputeHash(request);
        var result = EffectReactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(11, result.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(6, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(3, result.Value.Steps.Length); Assert.Equal(original, CanonicalJson.ComputeHash(request));
    }

    [Theory]
[InlineData("zero", "2e20e35c3cf1cd909dd77bae28ffa00ce443fc708e013227649b6fbd363d0703", "2c63fa802e2fea687d241f3195799edb917783a1434d6e9a227b46352b80de0c")]
[InlineData("two", "e3aa4a651face8f3a774907dc9e0d3185d2d6516478405504f024a0d108c8b08", "a90920030395506d8adbeaa95e647fb2e3c786307bc1a6c91c587ace3f2566be")]
[InlineData("four", "25324253bd55cbdf95ab6c3a5986219d0aba6d4d0246d83cba6c8141ed9d73f1", "d2ddeca0669bba6f629a0eb04c9acc305466799781b0a5b9212f5551f475f675")]
[InlineData("overkill", "77b36670d885bf45cd92ed5f70bfc49db0b4bb3180d98c03c84e2e9904ae4c27", "505f0a18cd6aa31f2a0760348947e4a833c2ee373d7ebb142e1860b250d045b5")]
[InlineData("repeat", "59515cc4a8c6ff483212d8dc3690c12a01ffb173d3d8e602c59dfda504420f20", "847aaad1d3a24d2d47ec68823913f82bdc73131e55da301354a24a22a0e84ad0")]
[InlineData("chance", "5f7ff62aee52ec557dfbcf7385f9298e0b4406f59a417a56b946f099a034f335", "71d56b1ac00cb903d5dda6dcaf0630bbb8b51274f44de01528f5da88d696d828")]
[InlineData("children", "48d6267331b468b43963b4e826253923383e08accb8ca5f814631e027b7c16b3", "f26a3d86c0f884e568425d48e840dce69703724be42fe66664967d2c006f1534")]
    public void CapturedResourceReactionExecutionRemainsExactlyEqual(string scenario, string requestHash, string executionHash)
    {
        var damage = EffectReactionTests.Damage(scenario == "zero" ? 0 : scenario == "four" ? 4 : scenario == "overkill" ? 30 : 2);
        if (scenario == "repeat") damage = damage with { Repeat = 3 };
        if (scenario == "chance") damage = damage with { Repeat = 6, Chance = .65f };
        if (scenario == "children") damage = damage with { ChainedEffects = [EffectReactionTests.Damage(1)] };
        var request = EffectReactionTests.Request(damage);
        Assert.Equal(requestHash, CanonicalJson.ComputeHash(request));
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var result = EffectReactionTests.Executor().Execute(request);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(executionHash, CanonicalJson.ComputeHash(result.Value));
            Assert.Equal(requestHash, CanonicalJson.ComputeHash(request));
        }
    }
}
