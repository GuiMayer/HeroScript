using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.Combat.Models;
using Core.Determinism;
using Core.Meta;
using Core.Run;
using Core.Run.Branching;
using Core.Run.Sandbox;
using Xunit;

namespace Core.Tests.Run;

public sealed class ProfileProgressTests
{
    internal static ProfileProgressPolicyDefinition Policy(int count = 1) => new()
    {
        ProfileProgressPolicyId = "options", ContributingModeIds = ["published"], Unlocks =
        [new() { UnlockId = "new-card", Targets = [new() { Kind = UnlockTargetKind.Card, DefinitionId = "card-b" }],
            Condition = new() { Kind = UnlockConditionKind.AttemptStarted, Count = count } }]
    };

    internal static RunState State(int id = 1, ProfileProgressPolicyDefinition? policy = null) => new()
    {
        RunId = Guid.Parse($"00000000-0000-0000-0000-{id:D12}"), Sequence = 1, PlayerEntityId = "player", SettingId = "setting",
        ModeId = "published", Determinism = DeterministicContext.Create(42, "revision").AdvanceStep(),
        Lineage = RunLineage.Root(Guid.Parse($"00000000-0000-0000-0000-{id:D12}")),
        ResolvedMode = new() { Definition = new() { ModeId = "published", RunDefinitionId = "act-one" },
            ProfileProgressPolicy = policy ?? Policy() }
    };

    internal static RunCommandIdentity Command(int id = 1) => new(
        Guid.Parse($"10000000-0000-0000-0000-{id:D12}"), "TEST", 0, 0, "hash");

    internal static ProfileProgressCommit Candidate(ProfileProgressSnapshot basis, RunState after, RunState? before = null, int command = 1) => new()
    {
        PlayerId = after.PlayerEntityId, SettingId = after.SettingId, ProfileSequence = basis.Sequence + 1,
        BasisRevision = basis.Revision, RunId = after.RunId, RunSequence = after.Sequence, CommandId = Command(command).CommandId,
        ContentRevision = after.Determinism.ContentRevision, Policy = after.ResolvedMode!.ProfileProgressPolicy!,
        Contributions = ProfileProgressFacts.Extract(before, after, Command(command))
    };

    [Fact]
    public void IncrementalReductionAndRebuildAgree_GrantOnceAtThreshold_WithDeterministicProofs()
    {
        var hashes = new HashSet<string>();
        for (var repetition = 0; repetition < 10; repetition++)
        {
            var empty = ProfileProgressSnapshot.Empty("player", "setting");
            var first = ProfileProgressReducer.Plan(empty, Candidate(empty, State(policy: Policy(2)))).Value!;
            Assert.Empty(first.Grants);
            var one = ProfileProgressReducer.Apply(empty, first).Value;
            var second = ProfileProgressReducer.Plan(one, Candidate(one, State(2, Policy(2)), command: 2)).Value!;
            Assert.Equal(2, Assert.Single(second.Grants).EvidenceCounts["condition"]);
            Assert.Equal(one.Revision, second.BasisRevision);
            var two = ProfileProgressReducer.Apply(one, second).Value;
            var third = ProfileProgressReducer.Plan(two, Candidate(two, State(3, Policy(2)), command: 3)).Value!;
            Assert.Empty(third.Grants);
            var final = ProfileProgressReducer.Apply(two, third).Value;
            var rebuilt = ProfileProgressReducer.Rebuild("player", "setting", [third, first, second]);
            Assert.True(rebuilt.IsSuccess, rebuilt.IsFailure ? rebuilt.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(final), CanonicalJson.ComputeHash(rebuilt.Value));
            hashes.Add(CanonicalJson.ComputeHash(new[] { first, second, third }));
        }
        Assert.Single(hashes);
    }

    [Fact]
    public void Conditions_AllAnyAndDependenciesAreReducedToAFixedPoint()
    {
        var policy = Policy() with { Unlocks = Policy().Unlocks.Add(new()
        {
            UnlockId = "a-dependent", Targets = [new() { Kind = UnlockTargetKind.CardUpgrade, DefinitionId = "upgrade" }],
            Condition = new() { Kind = UnlockConditionKind.All, Conditions =
            [new() { Kind = UnlockConditionKind.UnlockGranted, UnlockId = "new-card" },
                new() { Kind = UnlockConditionKind.Any, Conditions =
                [new() { Kind = UnlockConditionKind.RunCompleted }, new() { Kind = UnlockConditionKind.AttemptStarted }] }] }
        }) };
        var empty = ProfileProgressSnapshot.Empty("player", "setting");
        var proof = ProfileProgressReducer.Plan(empty, Candidate(empty, State(policy: policy))).Value!;
        Assert.Equal(2, proof.Grants.Length);
        Assert.Equal(1, proof.Grants.Single(grant => grant.UnlockId == "a-dependent").EvidenceCounts["condition.0"]);
    }

    [Fact]
    public void CanonicalCompletions_UseTypedNodeEncounterAndLifecycle_NotTextOrTelemetry()
    {
        var before = State();
        var after = before with { Sequence = 2, Lifecycle = RunLifecycleState.Completed,
            Map = new() { ResolvedNodeIds = ["boss"] }, Encounters = [new() { NodeId = "boss", Resolved = true,
                Combat = new() { CombatId = Guid.NewGuid(), Status = CombatStatus.VICTORY } }] };
        var facts = ProfileProgressFacts.Extract(before, after, Command());
        Assert.Equal(new[] { UnlockConditionKind.RunOutcome, UnlockConditionKind.RunCompleted, UnlockConditionKind.NodeCompleted,
            UnlockConditionKind.EncounterCompleted }, facts.Select(fact => fact.Kind));
        Assert.Equal(CombatStatus.VICTORY, facts[^1].EncounterOutcome);
        Assert.Empty(ProfileProgressFacts.Extract(after, after, Command()));
    }

    [Theory]
    [InlineData("no-policy")]
    [InlineData("wrong-mode")]
    [InlineData("sandbox")]
    [InlineData("cheats")]
    [InlineData("dev")]
    [InlineData("branch")]
    [InlineData("simulation")]
    public void ExcludesIneligibleSourcesByDefault(string source)
    {
        var state = State();
        state = source switch
        {
            "no-policy" => state with { ResolvedMode = state.ResolvedMode! with { ProfileProgressPolicy = null } },
            "wrong-mode" => state with { ModeId = "other" },
            "sandbox" => state with { Scenario = new CombatScenarioDefinition() },
            "cheats" => state with { ResolvedMode = state.ResolvedMode! with {
                Definition = state.ResolvedMode.Definition with { ProfileProgressProvenance = ProfileProgressProvenance.Published },
                CapabilityPolicy = new() { AllowRunResourceCheats = true } } },
            "dev" => state with { ResolvedMode = state.ResolvedMode! with {
                Definition = state.ResolvedMode.Definition with { ProfileProgressProvenance = ProfileProgressProvenance.DevModder } } },
            _ => state with { Lineage = RunLineage.Branch(Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), null,
                source == "simulation" ? "simulation:test" : "branch") }
        };
        Assert.Empty(ProfileProgressFacts.Extract(null, state, Command()));
        Assert.Empty(ProfileProgressFacts.Extract(state, state with { Sequence = 2, Lifecycle = RunLifecycleState.Completed }, Command()));
    }

    [Theory]
    [InlineData(ProfileProgressDeduplication.RootLineage, 1)]
    [InlineData(ProfileProgressDeduplication.RunInstance, 2)]
    public void ExplicitBranches_RespectDedupeAndDoNotCountInheritedCompletions(ProfileProgressDeduplication dedupe, int expected)
    {
        var policy = Policy() with { Deduplication = dedupe, AllowedProvenances = [ProfileProgressProvenance.Published, ProfileProgressProvenance.Branch] };
        var root = State(policy: policy);
        var complete = root with { Sequence = 2, Lifecycle = RunLifecycleState.Completed, Map = new() { ResolvedNodeIds = ["boss"] } };
        var basis = ProfileProgressSnapshot.Empty("player", "setting");
        var proof = ProfileProgressReducer.Plan(basis, Candidate(basis, complete, root)).Value!;
        basis = ProfileProgressReducer.Apply(basis, proof).Value;
        var branch = State(2, policy) with { Lineage = RunLineage.Branch(root.RunId, root.RunId, 1, new string('a', 64), null, "branch") };
        Assert.Empty(ProfileProgressFacts.Extract(null, branch with { Map = complete.Map }, Command()));
        var branchDone = branch with { Sequence = 2, Lifecycle = RunLifecycleState.Completed, Map = complete.Map };
        var branchProof = ProfileProgressReducer.Plan(basis, Candidate(basis, branchDone, branch, 2));
        Assert.True(branchProof.IsSuccess, branchProof.IsFailure ? branchProof.Error : null);
        if (branchProof.Value != null) basis = ProfileProgressReducer.Apply(basis, branchProof.Value).Value;
        Assert.Equal(expected, basis.Contributions.Count(item => item.Kind == UnlockConditionKind.RunCompleted));
    }

    [Fact]
    public void RuleChanges_NeverRevokeOrRewritePreviouslyGrantedProofs()
    {
        var basis = ProfileProgressSnapshot.Empty("player", "setting");
        var initial = ProfileProgressReducer.Plan(basis, Candidate(basis, State())).Value!;
        basis = ProfileProgressReducer.Apply(basis, initial).Value;
        var changed = Policy(100) with { ProfileProgressPolicyId = "new-revision", Unlocks = [Policy(100).Unlocks[0] with {
            Targets = [new() { Kind = UnlockTargetKind.Relic, DefinitionId = "different-option" }] }] };
        var second = ProfileProgressReducer.Plan(basis, Candidate(basis, State(2, changed), command: 2)).Value!;
        var updated = ProfileProgressReducer.Apply(basis, second).Value;
        Assert.Empty(second.Grants);
        Assert.Equal(CanonicalJson.ComputeHash(basis.Grants["new-card"]), CanonicalJson.ComputeHash(updated.Grants["new-card"]));
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("unknown-dependency")]
    [InlineData("threshold")]
    [InlineData("implicit-kind")]
    [InlineData("power-target")]
    [InlineData("unknown-provenance")]
    [InlineData("encounter-without-node")]
    [InlineData("ignored-outcome")]
    public void PolicyRejectsInvalidOrAmbiguousRules(string fault)
    {
        var policy = Policy();
        var unlock = policy.Unlocks[0];
        policy = fault switch
        {
            "cycle" => policy with { Unlocks = [unlock with { Condition = new() { Kind = UnlockConditionKind.UnlockGranted, UnlockId = unlock.UnlockId } }] },
            "unknown-dependency" => policy with { Unlocks = [unlock with { Condition = new() { Kind = UnlockConditionKind.UnlockGranted, UnlockId = "missing" } }] },
            "threshold" => policy with { Unlocks = [unlock with { Condition = unlock.Condition with { Count = 0 } }] },
            "implicit-kind" => policy with { Unlocks = [unlock with { Condition = new() }] },
            "power-target" => policy with { Unlocks = [unlock with { Targets = [new() { Kind = (UnlockTargetKind)99, DefinitionId = "strength" }] }] },
            "unknown-provenance" => policy with { AllowedProvenances = [(ProfileProgressProvenance)99] },
            "encounter-without-node" => policy with { Unlocks = [unlock with { Condition = new() { Kind = UnlockConditionKind.EncounterCompleted } }] },
            _ => policy with { Unlocks = [unlock with { Condition = unlock.Condition with { Outcomes = [RunLifecycleState.Completed] } }] }
        };
        Assert.True(ProfileProgressPolicyValidator.Validate(policy).IsFailure);
    }

    [Fact]
    public void ForgedProofs_StaleBasisAndMissingPredecessorsFailClosed()
    {
        var basis = ProfileProgressSnapshot.Empty("player", "setting");
        var first = ProfileProgressReducer.Plan(basis, Candidate(basis, State())).Value!;
        Assert.True(ProfileProgressReducer.Apply(basis, first with { Revision = "forged" }).IsFailure);
        Assert.True(ProfileProgressReducer.Apply(basis, first with { Grants = [] }).IsFailure);
        Assert.True(ProfileProgressReducer.Apply(basis, first with { Grants = [first.Grants[0] with { EvidenceCounts = first.Grants[0].EvidenceCounts.SetItem("condition", 99) }] }).IsFailure);
        var one = ProfileProgressReducer.Apply(basis, first).Value;
        var second = ProfileProgressReducer.Plan(one, Candidate(one, State(2), command: 2)).Value!;
        Assert.True(ProfileProgressReducer.Apply(basis, second).IsFailure);
        Assert.True(ProfileProgressReducer.Rebuild("player", "setting", [second]).IsFailure);
        Assert.Null(ProfileProgressReducer.Plan(one, Candidate(one, State())).Value);
        Assert.True(ProfileProgressReducer.Plan(basis, Candidate(basis, State()) with { SettingId = "other" }).IsFailure);
    }
}
