using System.Text.Json;
using Core.Determinism;
using Core.Run;
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
        Assert.True(json.TryGetProperty("drawPileInstanceIds", out _));
        Assert.True(json.TryGetProperty("cardInstances", out _));
        Assert.False(json.TryGetProperty("drawPile", out _));
        Assert.False(json.TryGetProperty("hand", out _));
        Assert.False(json.TryGetProperty("discardPile", out _));
        Assert.False(json.TryGetProperty("exhaustPile", out _));
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
}
