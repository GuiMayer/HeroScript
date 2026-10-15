using System.Collections.Immutable;
using System.Text.Json;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Loadouts;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class InscriptionLoadoutTests
{
    internal static (RunState Run, ContentRuntime Runtime, LoadoutChangePlanner Planner) Setup(LoadoutPolicyDefinition? policy = null)
    {
        var runtime = ContentRuntime.Create(LoadoutTestContent.Bundle(policy: policy)).Value;
        var run = new RunState { RunId = Guid.Parse("00000000-0000-0000-0000-000000000077"),
            Determinism = DeterministicContext.Create(77, runtime.Manifest.Revision),
            ResolvedMode = new() { Definition = new() { ModeId = "test", LoadoutPolicyId = "loadout", EffectResolutionPolicyId = "attributes", CalculationPipelineIds = ["critical"] } } };
        var initialized = InscriptionAcquisition.Initialize(run, runtime, ["alice", "bob"]);
        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(value => value.Resolve(runtime.Manifest.Revision, "default")).Returns(Result<ContentRuntime>.Success(runtime));
        return (initialized.Value, runtime, new(runtimes.Object));
    }

    internal static Guid Item(RunState run, string actor = "alice", int index = 0) => run.Loadouts!.Inventories[run.Loadouts.InventoryId(run.RunId, actor)].Items[index].InstanceId;

    [Theory]
    [InlineData(InscriptionInventoryScope.Actor)]
    [InlineData(InscriptionInventoryScope.Run)]
    public void OwnershipScopeIsExplicitAndEquipmentIsNeverShared(InscriptionInventoryScope scope)
    {
        var (run, runtime, planner) = Setup(LoadoutTestContent.Policy(scope));
        var granted = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "reward:one");
        Assert.True(granted.IsSuccess);
        var item = Item(granted.Value);
        var bob = planner.Plan(granted.Value, "bob", new Dictionary<string, Guid> { ["one"] = item });
        Assert.Equal(scope == InscriptionInventoryScope.Run, bob.IsSuccess);
        if (bob.IsSuccess)
        {
            Assert.Empty(bob.Value.Candidate.Loadouts!.Actors["alice"].Assignments);
            Assert.True(planner.Plan(bob.Value.Candidate, "alice", new Dictionary<string, Guid> { ["one"] = item }).IsFailure);
        }
        Assert.Empty(run.Loadouts!.Inventories.Values.SelectMany(value => value.Items));
    }

    [Fact]
    public void CompleteSwapUsesFinalBudgetAndPreservesInputRngCardsAndProvenance()
    {
        var (run, runtime, planner) = Setup(LoadoutTestContent.Policy() with { AllowDuplicateDefinitions = true });
        run = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "reward:one").Value;
        run = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "reward:two").Value;
        var first = Item(run); var second = Item(run, index: 1);
        var equipped = planner.Plan(run, "alice", new Dictionary<string, Guid> { ["one"] = first, ["two"] = second }).Value.Candidate;
        var before = CanonicalJson.ComputeHash(equipped);
        var swapped = planner.Plan(equipped, "alice", new Dictionary<string, Guid> { ["one"] = second, ["two"] = first });
        Assert.True(swapped.IsSuccess, swapped.IsFailure ? swapped.Error : null);
        Assert.Equal(6, swapped.Value.UsedCapacity);
        Assert.Equal(before, CanonicalJson.ComputeHash(equipped));
        Assert.Equal(equipped.Determinism.RandomState, swapped.Value.Candidate.Determinism.RandomState);
        Assert.Equal(equipped.Determinism.IdSequence, swapped.Value.Candidate.Determinism.IdSequence);
        Assert.Equal(CanonicalJson.ComputeHash(run.Deck), CanonicalJson.ComputeHash(swapped.Value.Candidate.Deck));
        var again = planner.Plan(equipped, "alice", new Dictionary<string, Guid> { ["two"] = first, ["one"] = second });
        Assert.Equal(CanonicalJson.ComputeHash(swapped.Value.Candidate), CanonicalJson.ComputeHash(again.Value.Candidate));
        Assert.True(planner.Plan(equipped, "alice", new Dictionary<string, Guid> { ["one"] = first, ["two"] = first }).IsFailure);
    }

    [Fact]
    public void InvalidBudgetDuplicatesSlotAndCharacterCannotPartiallyApply()
    {
        var (run, runtime, planner) = Setup();
        run = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "one").Value;
        run = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "two").Value;
        var before = CanonicalJson.ComputeHash(run);
        Assert.True(planner.Plan(run, "alice", new Dictionary<string, Guid> { ["one"] = Item(run), ["two"] = Item(run, index: 1) }).IsFailure);
        Assert.True(planner.Plan(run, "alice", new Dictionary<string, Guid> { ["missing"] = Item(run) }).IsFailure);
        Assert.True(planner.Plan(run, "ghost", new Dictionary<string, Guid> { ["one"] = Item(run) }).IsFailure);
        Assert.True(planner.Plan(run, "alice", new Dictionary<string, Guid> { ["one"] = Guid.NewGuid() }).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        var small = Setup(LoadoutTestContent.Policy() with { Capacity = 2 });
        var owned = InscriptionAcquisition.Grant(small.Run, small.Runtime, "alice", "focus", "one").Value;
        Assert.True(small.Planner.Plan(owned, "alice", new Dictionary<string, Guid> { ["one"] = Item(owned) }).IsFailure);
    }

    [Fact]
    public void AcquisitionIdsReplayAndSerializationAreDeterministicAndOptOutIsAbsent()
    {
        var (run, runtime, _) = Setup();
        var first = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "one").Value;
        var second = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "one").Value;
        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(second));
        Assert.Equal(run.Determinism.RandomState, first.Determinism.RandomState);
        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(first))!));
        Assert.DoesNotContain("Loadouts", JsonSerializer.Serialize(new RunState()));
        Assert.True(InscriptionAcquisition.Grant(run, runtime, "unknown", "focus", "one").IsFailure);
        Assert.True(InscriptionAcquisition.Grant(run, runtime, "alice", "missing", "one").IsFailure);
        Assert.True(InscriptionAcquisition.Initialize(first, runtime, ["alice"]).IsFailure);
    }

    [Fact]
    public void EquipmentWindowAndSlotCompatibilityAreDataDriven()
    {
        var (run, runtime, planner) = Setup();
        run = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "one").Value;
        Assert.True(planner.Plan(run with { ActiveEncounterId = Guid.NewGuid() }, "alice", new Dictionary<string, Guid> { ["one"] = Item(run) }).IsFailure);
        var restricted = Setup(LoadoutTestContent.Policy() with { Slots = [new() { SlotId = "one", RequiredTags = ["fire"] }] });
        var owned = InscriptionAcquisition.Grant(restricted.Run, restricted.Runtime, "alice", "focus", "one").Value;
        Assert.True(restricted.Planner.Plan(owned, "alice", new Dictionary<string, Guid> { ["one"] = Item(owned) }).IsFailure);
        var combat = Setup(LoadoutTestContent.Policy() with { EquipmentWindows = [LoadoutEquipmentWindow.Combat] });
        owned = InscriptionAcquisition.Grant(combat.Run, combat.Runtime, "alice", "focus", "one").Value;
        Assert.True(combat.Planner.Plan(owned, "alice", new Dictionary<string, Guid> { ["one"] = Item(owned) }).IsFailure);
        Assert.True(combat.Planner.Plan(owned with { ActiveEncounterId = Guid.NewGuid() }, "alice", new Dictionary<string, Guid> { ["one"] = Item(owned) }).IsSuccess);
    }
}
