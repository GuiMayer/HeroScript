using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Run.Content;

namespace Core.Run.DeckCompartments;

public sealed record DeckCompartmentPreviewRequest(int ExpectedSequence, ulong ExpectedStep, ImmutableArray<DeckCompartmentAllocation> Allocations);
public sealed record DeckCompartmentCardView(Guid CardInstanceId, string DefinitionId, string? CompartmentId, CardPresentationFacts Presentation);
public sealed record DeckCompartmentSnapshot(Guid RunId, int RunSequence, ulong ExpectedStep, string ContentRevision, string ActorId,
    bool Enabled, bool CanChange, string? UnavailableReason, DeckCompartmentProfileDefinition? Profile,
    ImmutableArray<DeckCompartmentAllocation> Allocations, ImmutableArray<DeckCompartmentCardView> Cards);
public sealed record DeckCompartmentPreview(DeckCompartmentSnapshot Before, DeckCompartmentSnapshot After);

public interface IDeckCompartmentQueryService
{
    Result<DeckCompartmentSnapshot> Read(Guid runId);
    Result<DeckCompartmentSnapshot> Read(RunState run);
    Result<DeckCompartmentPreview> Preview(Guid runId, DeckCompartmentPreviewRequest request);
}

public sealed class DeckCompartmentQueryService(IRunQueryService runs, IContentRuntimeResolver runtimes,
    ICardContentCompiler compiler, IEffectiveCardResolver effectiveCards) : IDeckCompartmentQueryService
{
    public Result<DeckCompartmentSnapshot> Read(Guid runId)
    {
        var run = runs.GetRun(runId);
        return run.IsFailure ? Result<DeckCompartmentSnapshot>.Failure(run.Error) : Read(run.Value);
    }

    public Result<DeckCompartmentSnapshot> Read(RunState run)
    {
        var enabled = run.ResolvedMode?.Definition.DeckCompartmentPolicyId != null &&
            run.PlayerEntity?.Component<Core.Combat.Models.DeckCompartmentProfileComponentState>() != null;
        if (!enabled) return Result<DeckCompartmentSnapshot>.Success(new(run.RunId, run.Sequence, run.Determinism.Step,
            run.Determinism.ContentRevision, run.PlayerEntityId, false, false, "Character or mode has no deck compartments", null, [], []));
        var runtime = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure) return Result<DeckCompartmentSnapshot>.Failure(runtime.Error);
        var profile = DeckCompartmentAccess.Profile(run, runtime.Value, run.PlayerEntityId);
        if (profile.IsFailure) return Result<DeckCompartmentSnapshot>.Failure(profile.Error);
        var window = DeckCompartmentAccess.CanChange(run, runtime.Value);
        var allocations = run.DeckCompartments?.Actors.GetValueOrDefault(run.PlayerEntityId)?.Allocations ?? ImmutableSortedDictionary<Guid, string>.Empty;
        var cards = ImmutableArray.CreateBuilder<DeckCompartmentCardView>();
        var eligible = run.Deck.Topology.Instances.Values.Where(c => DeckCompartmentAccess.Owns(run, run.PlayerEntityId, c)).OrderBy(c => c.CardInstanceId).ToArray();
        if (eligible.Length > 512) return Result<DeckCompartmentSnapshot>.Failure("Compartment view exceeds the card limit");
        foreach (var card in eligible)
        {
            var compiled = compiler.Compile(card.DefinitionId, runtime.Value);
            if (compiled.IsFailure) return Result<DeckCompartmentSnapshot>.Failure(compiled.Error);
            var effective = DeckCompartmentComposition.Resolve(run, runtime.Value, compiled.Value, card, effectiveCards);
            if (effective.IsFailure) return Result<DeckCompartmentSnapshot>.Failure(effective.Error);
            var facts = CardBasePresentationProjector.Project(compiled.Value, effective.Value,
                new() { Kind = CardPresentationSubjectKind.Instance, CardInstanceId = card.CardInstanceId, DefinitionId = card.DefinitionId },
                new() { RunId = run.RunId, RunSequence = run.Sequence, ContentRevision = runtime.Value.Manifest.Revision });
            if (facts.IsFailure) return Result<DeckCompartmentSnapshot>.Failure(facts.Error);
            cards.Add(new(card.CardInstanceId, card.DefinitionId, allocations.GetValueOrDefault(card.CardInstanceId), facts.Value));
        }
        return Result<DeckCompartmentSnapshot>.Success(new(run.RunId, run.Sequence, run.Determinism.Step, runtime.Value.Manifest.Revision,
            run.PlayerEntityId, true, window.IsSuccess, window.IsFailure ? window.Error : null, profile.Value,
            allocations.Select(a => new DeckCompartmentAllocation(a.Key, a.Value)).ToImmutableArray(), cards.ToImmutable()));
    }

    public Result<DeckCompartmentPreview> Preview(Guid runId, DeckCompartmentPreviewRequest request)
    {
        var run = runs.GetRun(runId);
        if (run.IsFailure) return Result<DeckCompartmentPreview>.Failure(run.Error);
        if (run.Value.Sequence != request.ExpectedSequence || run.Value.Determinism.Step != request.ExpectedStep)
            return Result<DeckCompartmentPreview>.Failure(RunCommandErrors.VersionConflictPrefix + " stale compartment distribution");
        if (request.Allocations.IsDefault) return Result<DeckCompartmentPreview>.Failure("Complete allocation list is required");
        var runtime = runtimes.Resolve(run.Value.Determinism.ContentRevision, run.Value.ConfigName);
        if (runtime.IsFailure) return Result<DeckCompartmentPreview>.Failure(runtime.Error);
        var plan = DeckCompartmentPlanner.Plan(run.Value, runtime.Value, run.Value.PlayerEntityId, request.Allocations);
        if (plan.IsFailure) return Result<DeckCompartmentPreview>.Failure(plan.Error);
        var before = Read(run.Value);
        if (before.IsFailure) return Result<DeckCompartmentPreview>.Failure(before.Error);
        var after = Read(plan.Value.Candidate);
        return after.IsFailure ? Result<DeckCompartmentPreview>.Failure(after.Error) : Result<DeckCompartmentPreview>.Success(new(before.Value, after.Value));
    }
}
