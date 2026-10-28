using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayBoundaryScopeTests
{
    private static GameplayBoundaryInvocation Invocation(string id, EffectDefinition? effect = null)
    {
        var context = GameplayResponseCompilationTests.Context();
        return new() { Kind = "phase.entered", BoundaryId = "main",
            Origin = new() { ContainerKind = "rule", ContainerId = "test", ComponentId = id, ContentRevision = "revision" },
            Template = EffectTransactionTests.Request(effect ?? EffectTransactionTests.Resource(EffectType.DAMAGE, 0)) with
                { Run = context.Run, Trigger = new() { TriggerId = id, Effects = effect == null ? [] : [effect] } } };
    }

    [Fact]
    public void EmptyCapturedTriggerRemainsAnInvocationButAuthoredEmptyEffectsAreRejected()
    {
        var context = GameplayResponseCompilationTests.Context(); var executor = EffectTransactionTests.Executor();
        var invocation = Invocation("empty");
        var scope = executor.BeginBoundaryScope(context.Run, context.Combat!).Value;
        var result = scope.Dispatch(context.Run, context.Combat!, "phase.entered", "main", "hero", [invocation]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var frame = Assert.Single(result.Value.Invocations); Assert.Empty(frame.Execution.Steps);
        Assert.Equal(CanonicalJson.ComputeHash(executor.Execute(invocation.Template).Value), CanonicalJson.ComputeHash(frame.Execution));
        Assert.Null(result.Value.Observation);
        Assert.True(GameplayResponseCompiler.Compile(GameplayResponseCompilationTests.Definition() with { Effects = [] }, context.Catalog).IsFailure);
    }

    [Fact]
    public void WorkAndFailureStateAreSharedAcrossSuccessiveDispatches()
    {
        var context = GameplayResponseCompilationTests.Context(); var executor = EffectTransactionTests.Executor();
        var scope = executor.BeginBoundaryScope(context.Run, context.Combat!).Value;
        var original = CanonicalJson.ComputeHash(new { context.Run, context.Combat });
        var effect = EffectTransactionTests.Resource(EffectType.DAMAGE, 0) with { Repeat = 256 };
        var failed = false;
        for (var index = 0; index < 9; index++)
        {
            var invocation = Invocation("body-" + index, effect);
            invocation = invocation with { Template = invocation.Template with { Trigger = invocation.Template.Trigger with { Effects = [effect, effect] } } };
            var result = scope.Dispatch(context.Run, context.Combat!, "phase.entered", "main", "hero", [invocation]);
            if (result.IsSuccess) continue;
            Assert.Contains("Effect execution limit", result.Error); failed = true; break;
        }
        Assert.True(failed);
        Assert.True(scope.Dispatch(context.Run, context.Combat!, "phase.entered", "main", "hero", [Invocation("retry")]).IsFailure);
        Assert.Equal(original, CanonicalJson.ComputeHash(new { context.Run, context.Combat }));
    }

    [Fact]
    public void CausalPrefixCannotBeReplacedAndAConsumedInvocationCannotRunAgain()
    {
        var context = GameplayResponseCompilationTests.Context(); var executor = EffectTransactionTests.Executor();
        var observation = GameplayObservedExecutorTests.ObservationFor(context.Run, context.Combat!);
        var scope = executor.BeginBoundaryScope(context.Run, context.Combat!, observation).Value;
        var invocation = Invocation("empty");
        var first = scope.Dispatch(context.Run, context.Combat!, "phase.entered", "main", "hero", [invocation], observation).Value;
        var repeated = scope.Dispatch(context.Run, context.Combat!, "phase.entered", "main", "hero", [invocation], first.Observation);
        Assert.True(repeated.IsSuccess, repeated.IsFailure ? repeated.Error : null); Assert.Empty(repeated.Value.Invocations);
        Assert.Single(repeated.Value.Observation!.Events.Occurrences);
        var forged = GameplayEventTransaction.Create(observation.Events.Scope, context.Catalog).Value;
        forged = forged.Append(GameplayCoreCatalog.PublisherId, "different-producer", new() { Kind = "phase.entered", ReasonId = "effect.application",
            Attribution = GameplayAttribution.None, Payload = new BoundaryOccurrencePayload { BoundaryId = "main", Sequence = 0 } }).Value.Transaction;
        var rejected = scope.Dispatch(context.Run, context.Combat!, "phase.entered", "main", "hero", [Invocation("other")], first.Observation! with { Events = forged });
        Assert.True(rejected.IsFailure); Assert.Contains("substitute", rejected.Error);
        Assert.Empty(observation.Events.Occurrences);
    }

    [Fact]
    public void FailedBodyDoesNotProduceAPartialLifecycleOrAllowReuse()
    {
        var context = GameplayResponseCompilationTests.Context(); var executor = EffectTransactionTests.Executor();
        var scope = executor.BeginBoundaryScope(context.Run, context.Combat!).Value;
        var first = Invocation("gain", EffectTransactionTests.Resource(EffectType.HEAL, 1));
        var last = Invocation("invalid", EffectTransactionTests.Resource(EffectType.HEAL, 1) with { TargetResource = "absent" });
        var original = CanonicalJson.ComputeHash(context.Combat);
        Assert.True(scope.Dispatch(context.Run, context.Combat!, "phase.entered", "main", "hero", [first, last]).IsFailure);
        Assert.Equal(original, CanonicalJson.ComputeHash(context.Combat));
        Assert.True(scope.Dispatch(context.Run, context.Combat!, "phase.entered", "main", "hero", [first]).IsFailure);
    }
}
