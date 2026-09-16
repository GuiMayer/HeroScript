using System.Text.Json;
using Core.Combat.Flow;
using Core.Determinism;
using Core.Run;
using Core.CardZones;
using Core.Common;
using Xunit;

namespace Core.Tests.Run;

[Trait("Category", "Unit")]
public sealed class DeckTransitionsTests
{
    [Fact]
    public void Create_DuplicateDefinitionsReceiveDistinctStableIdentities()
    {
        var context = DeterministicContext.Create(42, "test-content");

        var first = DeckTransitions.Create(["strike", "strike"], context);
        var second = DeckTransitions.Create(["strike", "strike"], context);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(2, first.Value.State.DrawPileInstanceIds.Distinct().Count());
        Assert.Equal(first.Value.State.DrawPileInstanceIds, second.Value.State.DrawPileInstanceIds);
        Assert.Equal(new[] { "strike", "strike" }, first.Value.State.DrawPile);
    }

    [Fact]
    public void Serialization_PersistsOnlyVersionedInstanceTopology()
    {
        var created = DeckTransitions.Create(
            ["strike", "defend"],
            DeterministicContext.Create(7, "test-content"));
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error : null);

        var json = JsonSerializer.SerializeToElement(
            created.Value.State,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(DeckState.CurrentTopologyVersion, json.GetProperty("topologyVersion").GetInt32());
        Assert.True(json.TryGetProperty("topology", out var topology));
        Assert.True(topology.TryGetProperty("zones", out _));
        Assert.True(topology.TryGetProperty("instances", out _));
        Assert.False(json.TryGetProperty("drawPileInstanceIds", out _));
        Assert.False(json.TryGetProperty("cardInstances", out _));
        Assert.False(json.TryGetProperty("collectionInstanceIds", out _));
        Assert.False(json.TryGetProperty("drawPile", out _));
        Assert.False(json.TryGetProperty("hand", out _));
        Assert.False(json.TryGetProperty("discardPile", out _));
        Assert.False(json.TryGetProperty("exhaustPile", out _));
    }

    [Fact]
    public void TopologyBackedFacade_HasOneRegistryAndStableSerializationRoundTrip()
    {
        var created = DeckTransitions.Create(["strike", "defend"],
            DeterministicContext.Create(42, "test-content")).Value;
        var drawn = DeckTransitions.Draw(created.State, 1, created.Context).Value;
        var encoded = JsonSerializer.Serialize(drawn.State,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var decoded = JsonSerializer.Deserialize<DeckState>(encoded,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(decoded);
        Assert.Equal(drawn.State.HandInstanceIds,
            drawn.State.Topology.GetZone("hand", "$run")!.InstanceIds);
        Assert.Equal(drawn.State.CardInstances.Keys, drawn.State.Topology.Instances.Keys);
        Assert.True(CardZoneTopologyValidator.Validate(decoded.Topology).IsSuccess);
        Assert.Equal(CanonicalJson.ComputeHash(drawn.State.Topology),
            CanonicalJson.ComputeHash(decoded.Topology));
    }

    [Fact]
    public void Deserialization_RejectsUnversionedDefinitionOnlyTopology()
    {
        const string legacy = """{"hand":["strike"]}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DeckState>(legacy));
    }

    [Fact]
    public void ValidateTopology_RejectsIdentityInMultipleZones()
    {
        var created = DeckTransitions.Create(
            ["strike"],
            DeterministicContext.Create(9, "test-content"));
        var instanceId = Assert.Single(created.Value.State.DrawPileInstanceIds);
        var invalid = created.Value.State with { HandInstanceIds = [instanceId] };

        var result = DeckTransitions.ValidateTopology(invalid);

        Assert.True(result.IsFailure);
        Assert.Contains("duplicate zone identities", result.Error);
    }

    [Fact]
    public void Constructor_DefensivelyCopiesMutableCollections()
    {
        var source = new List<string> { "strike" };
        var state = new DeckState { Hand = source };

        source.Add("defend");

        Assert.Equal(new[] { "strike" }, state.Hand);
    }

    [Fact]
    public void Draw_DoesNotModifyInputStateOrContext()
    {
        var state = new DeckState { DrawPile = new[] { "a", "b" } };
        var context = DeterministicContext.Create(42, "test-content");

        var result = DeckTransitions.Draw(state, 1, context);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(new[] { "a", "b" }, state.DrawPile);
        Assert.Empty(state.Hand);
        Assert.Equal(context, result.Value.Context);
        Assert.Equal(new[] { "b" }, result.Value.State.DrawPile);
        Assert.Equal(new[] { "a" }, result.Value.State.Hand);
    }

    [Fact]
    public void Shuffle_SameContextProducesSameStateAndNextContext()
    {
        var state = new DeckState { DiscardPile = new[] { "a", "b", "c", "d" } };
        var context = DeterministicContext.Create(1234, "test-content");

        var first = DeckTransitions.ShuffleDiscardIntoDrawPile(state, context);
        var second = DeckTransitions.ShuffleDiscardIntoDrawPile(state, context);

        Assert.Equal(first.State.DrawPile, second.State.DrawPile);
        Assert.Equal(first.Context, second.Context);
        Assert.Equal(new[] { "a", "b", "c", "d" }, state.DiscardPile);
        Assert.Empty(state.DrawPile);
        Assert.Equal(3UL, first.Context.RandomState.DrawCount);
    }

    [Fact]
    public void Draw_RecyclesDiscardAndCompletesInOneAtomicTransition()
    {
        var initial = new DeckState
        {
            DrawPile = ["a"],
            DiscardPile = ["b", "c", "d"]
        };
        var context = DeterministicContext.Create(321, "test-content");

        var first = DeckTransitions.Draw(initial, 4, context, true, false);
        var repeated = DeckTransitions.Draw(initial, 4, context, true, false);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(first.Value.State.HandInstanceIds, repeated.Value.State.HandInstanceIds);
        Assert.Equal(first.Value.Context, repeated.Value.Context);
        Assert.Equal(4, first.Value.State.HandInstanceIds.Count);
        Assert.Empty(first.Value.State.DrawPileInstanceIds);
        Assert.Empty(first.Value.State.DiscardPileInstanceIds);
        Assert.Empty(initial.HandInstanceIds);
        Assert.Equal(["a"], initial.DrawPile);
        Assert.Equal(["b", "c", "d"], initial.DiscardPile);
    }

    [Fact]
    public void MoveFromHand_IsAtomicWhenDuplicateIsMissing()
    {
        var state = new DeckState { Hand = new[] { "a" } };
        var context = DeterministicContext.Create(1, "test-content");

        var result = DeckTransitions.MoveFromHand(
            state,
            new[] { "a", "a" },
            CardConsumeDestination.Discard,
            context);

        Assert.True(result.IsFailure);
        Assert.Equal(new[] { "a" }, state.Hand);
        Assert.Empty(state.DiscardPile);
    }

    [Fact]
    public void BeginEncounter_ResetOrdered_RebuildsZonesAndDrawsConfiguredHand()
    {
        var context = DeterministicContext.Create(91, "test-content");
        var created = DeckTransitions.Create(["a", "b", "c", "d"], context).Value;
        var drawn = DeckTransitions.Draw(created.State, 2, created.Context).Value;
        var discarded = DeckTransitions.MoveFromHand(
            drawn.State,
            [drawn.State.HandInstanceIds[0].ToString()],
            CardConsumeDestination.Discard,
            drawn.Context).Value;
        var exhausted = DeckTransitions.MoveFromHand(
            discarded.State,
            [discarded.State.HandInstanceIds[0].ToString()],
            CardConsumeDestination.Exhaust,
            discarded.Context).Value;

        var result = DeckTransitions.BeginEncounter(
            exhausted.State,
            Policy() with
            {
                EncounterStart = EncounterDeckStartStrategy.ResetOrdered,
                InitialPlayableCardCount = 2,
                ExhaustPersistence = ExhaustPersistenceStrategy.Encounter
            },
            exhausted.Context);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["a", "b"], result.Value.State.Hand);
        Assert.Equal(["c", "d"], result.Value.State.DrawPile);
        Assert.Empty(result.Value.State.DiscardPile);
        Assert.Empty(result.Value.State.ExhaustPile);
    }

    [Fact]
    public void BeginEncounter_ResetShuffled_IsStableForSameContext()
    {
        var created = DeckTransitions.Create(
            ["a", "b", "c", "d", "e"],
            DeterministicContext.Create(22, "test-content")).Value;
        var policy = Policy() with
        {
            EncounterStart = EncounterDeckStartStrategy.ResetShuffled,
            InitialPlayableCardCount = 3
        };

        var first = DeckTransitions.BeginEncounter(created.State, policy, created.Context);
        var second = DeckTransitions.BeginEncounter(created.State, policy, created.Context);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(first.Value.State.HandInstanceIds, second.Value.State.HandInstanceIds);
        Assert.Equal(first.Value.State.DrawPileInstanceIds, second.Value.State.DrawPileInstanceIds);
        Assert.Equal(first.Value.Context, second.Value.Context);
        Assert.Equal(4UL, first.Value.Context.RandomState.DrawCount);
    }

    [Fact]
    public void EndEncounter_RemovesEncounterCardsAndRestoresEncounterExhaust()
    {
        var created = DeckTransitions.Create(
            ["owned"],
            DeterministicContext.Create(30, "test-content")).Value;
        var ownedId = Assert.Single(created.State.DrawPileInstanceIds);
        var ownedInHand = DeckTransitions.Draw(created.State, 1, created.Context).Value;
        var exhausted = DeckTransitions.MoveFromHand(
            ownedInHand.State,
            [ownedId.ToString()],
            CardConsumeDestination.Exhaust,
            ownedInHand.Context).Value;
        var generated = DeckTransitions.AddGeneratedToHand(
            exhausted.State,
            ["temporary"],
            exhausted.Context).Value;

        var result = DeckTransitions.EndEncounter(generated.State, Policy(), generated.Context);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["owned"], result.Value.State.DrawPile);
        Assert.Empty(result.Value.State.Hand);
        Assert.Empty(result.Value.State.DiscardPile);
        Assert.Empty(result.Value.State.ExhaustPile);
        Assert.Single(result.Value.State.CardInstances);
        Assert.DoesNotContain(result.Value.State.CardInstances.Values, card => card.DefinitionId == "temporary");
        Assert.True(DeckTransitions.ValidateTopology(result.Value.State).IsSuccess);
    }

    [Fact]
    public void EndEncounter_CanPersistGeneratedCardsAndRunExhaust()
    {
        var created = DeckTransitions.Create(
            ["owned"],
            DeterministicContext.Create(31, "test-content")).Value;
        var ownedId = Assert.Single(created.State.DrawPileInstanceIds);
        var ownedInHand = DeckTransitions.Draw(created.State, 1, created.Context).Value;
        var exhausted = DeckTransitions.MoveFromHand(
            ownedInHand.State,
            [ownedId.ToString()],
            CardConsumeDestination.Exhaust,
            ownedInHand.Context).Value;
        var generated = DeckTransitions.AddGeneratedToHand(
            exhausted.State,
            ["generated"],
            exhausted.Context).Value;

        var result = DeckTransitions.EndEncounter(
            generated.State,
            Policy() with
            {
                ExhaustPersistence = ExhaustPersistenceStrategy.Run,
                GeneratedCardPersistence = GeneratedCardPersistenceStrategy.Run
            },
            generated.Context);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["generated"], result.Value.State.DrawPile);
        Assert.Equal(["owned"], result.Value.State.ExhaustPile);
        Assert.All(result.Value.State.CardInstances.Values, card =>
            Assert.Equal(CardInstancePersistence.Run, card.Persistence));
    }

    private static DeckCyclePolicyDefinition Policy() => new()
    {
        DrawPerActivation = 5,
        HandLimit = 10,
        InitialPlayableCardCount = 5,
        ActorScope = FlowActorScope.RunOwner,
        EncounterStart = EncounterDeckStartStrategy.ResetOrdered,
        EncounterCleanup = EncounterDeckCleanupStrategy.ReturnToDrawPile,
        ExhaustPersistence = ExhaustPersistenceStrategy.Encounter,
        GeneratedCardPersistence = GeneratedCardPersistenceStrategy.Encounter,
        EndDiscard = DeckEndDiscardStrategy.NonRetain,
        ShuffleDiscardWhenDrawEmpty = true,
        AllowPartialDraw = true,
        Fatigue = FatigueStrategy.None
    };
}
