using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.CardZones;
using Core.Determinism;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Meta;
using Core.Run;
using Core.Run.Content;
using Core.Run.Branching;
using Core.Tests.Persistence;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunEligibilityTests
{
    private static readonly UnlockTargetDefinition Card = new() { Kind = UnlockTargetKind.Card, DefinitionId = "card-b" };
    private static readonly UnlockTargetDefinition Upgrade = new() { Kind = UnlockTargetKind.CardUpgrade, DefinitionId = "upgrade-b" };
    private static readonly UnlockTargetDefinition Relic = new() { Kind = UnlockTargetKind.Relic, DefinitionId = "relic-b" };
    private static ResolvedGameMode Mode(RunEligibilityRules? rules = null) => new()
    {
        Definition = new() { ModeId = "published", RunDefinitionId = "act-one", EligibilityRules = rules },
        ProfileProgressPolicy = ProfileProgressTests.Policy(2) with
        { Unlocks = [new() { UnlockId = "new-card", Targets = [Card, Upgrade, Relic], Condition = new() { Kind = UnlockConditionKind.AttemptStarted, Count = 2 } }] }
    };
    private static ProfileProgressSnapshot Progress(bool unlocked = false) => ProfileProgressSnapshot.Empty("player", "setting") with
    { Grants = unlocked ? ImmutableSortedDictionary<string, UnlockGrantProof>.Empty.Add("new-card", new() { UnlockId = "new-card", Targets = [Card, Upgrade, Relic] }) : ImmutableSortedDictionary<string, UnlockGrantProof>.Empty };
    private static RunState State(bool unlocked = false, RunEligibilityRules? rules = null)
    {
        var mode = Mode(rules);
        return ProfileProgressTests.State(policy: mode.ProfileProgressPolicy) with
        { ResolvedMode = mode, Eligibility = RunEligibility.Capture(mode, "player", "setting", "revision", Progress(unlocked)).Value };
    }

    [Fact]
    public void CapturesOnlyTrustedScope_EffectiveIdentityIgnoresIrrelevantHistory()
    {
        var empty = RunEligibility.Capture(Mode(), "player", "setting", "revision", Progress()).Value!;
        var unrelatedHistory = RunEligibility.Capture(Mode(), "player", "setting", "revision", Progress() with { Sequence = 100, Revision = "different-history" }).Value!;
        Assert.Equal(empty.EffectiveFingerprint, unrelatedHistory.EffectiveFingerprint);
        Assert.NotEqual(empty.ProfileRevision, unrelatedHistory.ProfileRevision);
        var unlocked = RunEligibility.Capture(Mode(), "player", "setting", "revision", Progress(true)).Value!;
        Assert.NotEqual(empty.EffectiveFingerprint, unlocked.EffectiveFingerprint);
        Assert.False(empty.Allows(Card.Kind, Card.DefinitionId));
        Assert.True(unlocked.Allows(Card.Kind, Card.DefinitionId));
        Assert.True(empty.Allows(UnlockTargetKind.Card, "baseline"));
        Assert.True(RunEligibility.Capture(Mode(), "other", "setting", "revision", Progress(true)).IsFailure);
        Assert.True(RunEligibility.Capture(Mode(), "player", "other", "revision", Progress(true)).IsFailure);
        Assert.True(RunEligibility.Capture(Mode(), "player", "setting", "revision", null).IsFailure);
    }

    [Fact]
    public void HistoricalProofCannotAuthorizeNewTargetsByReusingAnUnlockId()
    {
        var progress = Progress(true) with { Grants = Progress(true).Grants.SetItem("new-card", new() { UnlockId = "new-card", Targets = [Card] }) };
        var captured = RunEligibility.Capture(Mode(), "player", "setting", "revision", progress).Value!;
        Assert.True(captured.Allows(Card.Kind, Card.DefinitionId));
        Assert.False(captured.Allows(Upgrade.Kind, Upgrade.DefinitionId));
    }

    [Fact]
    public void FixedChallengeDoesNotReadProfile_UnrestrictedRequiresExplicitCheatMode()
    {
        var fixedMode = Mode(new() { Source = RunEligibilitySource.Fixed, FixedUnlockIds = ["new-card"] });
        var snapshot = RunEligibility.Capture(fixedMode, "player", "setting", "revision", null).Value!;
        Assert.Equal(3, snapshot.AvailableTargets.Length);
        Assert.Equal(0, snapshot.ProfileSequence);
        Assert.Equal(string.Empty, snapshot.ProfileRevision);
        Assert.True(RunEligibility.ValidateRules(Mode(new() { Source = RunEligibilitySource.Fixed, FixedUnlockIds = ["unknown"] })).IsFailure);
        Assert.True(RunEligibility.ValidateRules(Mode(new() { Source = RunEligibilitySource.Profile, FixedUnlockIds = ["new-card"] })).IsFailure);
        var unrestricted = Mode(new() { Source = RunEligibilitySource.Unrestricted });
        Assert.True(RunEligibility.ValidateRules(unrestricted).IsFailure);
        unrestricted = unrestricted with { CapabilityPolicy = new() { AllowCardZoneCheats = true } };
        Assert.Equal(3, RunEligibility.Capture(unrestricted, "player", "setting", "revision", null).Value!.AvailableTargets.Length);
    }

    [Fact]
    public void OffersAndRerollsFilterBeforeRng_CanonicalInputsRepeatExactly()
    {
        var pool = new CardPoolResult { PoolId = "options", Cards = [new() { CardId = "card-b" }, new() { CardId = "baseline" }] };
        var locked = State().Eligibility!;
        var context = DeterministicContext.Create(77, "revision");
        var first = CardOfferResolver.Resolve(pool, 2, context, eligibility: locked).Value;
        var reversed = CardOfferResolver.Resolve(pool with { Cards = pool.Cards.Reverse().ToArray() }, 2, context, eligibility: locked).Value;
        Assert.Equal("baseline", Assert.Single(first.Cards).CardId);
        Assert.Equal(first.Fingerprint, reversed.Fingerprint);
        Assert.Equal(first.Context, reversed.Context);
        Assert.DoesNotContain(CardOfferResolver.Resolve(pool, 2, first.Context, eligibility: locked).Value.Cards, card => card.CardId == "card-b");
        Assert.Equal(2, CardOfferResolver.Resolve(pool, 2, context, eligibility: State(true).Eligibility).Value.Cards.Count);
    }

    [Fact]
    public void InlineRewardsShopsAndLockedRerollOptionsUseTheSameSnapshot()
    {
        var generator = new RunOfferGenerator(null, null, false);
        var state = State();
        var selection = new CardSelectionDefinition { CardPool = ["card-b", "baseline"], OfferCount = 2 };
        Assert.Equal("baseline", Assert.Single(generator.GenerateCardSelection(state, selection, new HashSet<string>()).Value.Options).CardId);
        Assert.True(generator.GenerateCardSelection(state, selection, new HashSet<string> { "card-b" }).IsFailure);
        var shop = new ShopDefinition { Items = [new() { ItemId = "blocked", CardId = "card-b" }, new() { ItemId = "open", CardId = "baseline" }] };
        Assert.Equal("open", Assert.Single(generator.GenerateShop(state, shop).Value.Items).ItemId);
        Assert.Equal(2, generator.GenerateShop(State(true), shop).Value.Items.Count);
    }

    [Fact]
    public void EmptyPoolsFailClosedOrCompleteExplicitlyWithoutInventingFallbackCards()
    {
        var pool = new CardPoolResult { PoolId = "locked", Cards = [new() { CardId = "card-b" }] };
        var blocked = State();
        Assert.True(CardOfferResolver.Resolve(pool, 1, blocked.Determinism, eligibility: blocked.Eligibility).IsFailure);
        var allowed = State(rules: new() { EmptyPools = EmptyEligiblePoolPolicy.AllowEmpty });
        var result = CardOfferResolver.Resolve(pool, 1, allowed.Determinism, eligibility: allowed.Eligibility).Value;
        Assert.Empty(result.Cards);
        Assert.Equal(allowed.Determinism, result.Context);
        Assert.True(CardSelectionTransitions.Create(allowed, new(), [], "empty").Value.Completed);
        Assert.True(new RunOfferGenerator(null, null, false).GenerateShop(blocked,
            new() { Items = [new() { CardId = "card-b" }] }).IsFailure);
    }

    [Theory]
    [InlineData(UnlockTargetKind.Card)]
    [InlineData(UnlockTargetKind.CardUpgrade)]
    [InlineData(UnlockTargetKind.Relic)]
    public void EveryAcquisitionIsFencedBeforeDurability(UnlockTargetKind kind)
    {
        var before = State();
        var cardId = Guid.Parse("00000000-0000-0000-0000-000000000077");
        var card = new CardInstanceState { CardInstanceId = cardId, DefinitionId = "baseline" };
        if (kind == UnlockTargetKind.CardUpgrade) before = before with
        { Deck = new() { Topology = new() { Instances = new Dictionary<Guid, CardInstanceState> { [cardId] = card } } } };
        var after = before with { Sequence = 2, Determinism = before.Determinism.AdvanceStep() };
        after = kind switch
        {
            UnlockTargetKind.Relic => after with { Relics = [new() { RelicInstanceId = cardId, DefinitionId = "relic-b" }] },
            _ => after with { Deck = new() { Topology = new() { Instances = new Dictionary<Guid, CardInstanceState> { [cardId] =
                kind == UnlockTargetKind.Card ? card with { DefinitionId = "card-b" } : card with { Upgrades = [new() { UpgradeId = "upgrade-b", TransformationId = 1 }] } } } } }
        };
        Assert.True(RunEligibility.ValidateCandidate(before, after).IsFailure);
        var commit = ProfileProgressRunCommitStoreTests.Commit(after, before, 2);
        Assert.Throws<InvalidOperationException>(() => PreparedRunCommit.CreateVerified(commit, CanonicalJson.ComputeHash(after), before));
        Assert.Equal(1, before.Sequence);
        var available = State(true).Eligibility;
        Assert.True(RunEligibility.ValidateCandidate(before with { Eligibility = available }, after with { Eligibility = available }).IsSuccess);
    }

    [Fact]
    public void PreparationsAndRelicsCannotBypassSnapshot_AndNewProgressCannotRecaptureIt()
    {
        var run = State();
        var preparation = PreparationTransitions.Create(run, new() { Options = [new() { OptionId = "locked", GrantedCardIds = ["card-b"] }, new() { OptionId = "open" }] }).Value;
        Assert.Equal("open", Assert.Single(preparation.Options).OptionId);
        Assert.True(RelicTransitions.Acquire(run, new() { RelicId = "relic-b" }).IsFailure);
        Assert.True(CardTransformationAccess.Validate(run with { ResolvedMode = Mode() with { ProgressionPolicy = new() { AllowOutOfActivityCommands = true } } },
            CardTransformationOperation.Apply, "upgrade-b").IsFailure);
        Assert.True(RunEligibility.ValidateCandidate(run, run with { Eligibility = State(true).Eligibility }).IsFailure);
        Assert.True(RunEligibility.ValidateSnapshot(run with { Eligibility = run.Eligibility! with { EffectiveFingerprint = "forged" } }).IsFailure);
        Assert.True(RunEligibility.ValidateSnapshot(run with { Eligibility = null }).IsFailure);
    }

    [Theory]
    [InlineData(CardZoneFlowInvocation.Effect)]
    [InlineData(CardZoneFlowInvocation.GameplayCommand)]
    [InlineData(CardZoneFlowInvocation.Tool)]
    [InlineData(CardZoneFlowInvocation.Boundary)]
    public void AuthoredZoneCreationChecksEligibilityEvenIfTheFlowWouldDestroyItsNewCard(CardZoneFlowInvocation invocation)
    {
        var run = State();
        var definition = new CardZoneSystemDefinition { CardZoneSystemId = "test", Zones = [new()
            { ZoneId = "any", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }],
            Flows = [new() { FlowId = "create", AllowedInvocations = [invocation], Steps = [new()
                { StepId = "create", Operation = CardZoneOperation.Create, TargetZoneId = "any", CardDefinitionId = "card-b", Selection = new() { Count = 1 } },
                new() { StepId = "destroy", Operation = CardZoneOperation.Destroy, SourceZoneId = "any", Selection = new() { Strategy = CardZoneSelectionStrategy.All } }] }] };
        var compiled = CardZoneSystemCompiler.Compile(definition);
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        var topology = CardZoneTransitions.CreateEmpty([new() { ZoneId = "any", OwnerId = "$run" }]).Value;
        var result = new CardZoneFlowExecutor().Execute(compiled.Value, topology, run.Determinism, "create",
            new() { Invocation = invocation, FlowOwnerId = "$run", RunOwnerId = "$run", Eligibility = run.Eligibility });
        Assert.True(result.IsFailure);
        Assert.Contains("eligibility", result.Error);
        Assert.Empty(topology.Instances);
    }

    [Fact]
    public void BranchesAndSimulationsInheritEligibilityButNotTheRootCreationReceipt()
    {
        var source = State() with { CreationReceipt = new("key", "request") };
        foreach (var key in new[] { "experiment", "simulation:trial" })
        {
            var command = new RunBranchStartCommand(source.RunId, source.Sequence, key, CanonicalJson.ComputeHash(source));
            var branch = RunBranchTransitions.Create(source, command).Value;
            Assert.Equal(source.Eligibility, branch.Eligibility);
            Assert.Null(branch.CreationReceipt);
            Assert.True(RunEligibility.ValidateSnapshot(branch).IsSuccess);
            Assert.NotEqual(source.RunId, branch.RunId);
        }
    }

    [Fact]
    public void UnseededRequestsWithoutRetryKeysAreNewAttempts_SeededOrKeyedRequestsAreStable()
    {
        var unseeded = new RunStartOptions(ModeId: "published");
        Assert.NotEqual(RunCreationService.Identify(unseeded).Key, RunCreationService.Identify(unseeded).Key);
        var seeded = unseeded with { Seed = 42 };
        Assert.Equal(RunCreationService.Identify(seeded), RunCreationService.Identify(seeded));
        var keyed = unseeded with { CreationCommandId = Guid.Parse("00000000-0000-0000-0000-000000000077") };
        Assert.Equal(RunCreationService.Identify(keyed), RunCreationService.Identify(keyed));
    }

    [Fact]
    public async Task CreationReceiptIsStableAcrossHostsRestartAndLaterProfileProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), "HeroScript", "creation-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var a = new FileRunCommitStore(path, NullLogger.Instance);
            using var b = new FileRunCommitStore(path, NullLogger.Instance);
            var options = new RunStartOptions("setting", "act-one", "player", 42, "revision", "published", SettingId: "setting",
                CreationCommandId: Guid.Parse("00000000-0000-0000-0000-000000000080"));
            var identity = RunCreationService.Identify(options);
            async Task<RunState> Start(FileRunCommitStore store)
            {
                using var lease = await store.AcquireCreationLeaseAsync(identity.Key);
                var service = new RunCreationService(store);
                var found = service.Find(identity).Value;
                if (found != null) return found;
                var state = State() with { CreationReceipt = identity, Eligibility = service.Capture(options, Mode(), "revision").Value };
                var commit = ProfileProgressRunCommitStoreTests.Commit(state);
                await store.AppendAsync(commit with { RootCommand = commit.RootCommand with { Type = RunCommandTypes.StartRun } });
                return state;
            }
            var states = await Task.WhenAll(Start(a), Start(b));
            Assert.Equal(CanonicalJson.ComputeHash(states[0]), CanonicalJson.ComputeHash(states[1]));
            Assert.Equal(1, (await a.ReadAsync("player", "setting")).Sequence);
            var second = ProfileProgressTests.State(2, Mode().ProfileProgressPolicy) with { Eligibility = states[0].Eligibility };
            await a.AppendAsync(ProfileProgressRunCommitStoreTests.Commit(second, command: 2));
            Assert.Single((await a.ReadAsync("player", "setting")).Grants);
            using var restarted = new FileRunCommitStore(path, NullLogger.Instance);
            var creation = new RunCreationService(restarted);
            Assert.Equal(states[0].Eligibility!.EffectiveFingerprint, creation.Find(identity).Value!.Eligibility!.EffectiveFingerprint);
            Assert.Empty(creation.Find(identity).Value!.Eligibility!.AvailableTargets);
            Assert.Equal(3, creation.Capture(options, Mode(), "revision").Value!.AvailableTargets.Length);
            Assert.True(creation.Find(RunCreationService.Identify(options with { Seed = 43 })).IsFailure);
            Assert.NotEqual(identity.Key, RunCreationService.Identify(options with { PlayerEntityId = "other" }).Key);
            Assert.NotEqual(identity.Key, RunCreationService.Identify(options with { SettingId = "other" }).Key);
            Assert.True(creation.Capture(options with { CapturedEligibility = State(true).Eligibility }, Mode(), "revision").IsFailure);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
