using System.Text.Json;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayBoundaryResponseFilterTests
{
    private static GameplayResponseDefinition Definition() => GameplayResponseCompilationTests.Definition() with
    {
        Observe = new() { Kinds = new() { Include = ["phase.**"] }, Boundary = new() { BoundaryIds = ["main"] } },
        Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner } }
    };

    [Fact]
    public void BoundaryPayloadSelectionDoesNotInterpretThePhaseNameOrChangeItsTopic()
    {
        var context = GameplayResponseCompilationTests.Context();
        var registrations = GameplayObserverCollector.Collect(context,
            [new GameplayResponseCompilationTests.FixedProvider(GameplayResponseCompilationTests.Registration(definition: Definition()))]).Value;
        var matcher = GameplayResponseMatcher.Create(registrations).Value;
        var fact = GameplayResponseMatcherTests.Fact() with { Kind = "phase.entered", SubjectActorId = null,
            Payload = new BoundaryOccurrencePayload { BoundaryId = "main", Sequence = 1 } };
        var candidate = Assert.Single(matcher.Match(fact, context).Value);
        Assert.Equal("hero", candidate.TargetActorId);
        Assert.Equal("phase.entered", candidate.Occurrence.Kind);
        Assert.Empty(matcher.Match(fact with { Payload = new BoundaryOccurrencePayload { BoundaryId = "Main", Sequence = 1 } }, context).Value);
        Assert.Empty(matcher.Match(fact with { Payload = new BoundaryOccurrencePayload { BoundaryId = "other", Sequence = 1 } }, context).Value);
        Assert.Empty(matcher.Match(fact with { Kind = "activation.started" }, context).Value);
    }

    [Fact]
    public void ImpossibleMixedPayloadsEmptyAndDuplicateBoundaryIdsAreRejected()
    {
        var definition = Definition();
        Assert.True(GameplayResponseCompiler.Compile(definition, GameplayResponseCompilationTests.Catalog()).IsSuccess);
        foreach (var filter in new GameplayResponseFilter[]
        {
            definition.Observe with { Kinds = new() { Include = ["resource.**"] } },
            definition.Observe with { Resource = new(), Kinds = new() { Include = ["**"] } },
            definition.Observe with { Boundary = new() },
            definition.Observe with { Boundary = new() { BoundaryIds = ["main", "main"] } },
            definition.Observe with { Boundary = new() { BoundaryIds = default } }
        }) Assert.True(GameplayResponseCompiler.Compile(definition with { Observe = filter }, GameplayResponseCompilationTests.Catalog()).IsFailure);
    }

    [Fact]
    public void JsonRoundTripIsStrictAndAbsentFilterPreservesExistingSerialization()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(Definition(), options);
        Assert.Contains("\"boundaryIds\":[\"main\"]", json);
        var restored = JsonSerializer.Deserialize<GameplayResponseDefinition>(json, options)!;
        Assert.Equal("main", Assert.Single(restored.Observe.Boundary!.BoundaryIds));
        Assert.DoesNotContain("\"boundary\"", JsonSerializer.Serialize(GameplayResponseCompilationTests.Definition(), options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ResponseBoundaryFilter>("""{"boundaryIds":["main"],"executeAnything":true}""", options));
    }
}
