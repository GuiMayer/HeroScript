using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;

namespace Core.Run.Content;

public sealed record CardPaymentPreviewRequest(int ExpectedSequence, ulong ExpectedStep, CombatGameplayCommand Command);
public sealed record CardPaymentPreview(CardInspectionVersion Version, CombatGameplayCommand Command, bool IsLegal,
    CardPlayEvaluation Evaluation, ImmutableArray<CardPresentationFacts> Cards,
    ImmutableArray<Core.Effects.EffectApplicationRecord> Applications, bool OutcomeUncertain = false);
public interface ICardPaymentQueryService
{
    Result<CardPaymentPreview> Preview(Guid combatId, CardPaymentPreviewRequest request);
}

/// <summary>One captured revision for requirements, visible faces and executable preview. Never commits.</summary>
public sealed class CardPaymentQueryService(IRunQueryService runs, ICardInspectionService inspections,
    ICardPresentationQueryService presentations) : ICardPaymentQueryService
{
    public Result<CardPaymentPreview> Preview(Guid combatId, CardPaymentPreviewRequest request)
    {
        Result<CardPaymentPreview> Fail(string message) => Result<CardPaymentPreview>.Failure(message);
        if (request?.Command == null || request.Command.ActionType.HasValue && request.Command.ActionType != ActionType.PLAY_CARD ||
            request.Command.CardInstanceId is not { } cardId || cardId == Guid.Empty || string.IsNullOrWhiteSpace(request.Command.ActorId))
            return Fail("A normal card command is required for payment preview");
        var loaded = runs.GetRunByCombat(combatId);
        if (loaded.IsFailure) return Fail(loaded.Error);
        var run = loaded.Value; var encounter = run.GetEncounter(combatId);
        if (encounter == null || encounter.Resolved || run.ActiveEncounterId != combatId)
            return Fail("Payment preview requires the active encounter");
        var combat = encounter.Combat;
        if (run.Sequence != request.ExpectedSequence || combat.Determinism.Step != request.ExpectedStep)
            return Fail(RunCommandErrors.VersionConflictPrefix + " stale payment selection");
        // The ordinary visible projection is the viewer boundary. Never synthesize hidden faces.
        var faces = presentations.Read(run, combat: combat, actorId: request.Command.ActorId);
        if (faces.IsFailure) return Fail(faces.Error);
        var visible = faces.Value.Cards.Where(c => c.Identity.CardInstanceId.HasValue)
            .ToDictionary(c => c.Identity.CardInstanceId!.Value);
        if (!visible.ContainsKey(cardId)) return Fail("Payment card is not visible");
        var command = request.Command with { ActionType = ActionType.PLAY_CARD };
        var inspected = inspections.Inspect(run, combat, new()
        {
            CombatId = combatId, CardInstanceId = cardId, ActorId = command.ActorId,
            SelectedTargetIds = command.TargetIds is { Count: > 0 } ? command.TargetIds : command.TargetId == null ? [] : [command.TargetId],
            CostOptionId = command.CostOptionId, PaymentSelections = command.PaymentSelections, MaximumDetail = InspectionDetailLevel.Resolved
        });
        if (inspected.IsFailure) return Fail(inspected.Error);
        var evaluation = inspected.Value.Evaluation;
        var requirements = (evaluation.PaymentRequirements ?? []).Select(requirement => requirement with
        {
            EligibleCardIds = requirement.EligibleCardIds.Where(visible.ContainsKey).ToImmutableArray(),
            UnavailableReason = requirement.EligibleCardIds.Any(id => !visible.ContainsKey(id))
                ? "Payment source is not visible to the current viewer" : requirement.UnavailableReason
        }).ToImmutableArray();
        var ids = requirements.SelectMany(r => r.EligibleCardIds).Append(cardId).Distinct().Order().ToArray();
        var isLegal = inspected.Value.IsPlayable && requirements.All(r => r.CostOptionId != null && r.CostOptionId != command.CostOptionId || r.UnavailableReason == null);
        return Result<CardPaymentPreview>.Success(new(inspected.Value.Version, command, isLegal,
            evaluation with { PaymentRequirements = requirements, ZonePayments = null }, ids.Select(id => visible[id]).ToImmutableArray(),
            inspected.Value.PreviewApplications.Select(application => application with
            {
                CardZoneSteps = [],
                CardInstanceIds = application.CardInstanceIds.Where(visible.ContainsKey).Order().ToImmutableArray()
            }).ToImmutableArray(), inspected.Value.PreviewScope.DependsOnRandomInputs));
    }
}
