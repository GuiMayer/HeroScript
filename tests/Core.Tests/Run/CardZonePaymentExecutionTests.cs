using System.Collections.Immutable;
using Core.CardZones;
using Core.Combat.Flow;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed partial class CardPlayExecutorTests
{
    private static Guid PayId(int i) => Guid.Parse($"70000000-0000-4000-8000-{i:000000000000}");
    private static CardZonePaymentDefinition Payment(string source = "spent", int count = 1) => new()
    { PaymentId = "fuel", FlowId = "pay", SourceZoneId = source, TargetZoneId = "ash", Count = count };
    private static CardContentDefinition PaymentCard(params CardZonePaymentDefinition[] payments) => Card([
        new CardCostComponentDefinition { ComponentId = "cost", Costs = new() { Costs = [new() { ResourceId = "energy", Amount = 2 }] }, ZonePayments = payments.ToImmutableArray() },
        new CardEffectComponentDefinition { ComponentId = "effect", Effect = new() { Type = EffectType.DAMAGE, TargetResource = "mana", FlatValue = 5 } },
        Targeting(), Disposition() ]) with { Tags = ["fuel"] };
    private static RunState PaymentRun(string source, int? capacity = null, CardZoneVisibility visibility = CardZoneVisibility.Owner)
    {
        var graph = new CardZoneSystemDefinition { CardZoneSystemId = "payments", FlowOwnerBinding = CardZoneOwnerBinding.ActiveActor,
            Zones = new[] { "playable", "spent", "ash" }.Select(id => new CardZoneDefinition { ZoneId = id,
                OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered, AllowsCardPlay = id == "playable",
                Capacity = id == "ash" ? capacity : null, Visibility = new() { Contents = id == source ? visibility : CardZoneVisibility.Owner,
                    Order = CardZoneOrderVisibility.Visible } }).ToArray(),
            Flows = [new() { FlowId = "pay", AllowedInvocations = [CardZoneFlowInvocation.Effect], Steps = [new()
                { StepId = "transfer", Operation = CardZoneOperation.Move, SourceZoneId = source, TargetZoneId = "ash",
                    Selection = new() { Strategy = CardZoneSelectionStrategy.Explicit } }] },
                new() { FlowId = "card.played", AllowedInvocations = [CardZoneFlowInvocation.CardResolution], Steps = [new()
                { StepId = "dispose", Operation = CardZoneOperation.Move, SourceZoneId = "playable", TargetZoneId = "spent",
                    Selection = new() { Strategy = CardZoneSelectionStrategy.Explicit } }] }] };
        var cards = Enumerable.Range(1, 4).ToDictionary(PayId, i => new CardInstanceState { CardInstanceId = PayId(i),
            DefinitionId = "arcane_drain", OwnerId = i == 4 ? "enemy" : "hero", CreationOrdinal = (ulong)i });
        var zones = new Dictionary<string, CardZoneState>();
        foreach (var owner in new[] { "hero", "enemy" }) foreach (var id in new[] { "playable", "spent", "ash" })
        {
            var address = new CardZoneAddress { ZoneId = id, OwnerId = owner };
            var ids = owner == "enemy" ? (id == source ? new[] { PayId(4) } : []) :
                id == source && id == "playable" ? new[] { PayId(1), PayId(2), PayId(3) } :
                id == "playable" ? new[] { PayId(1) } : id == source ? new[] { PayId(2), PayId(3) } : [];
            zones[address.Key] = new() { Address = address, InstanceIds = ids };
        }
        var run = Run(PayId(1));
        return run with { Deck = new() { Topology = new() { Instances = cards, Zones = zones } }, ResolvedMode = run.ResolvedMode! with { CardZoneSystem = graph } };
    }
    private static CardPlayExecutionRequest PaymentRequest(RunState run, params Guid[] ids) => new()
    { Run = run, Combat = Combat(), CardInstanceId = PayId(1), ActorId = "hero", SelectedTargetIds = ["enemy"],
        PaymentSelections = [new("cost", "fuel", ids.ToImmutableArray())] };

    [Theory]
    [InlineData("spent")]
    [InlineData("playable")]
    public void PaymentUsesOrdinaryTransferAndResourceEffectsInOneImmutableBatch(string source)
    {
        var run = PaymentRun(source); var request = PaymentRequest(run, PayId(2));
        var runtime = Runtime(PaymentCard(Payment(source) with { RequiredTags = ["fuel"] }), Pipeline());
        var executor = Executor(runtime); var input = CanonicalJson.ComputeHash(request);
        var result = executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(1, result.Value.Combat.GetActor("hero")!.GetResource("energy")!.Current);
        Assert.Equal(15, result.Value.Combat.GetActor("enemy")!.GetResource("mana")!.Current);
        Assert.Equal(new[] { PayId(2) }, result.Value.Run!.Deck.Topology.GetZone(new() { ZoneId = "ash", OwnerId = "hero" })!.InstanceIds);
        Assert.Equal(4, result.Value.Run.Deck.Topology.Instances.Count);
        Assert.Single(result.Value.Applications.SelectMany(a => a.CardZoneSteps));
        var command = new CombatActionCommand { ActorId = "hero", ActionType = ActionType.PLAY_CARD, CardInstanceId = PayId(1) };
        var reduced = new CombatActionStateReducer(new CombatOutcomeResolver(), new CardZoneFlowExecutor()).Apply(run, request.Combat,
            new LegalActionCandidate { Command = command, ResolvedCommand = command, SuccessorRun = result.Value.Run!,
                SuccessorCombat = result.Value.Combat, CardPlay = result.Value }, command, new());
        Assert.True(reduced.IsSuccess, reduced.IsFailure ? reduced.Error : null);
        Assert.Contains(PayId(1), reduced.Value.Run.Deck.Topology.GetZone(new() { ZoneId = "spent", OwnerId = "hero" })!.InstanceIds);
        var observed = executor.ExecuteObserved(request, Core.Tests.GameplayEvents.GameplayObservedExecutorTests.Observation(revision: Revision) with { CostReasonId = "cost.payment" });
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(result.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
        Assert.True(observed.Value.Observation.Events.Occurrences.Count(e => e.ReasonId == "cost.payment") >= 2);
        var hashes = new HashSet<string>();
        for (var repeat = 0; repeat < 3; repeat++) hashes.Add(CanonicalJson.ComputeHash(executor.Execute(request).Value));
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void MissingForeignOverlappingOrCapacityInvalidSelectionsNeverConsumeState()
    {
        var runtime = Runtime(PaymentCard(Payment()), Pipeline()); var executor = Executor(runtime);
        var run = PaymentRun("spent");
        CardPlayExecutionRequest[] invalid = [PaymentRequest(run, PayId(4)), PaymentRequest(run, PayId(1)),
            PaymentRequest(run) with { PaymentSelections = null }, PaymentRequest(run, PayId(2), PayId(3)),
            PaymentRequest(PaymentRun("spent", capacity: 0), PayId(2)),
            PaymentRequest(PaymentRun("spent", visibility: CardZoneVisibility.None), PayId(2))];
        foreach (var request in invalid)
        {
            var hash = CanonicalJson.ComputeHash(request); Assert.True(executor.Execute(request).IsFailure);
            Assert.Equal(hash, CanonicalJson.ComputeHash(request));
        }
        var two = Runtime(PaymentCard(Payment(), Payment() with { PaymentId = "other" }), Pipeline());
        var duplicate = PaymentRequest(run, PayId(2)) with { PaymentSelections = [new("cost", "fuel", [PayId(2)]), new("cost", "other", [PayId(2)])] };
        Assert.True(Executor(two).Execute(duplicate).IsFailure);
        var capacityRequest = PaymentRequest(PaymentRun("spent", capacity: 1), PayId(2)) with
            { PaymentSelections = [new("cost", "fuel", [PayId(2)]), new("cost", "other", [PayId(3)])] };
        Assert.True(Executor(two).Execute(capacityRequest).IsFailure);
    }

    [Fact]
    public void ChosenOrderIsPreservedAndDefaultOrderIsNormalizedWithoutAdvancingInputRng()
    {
        var request = PaymentRequest(PaymentRun("spent"), PayId(3), PayId(2)); var hash = CanonicalJson.ComputeHash(request);
        var normalized = Executor(Runtime(PaymentCard(Payment(count: 2)), Pipeline())).Execute(request);
        var chosen = Executor(Runtime(PaymentCard(Payment(count: 2) with { Order = CardPaymentOrder.ChosenOrder }), Pipeline())).Execute(request);
        Assert.True(normalized.IsSuccess); Assert.True(chosen.IsSuccess);
        Assert.Equal(new[] { PayId(2), PayId(3) }, normalized.Value.Evaluation.ZonePayments!.Value[0].CardInstanceIds);
        Assert.Equal(new[] { PayId(3), PayId(2) }, chosen.Value.Evaluation.ZonePayments!.Value[0].CardInstanceIds);
        Assert.Equal(hash, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void PayingTheResolvingCardConsumesItsDispositionExactlyOnce()
    {
        var request = PaymentRequest(PaymentRun("playable"), PayId(1));
        var result = Executor(Runtime(PaymentCard(Payment("playable") with { AllowResolvingCard = true }), Pipeline())).Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var command = new CombatActionCommand { ActorId = "hero", ActionType = ActionType.PLAY_CARD, CardInstanceId = PayId(1) };
        var candidate = new LegalActionCandidate { Command = command, ResolvedCommand = command,
            SuccessorRun = result.Value.Run!, SuccessorCombat = result.Value.Combat, CardPlay = result.Value };
        var reduced = new CombatActionStateReducer(new CombatOutcomeResolver(), new CardZoneFlowExecutor())
            .Apply(request.Run, request.Combat, candidate, command, new());
        Assert.True(reduced.IsSuccess, reduced.IsFailure ? reduced.Error : null);
        Assert.Empty(reduced.Value.CardZoneSteps);
        Assert.Equal(PayId(1).ToString(), reduced.Value.ConsumedCardId);
        Assert.Contains(PayId(1), reduced.Value.Run.Deck.Topology.GetZone(new() { ZoneId = "ash", OwnerId = "hero" })!.InstanceIds);
    }

    [Fact]
    public void AlternativeSelectionIncludesOnlyItsPaymentsAndRejectsUnselectedExtras()
    {
        var card = PaymentCard(Payment() with { CostOptionId = "cards" });
        card = card with { Components = card.Components.Select(c => c is CardCostComponentDefinition cost ? cost with
            { Costs = cost.Costs with { AlternativeCosts = [new() { OptionId = "cards" }, new() { OptionId = "resource", Costs = [new() { ResourceId = "mana", Amount = 1 }] }] } } : c).ToArray() };
        var executor = Executor(Runtime(card, Pipeline())); var request = PaymentRequest(PaymentRun("spent"), PayId(2));
        Assert.True(executor.Execute(request with { CostOptionId = "cards" }).IsSuccess);
        Assert.True(executor.Execute(request with { CostOptionId = "resource", PaymentSelections = null }).IsSuccess);
        Assert.True(executor.Execute(request with { CostOptionId = "resource" }).IsFailure);
    }

    [Fact]
    public void MalformedCostsAndNonExactFlowsAreRejectedRatherThanIgnored()
    {
        foreach (var payment in new[] { Payment() with { Count = 0 }, Payment() with { FlowId = null }, Payment() with { CostOptionId = "absent" } })
            Assert.True(new CardContentCompiler().Compile(PaymentCard(payment)).IsFailure);
        var run = PaymentRun("spent"); var flow = run.ResolvedMode!.CardZoneSystem!.Flows[0];
        Assert.True(CardZonePaymentPlanner.ValidateFlow(Payment(), flow with { Steps = [flow.Steps[0] with { OnOverflow = CardZoneOverflowPolicy.AllowPartial }] }).IsFailure);
        Assert.True(CardZonePaymentPlanner.ValidateFlow(Payment(), flow with { Steps = [flow.Steps[0] with { Condition = "0" }] }).IsFailure);
    }

    [Fact]
    public void FailureAfterThePaymentPlanDiscardsResourceAndZoneCandidates()
    {
        var card = PaymentCard(Payment());
        card = card with { Components = card.Components.Select(c => c is CardEffectComponentDefinition effect ?
            effect with { Effect = effect.Effect with { TargetResource = "missing" } } : c).ToArray() };
        var request = PaymentRequest(PaymentRun("spent"), PayId(2)); var before = CanonicalJson.ComputeHash(request);
        Assert.True(Executor(Runtime(card, Pipeline())).Execute(request).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
        Assert.Equal(3, request.Combat.GetActor("hero")!.GetResource("energy")!.Current);
        Assert.Empty(request.Run.Deck.Topology.GetZone(new() { ZoneId = "ash", OwnerId = "hero" })!.InstanceIds);
    }
}
