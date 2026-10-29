using System.Collections.Immutable;
using System.Diagnostics;
using Core.Determinism;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Resources;
using Xunit;
using Xunit.Abstractions;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResponsePerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void CompiledCacheIsBoundedAndNeverReusesAnotherRevisionOrCapabilities()
    {
        var catalog = GameplayResponseCompilationTests.Catalog();
        var definition = GameplayResponseCompilationTests.Definition();
        var first = GameplayResponseCompiler.Compile(definition, catalog).Value;
        Assert.Same(first, GameplayResponseCompiler.Compile(definition, catalog).Value);
        var next = GameplayCoreCatalog.Create("setting", "next").Value;
        Assert.Equal("next", GameplayResponseCompiler.Compile(definition, next).Value.ContentRevision);
        Assert.True(GameplayResponseCompiler.Compile(definition with { Timing = ResponseExecutionTiming.ContainerBoundary }, catalog).IsFailure);
        Assert.True(GameplayResponseCompiler.Compile(definition with { Timing = ResponseExecutionTiming.ContainerBoundary }, catalog,
            new() { ContainerBoundary = true }).IsSuccess);
        for (var i = 0; i < GameplayResponseCompilationCache.Capacity + 1; i++)
            Assert.True(GameplayResponseCompiler.Compile(definition with { ResponseId = "entry_" + i }, catalog).IsSuccess);
        var recompiled = GameplayResponseCompiler.Compile(definition, catalog).Value;
        Assert.Equal(first.DefinitionFingerprint, recompiled.DefinitionFingerprint);
        Assert.Equal(CanonicalJson.ComputeHash(definition), recompiled.DefinitionFingerprint);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    [InlineData(1024)]
    public void IndexedMatchingPreservesReasonSnapshotAndOrderWithLargeComposition(int registrations)
    {
        var context = GameplayResponseCompilationTests.Context();
        var input = CanonicalJson.ComputeHash(context.Run);
        var definitions = Enumerable.Range(0, registrations).Select(index =>
            GameplayResponseCompilationTests.Registration(index.ToString("D4"), GameplayResponseCompilationTests.Definition() with {
                Availability = new() { Snapshot = index % 2 == 0 ? ResponseSnapshot.BeforeOccurrence : ResponseSnapshot.AfterOccurrence },
                Observe = new() { Kinds = new() { Include = ["resource.**", "resource.change.resolved"] },
                    Reasons = new() { Include = [index % 3 == 0 ? "effect.application" : "cost.*"] } } })).ToImmutableArray();
        var watch = Stopwatch.StartNew();
        var matcher = GameplayResponseMatcher.Create(definitions.Reverse().ToImmutableArray()).Value;
        var buildMs = watch.Elapsed.TotalMilliseconds;
        var fact = new GameplayOccurrence { EventId = "fact", RootEventId = "fact", Kind = "resource.change.resolved", ReasonId = "effect.application",
            Scope = new() { SettingId = context.Run.SettingId, ContentRevision = "revision", RunId = context.Run.RunId.ToString("D"),
                BranchId = context.Run.RunId.ToString("D"), ActionId = "action", CommandId = "command" }, SourceActorId = "hero", SubjectActorId = "hero",
            Payload = new ResourceChangePayload { ResourceId = "arbitrary", Field = ResourceValueField.Current, Operation = ResourceMutationOperation.Add,
                Operand = 1, Before = 0, RequestedValue = 1, After = 1 } };
        var expected = Enumerable.Range(0, registrations).Where(index => index % 2 == 0 && index % 3 == 0).Select(index => index.ToString("D4")).ToArray();
        watch.Restart();
        for (var sample = 0; sample < 32; sample++)
        {
            var matches = matcher.Match(fact, context);
            Assert.True(matches.IsSuccess, matches.IsFailure ? matches.Error : null);
            Assert.Equal(expected, matches.Value.Select(item => item.Registration.RegistrationId));
        }
        output.WriteLine($"registrations={registrations}; index={buildMs:F3}ms; matchMean={watch.Elapsed.TotalMilliseconds / 32:F3}ms");
        Assert.Equal(input, CanonicalJson.ComputeHash(context.Run));
    }
}
