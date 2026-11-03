using System.Collections.Immutable;
using Core.CardZones;
using Core.Common;
using Core.Content;

namespace Core.Run.Content;

public sealed record CardZonePaymentQuote(ImmutableArray<CardPaymentRequirement> Requirements,
    ImmutableArray<ResolvedCardZonePayment> Payments, ImmutableArray<string> Failures);

/// <summary>Read-only quotation. Authored transfers are executed later as ordinary prefix effects.</summary>
public static class CardZonePaymentPlanner
{
    public static Result<CardZonePaymentQuote> Quote(EffectiveCardDefinition card, RunState run, string actorId,
        string? costOptionId, ImmutableArray<CardPaymentSelection>? selections, IContentRuntimeResolver runtimes)
    {
        Result<CardZonePaymentQuote> Fail(string reason) => Result<CardZonePaymentQuote>.Failure(reason);
        var components = card.All<CardCostComponentDefinition>().ToArray();
        if (components.Any(c => c.ZonePayments.HasValue && c.ZonePayments.Value.IsDefault)) return Fail("Uninitialized zone payment definitions");
        var definitions = components.SelectMany(component =>
            (component.ZonePayments ?? []).Select(payment => (component.ComponentId, Payment: payment))).ToArray();
        var submitted = selections ?? [];
        if (submitted.IsDefault || definitions.Length > 64 || submitted.Length > 64 || submitted.Any(s => s == null) ||
            submitted.Select(s => (s.ComponentId, s.PaymentId)).Distinct().Count() != submitted.Length)
            return Fail("Invalid or duplicate card payment selections");
        if (definitions.Any(d => d.Payment == null || d.Payment.Validate().IsFailure)) return Fail("Invalid payment definition");
        var active = definitions.Where(d => d.Payment.CostOptionId == null || d.Payment.CostOptionId == costOptionId).ToArray();
        if (submitted.Any(s => !active.Any(d => d.ComponentId == s.ComponentId && d.Payment.PaymentId == s.PaymentId)))
            return Fail("Card payment selection is not part of the chosen cost");
        if (definitions.Length == 0) return Result<CardZonePaymentQuote>.Success(new([], [], []));
        if (run.ResolvedMode?.CardZoneSystem is not { } graph)
            return Fail("Card payment requires an authored zone graph");
        var compiled = CardZoneSystemCompiler.Compile(graph);
        if (compiled.IsFailure) return Fail(compiled.Error);
        var context = new CardZoneFlowContext { Invocation = CardZoneFlowInvocation.Effect, FlowOwnerId = actorId,
            RunOwnerId = "$run", ActiveActorId = actorId, RunPlayerActorId = run.PlayerEntityId, SourceActorId = actorId,
            TargetActorId = actorId, RunSnapshot = run, ContentRevision = run.Determinism.ContentRevision, ConfigName = run.ConfigName };
        var metadata = new RevisionedCardZoneCardMetadataResolver(runtimes, new CardContentCompiler(), new EffectiveCardResolver());
        var requirements = ImmutableArray.CreateBuilder<CardPaymentRequirement>();
        var payments = ImmutableArray.CreateBuilder<ResolvedCardZonePayment>();
        var failures = ImmutableArray.CreateBuilder<string>();
        var used = new HashSet<Guid>();
        var candidate = run.Deck.Topology; var random = run.Determinism;
        foreach (var (componentId, payment) in definitions)
        {
            if (payment == null || payment.Validate().IsFailure || string.IsNullOrWhiteSpace(payment.FlowId))
                return Fail("Invalid card payment definition");
            if (!compiled.Value.Flows.TryGetValue(payment.FlowId, out var flow))
                return Fail($"Card payment flow not found: {payment.FlowId}");
            var validFlow = ValidateFlow(payment, flow);
            if (validFlow.IsFailure) return Fail(validFlow.Error);
            var step = flow.Steps[0];
            var source = Address(payment.SourceZoneId, step.SourceOwner, actorId);
            var target = Address(payment.TargetZoneId, step.TargetOwner, actorId);
            if (source == null || target == null) return Fail("Payment owners must be resolved from the acting participant or shared scope");
            var sourceZone = run.Deck.Topology.GetZone(source);
            var targetZone = run.Deck.Topology.GetZone(target);
            if (sourceZone == null || targetZone == null) return Fail("Payment source or destination zone is unavailable");
            var sourceDefinition = compiled.Value.GetZone(payment.SourceZoneId).Value;
            var owns = source.OwnerId == actorId || source.OwnerId == "$run" && actorId == run.PlayerEntityId;
            var visible = sourceDefinition.Visibility.Contents == CardZoneVisibility.All ||
                sourceDefinition.Visibility.Contents == CardZoneVisibility.Owner && owns;
            var eligible = ImmutableArray.CreateBuilder<Guid>();
            if (visible)
                foreach (var id in sourceZone.InstanceIds)
                {
                    if (id == card.CardInstanceId && !payment.AllowResolvingCard) continue;
                    if (!payment.RequiredTags.IsEmpty || !payment.ExcludedTags.IsEmpty)
                    {
                        var tags = metadata.ResolveTags(run.Deck.Topology.Instances[id], context);
                        if (tags.IsFailure) return Fail(tags.Error);
                        if (!payment.RequiredTags.All(tag => tags.Value.Contains(tag, StringComparer.Ordinal)) ||
                            payment.ExcludedTags.Any(tag => tags.Value.Contains(tag, StringComparer.Ordinal))) continue;
                    }
                    eligible.Add(id);
                }
            var ids = eligible.ToImmutable();
            var reason = !visible ? "Manual payment cannot select hidden card identities" :
                ids.Length < payment.Count ? "Insufficient eligible cards" : null;
            requirements.Add(new(componentId, payment.PaymentId, payment.CostOptionId, payment.SourceZoneId,
                payment.TargetZoneId, payment.Count, payment.AllowResolvingCard, payment.Order ?? CardPaymentOrder.SourceOrder,
                sourceDefinition.Visibility.Order == CardZoneOrderVisibility.Visible ? ids : ids.OrderBy(id => id).ToImmutableArray(), reason));
            if (payment.CostOptionId != null && payment.CostOptionId != costOptionId) continue;
            if (reason != null) failures.Add($"Payment {componentId}/{payment.PaymentId}: {reason}");
            var selection = submitted.FirstOrDefault(s => s.ComponentId == componentId && s.PaymentId == payment.PaymentId);
            if (selection == null)
            {
                failures.Add($"PAYMENT_SELECTION_REQUIRED: {componentId}/{payment.PaymentId}"); continue;
            }
            if (selection.CardInstanceIds.IsDefault || selection.CardInstanceIds.Length != payment.Count ||
                selection.CardInstanceIds.Distinct().Count() != payment.Count || selection.CardInstanceIds.Any(id => !ids.Contains(id)))
            {
                failures.Add($"Invalid card identities or count for payment {componentId}/{payment.PaymentId}"); continue;
            }
            if (selection.CardInstanceIds.Any(id => !used.Add(id)))
            {
                failures.Add("An instance cannot pay more than one card cost"); continue;
            }
            var selected = payment.Order == CardPaymentOrder.ChosenOrder ? selection.CardInstanceIds :
                (sourceDefinition.Ordering == CardZoneOrdering.Ordered ? sourceZone.InstanceIds.AsEnumerable() : sourceZone.InstanceIds.OrderBy(id => id))
                .Where(selection.CardInstanceIds.Contains).ToImmutableArray();
            // Simulate the same primitive over local snapshots to account for shared destination capacity.
            var moved = new CardZoneFlowExecutor().Execute(compiled.Value, candidate, random, payment.FlowId,
                context with { CardInstanceIds = selected, Variables = new Dictionary<string, double>
                    { ["requestedCount"] = payment.Count, ["requestedCardCount"] = payment.Count } });
            if (moved.IsFailure) { failures.Add($"Payment {componentId}/{payment.PaymentId}: {moved.Error}"); continue; }
            candidate = moved.Value.State; random = moved.Value.Context;
            payments.Add(new(componentId, payment.PaymentId, payment.FlowId, selected, selected.Contains(card.CardInstanceId)));
        }
        return Result<CardZonePaymentQuote>.Success(new(requirements.ToImmutable(), payments.ToImmutable(), failures.ToImmutable()));
    }

    public static Result ValidateFlow(CardZonePaymentDefinition payment, CardZoneFlowDefinition flow)
    {
        if (!flow.AllowedInvocations.Contains(CardZoneFlowInvocation.Effect) || flow.ActorScope != CardZoneFlowActorScope.Any || flow.Steps.Count != 1)
            return Result.Failure("Payment flow must allow Effect, any acting participant and exactly one transfer");
        var step = flow.Steps[0]; var selection = step.Selection;
        return Address(payment.SourceZoneId, step.SourceOwner, "validation") != null && Address(payment.TargetZoneId, step.TargetOwner, "validation") != null &&
            step.Operation == CardZoneOperation.Move && step.SourceZoneId == payment.SourceZoneId && step.TargetZoneId == payment.TargetZoneId &&
            string.IsNullOrWhiteSpace(step.Condition) && step.OnInsufficient == CardZoneInsufficientPolicy.RejectTransaction &&
            step.OnOverflow == CardZoneOverflowPolicy.RejectTransaction && selection.Strategy == CardZoneSelectionStrategy.Explicit &&
            selection.InstanceIds.Count == 0 && selection.InstanceSetId == null && selection.Count == null &&
            selection.CountFormula == null && selection.TargetZoneCount == null && selection.TargetZoneCountFormula == null &&
            selection.Condition == null && selection.RequiredTags.Count == 0 && selection.ExcludedTags.Count == 0 && selection.DefinitionIds.Count == 0
            ? Result.Success() : Result.Failure("Payment flow must be an unconditional exact explicit transfer with rejecting capacity policies");
    }

    private static CardZoneAddress? Address(string zoneId, CardZoneOwnerBinding binding, string actorId)
    {
        var owner = binding switch { CardZoneOwnerBinding.Global => "$global", CardZoneOwnerBinding.RunOwner => "$run",
            CardZoneOwnerBinding.FlowOwner or CardZoneOwnerBinding.ActiveActor or CardZoneOwnerBinding.SourceActor or
                CardZoneOwnerBinding.TargetActor => actorId, _ => null };
        return owner == null ? null : new() { ZoneId = zoneId, OwnerId = owner };
    }
}
