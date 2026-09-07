using System.Text.Json;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.Run;

public sealed class GameplayCommandCodecTests
{
    private readonly GameplayCommandCodec _codec = GameplayCommandCodec.CreateDefault();

    [Fact]
    public void Decode_NormalizesAndHashesExactlyOnce()
    {
        var payload = JsonSerializer.SerializeToElement(new { count = 3 });
        var commandId = Guid.Parse("10000000-0000-0000-0000-000000000001");

        var result = _codec.Decode(new GameplayCommandEnvelope(
            new RunCommandIdentity(commandId, " draw_cards ", 4, 9),
            payload));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(RunCommandTypes.DrawCards, result.Value.Envelope.Identity.Type);
        Assert.Equal(CanonicalJson.ComputeHash(payload), result.Value.Envelope.Identity.PayloadHash);
        Assert.Equal(new CountCommand(3), result.Value.Payload);
    }

    [Fact]
    public void Decode_RejectsUnknownCommandType()
    {
        var result = _codec.Decode(new GameplayCommandEnvelope(
            new RunCommandIdentity(Guid.NewGuid(), "CHEAT", 0, 0),
            JsonSerializer.SerializeToElement(new { })));

        Assert.True(result.IsFailure);
        Assert.Contains("Unsupported command type", result.Error);
    }

    [Fact]
    public void Decode_RejectsUnknownPayloadProperty()
    {
        var result = _codec.Decode(new GameplayCommandEnvelope(
            new RunCommandIdentity(Guid.NewGuid(), RunCommandTypes.DrawCards, 0, 0),
            JsonSerializer.SerializeToElement(new { count = 1, hiddenMutation = true })));

        Assert.True(result.IsFailure);
        Assert.Contains("hiddenMutation", result.Error);
    }

    [Fact]
    public void Codec_RejectsDuplicateDescriptors()
    {
        var descriptor = new GameplayCommandDescriptor(
            "TEST",
            typeof(EmptyGameplayCommand),
            GameplayCommandRoute.Run);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new GameplayCommandCodec([descriptor, descriptor]));

        Assert.Contains("Duplicate command descriptor", exception.Message);
    }

    [Fact]
    public void HandlerRegistry_RejectsDuplicateHandlers()
    {
        var handler = new TestHandler("TEST");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RunCommandHandlerRegistry([handler, handler]));

        Assert.Contains("Duplicate command handler", exception.Message);
    }

    [Fact]
    public void RejectedHandler_DoesNotMutateInputSnapshot()
    {
        var state = new RunState
        {
            RunId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Sequence = 7,
            Determinism = DeterministicContext.Create(42, "revision")
        };
        var hash = CanonicalJson.ComputeHash(state);
        var handler = new TestHandler("REJECT", reject: true);

        var result = handler.Plan(
            state,
            new EmptyGameplayCommand(),
            new GameplayCommandExecutionContext(
                new RunCommandIdentity(Guid.NewGuid(), "REJECT", 7, 0),
                state.Determinism));

        Assert.True(result.IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(state));
    }

    private sealed class TestHandler : IRunCommandHandler<EmptyGameplayCommand>
    {
        private readonly bool _reject;

        public TestHandler(string type, bool reject = false)
        {
            Descriptor = new GameplayCommandDescriptor(
                type,
                typeof(EmptyGameplayCommand),
                GameplayCommandRoute.Run);
            _reject = reject;
        }

        public GameplayCommandDescriptor Descriptor { get; }

        public Result<RunTransitionPlan> Plan(
            RunState state,
            EmptyGameplayCommand payload,
            GameplayCommandExecutionContext context) =>
            _reject
                ? Result<RunTransitionPlan>.Failure("rejected")
                : Result<RunTransitionPlan>.Success(new RunTransitionPlan
                {
                    PreviousState = state,
                    CandidateState = state,
                    Context = context.Determinism
                });
    }
}
