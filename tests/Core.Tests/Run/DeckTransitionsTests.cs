using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.Run;

[Trait("Category", "Unit")]
public sealed class DeckTransitionsTests
{
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
