using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Run.Content;

namespace Core.Run.DeckLoadouts;

public sealed record DeckLoadoutCardView(Guid CardInstanceId, string DefinitionId, string? GroupId, CardPresentationFacts Presentation);
public sealed record DeckLoadoutSnapshot(Guid RunId, string SettingId, int RunSequence, ulong ExpectedStep, string ContentRevision,
    string ActorId, bool Enabled, bool CanChange, string? UnavailableReason, DeckCompositionPolicyDefinition? Policy,
    ActorDeckLoadoutState? Selection, ImmutableArray<DeckLoadoutCardView> Cards);
public sealed record DeckLoadoutPreviewRequest(int ExpectedSequence, ulong ExpectedStep, DeckLoadoutEdit Edit);
public sealed record DeckLoadoutPreview(DeckLoadoutSnapshot Before, DeckLoadoutSnapshot After);
public interface IDeckLoadoutQueryService
{
    Result<DeckLoadoutSnapshot> Read(Guid runId);
    Result<DeckLoadoutSnapshot> Read(RunState run);
    Result<DeckLoadoutPreview> Preview(Guid runId, DeckLoadoutPreviewRequest request);
}

public sealed class DeckLoadoutQueryService(IRunQueryService runs, IContentRuntimeResolver runtimes,
    ICardContentCompiler compiler, IEffectiveCardResolver effectiveCards) : IDeckLoadoutQueryService
{
    public Result<DeckLoadoutSnapshot> Read(Guid runId)
    {
        var run = runs.GetRun(runId);
        return run.IsFailure ? Result<DeckLoadoutSnapshot>.Failure(run.Error) : Read(run.Value);
    }
    public Result<DeckLoadoutSnapshot> Read(RunState run)
    {
        Result<DeckLoadoutSnapshot> Fail(string error) => Result<DeckLoadoutSnapshot>.Failure(error);
        DeckLoadoutSnapshot Snapshot(bool enabled, bool editable, string? reason, DeckCompositionPolicyDefinition? policy,
            ActorDeckLoadoutState? actor, ImmutableArray<DeckLoadoutCardView> cards) => new(run.RunId, run.SettingId,
                run.Sequence, run.Determinism.Step, run.Determinism.ContentRevision, run.PlayerEntityId, enabled, editable, reason, policy, actor, cards);
        if (run.ResolvedMode?.Definition.DeckCompositionPolicyId is not { } policyId)
            return Result<DeckLoadoutSnapshot>.Success(Snapshot(false, false, "This mode has no deck composition", null, null, []));
        var runtime = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure) return Fail(runtime.Error);
        var policy = DeckLoadoutPlanner.Policy(runtime.Value, policyId);
        if (policy.IsFailure) return Fail(policy.Error);
        if (run.DeckLoadout?.Actors.TryGetValue(run.PlayerEntityId, out var actor) != true || actor == null || actor.PolicyId != policyId)
            return Fail("Composition is not initialized for the current character");
        var owned = DeckLoadoutPlanner.Owned(run, run.PlayerEntityId);
        if (owned.Length > 512) return Fail("Composition projection exceeds the technical card budget");
        var cards = ImmutableArray.CreateBuilder<DeckLoadoutCardView>();
        foreach (var instance in owned)
        {
            var compiled = compiler.Compile(instance.DefinitionId, runtime.Value);
            if (compiled.IsFailure) return Fail(compiled.Error);
            var effective = DeckCompartments.DeckCompartmentComposition.Resolve(run, runtime.Value, compiled.Value, instance, effectiveCards);
            if (effective.IsFailure) return Fail(effective.Error);
            var facts = CardBasePresentationProjector.Project(compiled.Value, effective.Value,
                new() { Kind = CardPresentationSubjectKind.Instance, CardInstanceId = instance.CardInstanceId, DefinitionId = instance.DefinitionId },
                new() { RunId = run.RunId, RunSequence = run.Sequence, ContentRevision = runtime.Value.Manifest.Revision });
            if (facts.IsFailure) return Fail(facts.Error);
            cards.Add(new(instance.CardInstanceId, instance.DefinitionId,
                actor.Groups.FirstOrDefault(group => group.Value.Contains(instance.CardInstanceId)).Key, facts.Value));
        }
        var window = DeckLoadoutPlanner.CanChange(run, policy.Value);
        return Result<DeckLoadoutSnapshot>.Success(Snapshot(true, window.IsSuccess, window.IsFailure ? window.Error : null,
            policy.Value, actor, cards.ToImmutable()));
    }
    public Result<DeckLoadoutPreview> Preview(Guid runId, DeckLoadoutPreviewRequest request)
    {
        Result<DeckLoadoutPreview> Fail(string error) => Result<DeckLoadoutPreview>.Failure(error);
        var run = runs.GetRun(runId);
        if (run.IsFailure) return Fail(run.Error);
        if (request == null || request.ExpectedSequence != run.Value.Sequence || request.ExpectedStep != run.Value.Determinism.Step)
            return Fail(RunCommandErrors.VersionConflictPrefix + " stale deck composition");
        var runtime = runtimes.Resolve(run.Value.Determinism.ContentRevision, run.Value.ConfigName);
        if (runtime.IsFailure) return Fail(runtime.Error);
        var plan = DeckLoadoutEdits.Plan(run.Value, runtime.Value, request.Edit);
        if (plan.IsFailure) return Fail(plan.Error);
        var before = Read(run.Value); if (before.IsFailure) return Fail(before.Error);
        var after = Read(plan.Value.Candidate);
        return after.IsFailure ? Fail(after.Error) : Result<DeckLoadoutPreview>.Success(new(before.Value, after.Value));
    }
}
