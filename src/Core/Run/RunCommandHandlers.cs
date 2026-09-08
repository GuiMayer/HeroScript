using System.Text.Json;
using Core.Common;

namespace Core.Run;

/// <summary>
/// Isolates the existing run reducers while they are split into smaller
/// category services. The fresh engine has no commit store or event bus, so it
/// can only calculate a candidate snapshot.
/// </summary>
internal sealed class IsolatedRunCommandHandler : IRunCommandHandler
{
    private readonly Func<RunManager> _createEngine;

    public IsolatedRunCommandHandler(
        GameplayCommandDescriptor descriptor,
        Func<RunManager> createEngine)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _createEngine = createEngine ?? throw new ArgumentNullException(nameof(createEngine));
        if (descriptor.Route != GameplayCommandRoute.Run)
            throw new ArgumentException("An isolated run handler requires a run command", nameof(descriptor));
    }

    public GameplayCommandDescriptor Descriptor { get; }

    public Result<RunTransitionPlan> Plan(
        RunState state,
        object payload,
        GameplayCommandExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Identity.ExpectedSequence != state.Sequence ||
            context.Identity.ExpectedStep != state.Determinism.Step)
            return Result<RunTransitionPlan>.Failure("Command handler received a stale run snapshot");

        var engine = _createEngine();
        var hydrated = engine.HydrateForReplay(state);
        if (hydrated.IsFailure)
            return Result<RunTransitionPlan>.Failure(hydrated.Error);

        var json = JsonSerializer.SerializeToElement(payload, Descriptor.PayloadType);
        var transition = engine.ExecuteCommandTransition(state.RunId, Descriptor.Type, json);
        if (transition.IsFailure)
            return Result<RunTransitionPlan>.Failure(transition.Error);
        var candidate = engine.GetRun(state.RunId);
        if (candidate.IsFailure)
            return Result<RunTransitionPlan>.Failure(candidate.Error);

        var planned = candidate.Value.Sequence == state.Sequence
            ? candidate.Value with { Determinism = candidate.Value.Determinism.AdvanceStep() }
            : candidate.Value with { Sequence = state.Sequence };
        return Result<RunTransitionPlan>.Success(new RunTransitionPlan
        {
            PreviousState = state,
            CandidateState = planned,
            Context = planned.Determinism,
            Value = payload
        });
    }
}

internal static class RunCommandHandlers
{
    public static RunCommandHandlerRegistry Create(
        IGameplayCommandCodec codec,
        Func<RunManager> createEngine)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(createEngine);
        return new RunCommandHandlerRegistry(codec.Descriptors
            .Where(descriptor => descriptor.Route == GameplayCommandRoute.Run)
            .Select(descriptor => (IRunCommandHandler)new IsolatedRunCommandHandler(
                descriptor,
                createEngine)));
    }
}
