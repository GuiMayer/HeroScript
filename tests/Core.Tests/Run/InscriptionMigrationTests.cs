using System.Collections.Immutable;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Branching;
using Core.Run.Loadouts;
using Xunit;

namespace Core.Tests.Run;

public sealed class InscriptionMigrationTests
{
    [Fact]
    public void CompatibleRevisionRebindsDefinitionsWithoutChangingAcquisitionOrInputContext()
    {
        var (run, _, _) = Equipped();
        var before = CanonicalJson.ComputeHash(run);
        var runtime = Target(cost: 4);
        var prepared = LoadoutMigration.Prepare(run, runtime);
        Assert.True(prepared.IsSuccess, prepared.IsFailure ? prepared.Error : null);
        var item = prepared.Value.Loadouts!.Inventories.Values.SelectMany(inventory => inventory.Items).Single();
        Assert.Equal(runtime.Manifest.Revision, item.DefinitionRevision);
        Assert.Equal(run.Determinism.ContentRevision, item.AcquisitionRevision);
        Assert.Equal(run.Determinism, prepared.Value.Determinism);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.Equal(run.Loadouts!.Actors["alice"].Assignments, prepared.Value.Loadouts.Actors["alice"].Assignments);
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("slots")]
    [InlineData("tags")]
    [InlineData("scope")]
    [InlineData("removed")]
    [InlineData("reject")]
    [InlineData("cost")]
    public void IncompatibleMigrationRejectsWholeDraftWithoutUnequipping(string change)
    {
        var (run, _, _) = Equipped();
        var before = CanonicalJson.ComputeHash(run);
        var policy = LoadoutTestContent.Policy() with { RevisionMigration = LoadoutRevisionMigration.RebindCompatible };
        policy = change switch
        {
            "capacity" => policy with { Capacity = 2 },
            "slots" => policy with { Slots = [new() { SlotId = "two" }] },
            "tags" => policy with { Slots = [new() { SlotId = "one", RequiredTags = ["foreign"] }] },
            "scope" => policy with { InventoryScope = InscriptionInventoryScope.Run },
            "removed" => policy with { InscriptionIds = ["not-owned"] },
            "reject" => policy with { RevisionMigration = LoadoutRevisionMigration.RejectRevisionChange },
            _ => policy
        };
        Assert.True(LoadoutMigration.Prepare(run, Target(cost: change == "cost" ? 7 : 3, policy: policy)).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.Single(run.Loadouts!.Actors["alice"].Assignments);
    }

    [Fact]
    public void CurrentOpenOfferMustBeClosedBeforeRebindingContent()
    {
        var (run, _, _) = Equipped();
        var offer = new InscriptionOfferState { OfferId = Guid.NewGuid(), ActorId = "alice", NodeId = "reward",
            StationId = "station", ContentRevision = run.Determinism.ContentRevision, Fingerprint = "captured",
            Options = ["focus"], MaximumPurchases = 1 };
        run = run with { CurrentNodeId = "reward", Loadouts = run.Loadouts! with
            { Offers = ImmutableSortedDictionary<Guid, InscriptionOfferState>.Empty.Add(offer.OfferId, offer) } };
        var before = CanonicalJson.ComputeHash(run);
        Assert.True(LoadoutMigration.Prepare(run, Target()).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        var closed = run with { Loadouts = run.Loadouts! with { Offers = run.Loadouts.Offers!.SetItem(offer.OfferId, offer with { Closed = true }) } };
        Assert.True(LoadoutMigration.Prepare(closed, Target()).IsSuccess);
    }

    [Fact]
    public void TransferAndRosterExitHaveExplicitOwnershipPolicyAndPreserveInstanceIdentity()
    {
        var policy = LoadoutTestContent.Policy() with { AllowTransfers = true };
        var (run, runtime, planner) = InscriptionLoadoutTests.Setup(policy);
        run = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "reward").Value;
        var id = InscriptionLoadoutTests.Item(run);
        var equipped = planner.Plan(run, "alice", new Dictionary<string, Guid> { ["one"] = id }).Value.Candidate;
        Assert.True(LoadoutOwnershipTransitions.Transfer(equipped, runtime, new("alice", "bob", id)).IsFailure);
        Assert.True(LoadoutOwnershipTransitions.RemoveActor(run, runtime, "alice").IsFailure);
        var moved = LoadoutOwnershipTransitions.Transfer(run, runtime, new("alice", "bob", id));
        Assert.True(moved.IsSuccess, moved.IsFailure ? moved.Error : null);
        Assert.Equal(id, InscriptionLoadoutTests.Item(moved.Value, "bob"));
        Assert.Empty(moved.Value.Loadouts!.Inventories[moved.Value.Loadouts.InventoryId(run.RunId, "alice")].Items);
        Assert.True(LoadoutOwnershipTransitions.RemoveActor(moved.Value, runtime, "alice").IsSuccess);
        Assert.Equal(run.Determinism.RandomState, moved.Value.Determinism.RandomState);
        Assert.True(LoadoutOwnershipTransitions.Transfer(run, LoadoutTestContent.Runtime(), new("alice", "bob", id)).IsFailure);
    }

    [Fact]
    public void SharedBranchAfterMigrationHasIndependentRegistryAndKeepsProvenance()
    {
        var policy = LoadoutTestContent.Policy(InscriptionInventoryScope.Run) with { RevisionMigration = LoadoutRevisionMigration.RebindCompatible };
        var (run, runtime, planner) = InscriptionLoadoutTests.Setup(policy);
        run = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "reward").Value;
        run = planner.Plan(run, "alice", new Dictionary<string, Guid> { ["one"] = InscriptionLoadoutTests.Item(run) }).Value.Candidate;
        var target = Target(policy: policy);
        var migrated = LoadoutMigration.Prepare(run, target).Value with { Determinism = run.Determinism.WithContentRevision(target.Manifest.Revision), Sequence = 1 };
        var hash = CanonicalJson.ComputeHash(migrated);
        var child = RunBranchTransitions.Create(migrated, new(migrated.RunId, 1, "equipment", hash));
        Assert.True(child.IsSuccess, child.IsFailure ? child.Error : null);
        Assert.True(child.Value.Loadouts!.Validate(child.Value.RunId, policy).IsSuccess);
        Assert.NotEqual(migrated.Loadouts!.Inventories.Keys.Single(), child.Value.Loadouts.Inventories.Keys.Single());
        Assert.Equal(InscriptionLoadoutTests.Item(migrated), InscriptionLoadoutTests.Item(child.Value));
        Assert.Equal(hash, CanonicalJson.ComputeHash(migrated));
    }

    private static (RunState Run, ContentRuntime Runtime, LoadoutChangePlanner Planner) Equipped()
    {
        var (run, runtime, planner) = InscriptionLoadoutTests.Setup();
        run = InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "reward").Value;
        return (planner.Plan(run, "alice", new Dictionary<string, Guid> { ["one"] = InscriptionLoadoutTests.Item(run) }).Value.Candidate, runtime, planner);
    }
    private static ContentRuntime Target(float cost = 3, LoadoutPolicyDefinition? policy = null)
    {
        var bundle = LoadoutTestContent.Bundle(LoadoutTestContent.Inscription(cost: cost), policy ?? LoadoutTestContent.Policy() with
            { RevisionMigration = LoadoutRevisionMigration.RebindCompatible });
        return ContentRuntime.Create(bundle with { Manifest = bundle.Manifest with { Revision = new string('b', 64) } }).Value;
    }
}
