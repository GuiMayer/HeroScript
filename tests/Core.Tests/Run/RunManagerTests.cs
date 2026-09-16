using Core.Abstractions.Persistence;
using Core.Config;
using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Combat.Flow;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.Run.Replay;
using Core.CardZones;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunManagerTests
{
    private readonly Mock<IConfigManager> _configManager = new();
    private readonly Mock<IResourceLoader> _resourceLoader = new();

    [Fact]
    public void PersistenceFailure_DoesNotPublishCandidateState()
    {
        var repository = new Mock<IRunCommitStore>();
        var appendCount = 0;
        repository
            .Setup(item => item.AppendAsync(
                It.IsAny<RunCommit>(),
                It.IsAny<CancellationToken>()))
            .Returns((RunCommit commit, CancellationToken _) =>
            {
                appendCount++;
                if (appendCount > 1)
                    throw new IOException("disk unavailable");
                return Task.FromResult(new RunCommitAppendResult(commit, false));
            });
        var manager = CreateManager(repository: repository.Object);
        var started = manager.StartRun(new RunStartOptions(
            "test", "default_run", "hero", Seed: 10UL, ContentRevision: "test"));
        Assert.True(started.IsSuccess);

        var changed = manager.ApplyRunResource(
            started.Value.RunId, "gold", 5, ResourceEffectOperation.ADD);
        var active = manager.GetRun(started.Value.RunId);

        Assert.True(changed.IsFailure);
        Assert.Contains("disk unavailable", changed.Error);
        Assert.True(active.IsSuccess);
        Assert.Equal(started.Value, active.Value);
    }

    [Fact]
    public async Task CommitJournal_ReplaysAndDetectsTampering()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heroscript-replay-{Guid.NewGuid():N}");
        try
        {
            using var repository = new FileRunCommitStore(path, NullLogger.Instance);
            var manager = CreateManager(repository: repository);
            var started = manager.StartRun(new RunStartOptions(
                "test", "default_run", "hero", Seed: 20UL, ContentRevision: "test"));
            Assert.True(started.IsSuccess);
            var changed = manager.ApplyRunResource(
                started.Value.RunId, "gold", 5, ResourceEffectOperation.ADD);
            Assert.True(changed.IsSuccess);

            var commits = await repository.LoadCommitsAsync(started.Value.RunId);
            var replay = RunReplayVerifier.Verify(commits);

            Assert.True(replay.IsValid, string.Join("; ", replay.Errors));
            Assert.NotNull(replay.FinalState);
            Assert.Equal(
                CanonicalJson.ComputeHash(changed.Value),
                CanonicalJson.ComputeHash(replay.FinalState!));
            Assert.Equal(
                new[] { RunCommandTypes.StartRun, RunCommandTypes.ApplyRunResource },
                commits.Select(item => item.RootCommand.Type));

            var tampered = commits.ToArray();
            tampered[^1] = tampered[^1] with
            {
                StateAfter = SetResource(tampered[^1].StateAfter, "gold", 999)
            };
            Assert.False(RunReplayVerifier.Verify(tampered).IsValid);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void StartRun_SameInputsProduceIdenticalStateAndHash()
    {
        var options = new RunStartOptions(
            "test",
            "default_run",
            "hero",
            Seed: 0xC0FFEEUL,
            ContentRevision: "test-content-v1");

        var first = CreateManager().StartRun(options);
        var second = CreateManager().StartRun(options);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(first.Value.RunId, second.Value.RunId);
        Assert.Equal(first.Value.Deck.DrawPile, second.Value.Deck.DrawPile);
        Assert.Equal(first.Value.Deck.Hand, second.Value.Deck.Hand);
        Assert.Equal(first.Value.Determinism, second.Value.Determinism);
        Assert.Equal(CanonicalJson.ComputeHash(first.Value), CanonicalJson.ComputeHash(second.Value));
    }

    [Fact]
    public void StartRun_WithManifestProvider_PinsFullRevisionAndRejectsMismatch()
    {
        var manifest = new ContentManifest
        {
            ConfigName = "test",
            ConfigChain = new[] { "test" },
            Artifacts = new[]
            {
                new ContentArtifactManifest
                {
                    Kind = "runs",
                    Path = "runs/default_run.json",
                    Hash = new string('a', 64),
                    DefinitionCount = 1
                }
            },
            Revision = new string('b', 64)
        };
        var manifests = new Mock<IContentManifestProvider>();
        manifests.Setup(provider => provider.GetManifest("test"))
            .Returns(Result<ContentManifest>.Success(manifest));
        var manager = CreateManager(contentManifestProvider: manifests.Object);

        var started = manager.StartRun(new RunStartOptions(
            "test", "default_run", "hero", Seed: 123UL));
        var mismatch = manager.StartRun(new RunStartOptions(
            "test", "default_run", "hero", Seed: 124UL, ContentRevision: "stale"));

        Assert.True(started.IsSuccess, started.IsFailure ? started.Error : null);
        Assert.Equal(manifest.Revision, started.Value.Determinism.ContentRevision);
        Assert.Equal(manifest, started.Value.ContentManifest);
        Assert.True(mismatch.IsFailure);
        Assert.Contains("not active", mismatch.Error);
    }

    [Fact]
    public void GetRun_FromRepository_ValidatesEngineAndContentCompatibility()
    {
        var runId = Guid.NewGuid();
        var revision = new string('c', 64);
        var state = new RunState
        {
            RunId = runId,
            ConfigName = "test",
            Determinism = DeterministicContext.Create(1UL, revision)
        };
        var repository = new Mock<IRunCommitStore>();
        repository.Setup(store => store.LoadLatestStateAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        var compatibleManifests = new Mock<IContentManifestProvider>();
        compatibleManifests.Setup(provider => provider.GetManifest("test"))
            .Returns(Result<ContentManifest>.Success(new ContentManifest
            {
                ConfigName = "test",
                Revision = revision
            }));
        var incompatibleManifests = new Mock<IContentManifestProvider>();
        incompatibleManifests.Setup(provider => provider.GetManifest("test"))
            .Returns(Result<ContentManifest>.Success(new ContentManifest
            {
                ConfigName = "test",
                Revision = new string('d', 64)
            }));
        var compatible = CreateManager(
            repository: repository.Object,
            contentManifestProvider: compatibleManifests.Object);
        var incompatible = CreateManager(
            repository: repository.Object,
            contentManifestProvider: incompatibleManifests.Object);

        var restored = compatible.GetRun(runId);
        var refused = incompatible.GetRun(runId);

        Assert.True(restored.IsSuccess, restored.IsFailure ? restored.Error : null);
        Assert.Equal(state, restored.Value);
        Assert.True(refused.IsFailure);
        Assert.Contains("Content revision unavailable", refused.Error);
    }

    [Fact]
    public void StartRun_LoadsDefinitionFromJsonAndDrawsStartingHand()
    {
        var manager = CreateManager();

        var result = manager.StartRun("test", "default_run", "hero");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("test", result.Value.ConfigName);
        Assert.Equal("hero", result.Value.PlayerEntityId);
        Assert.Equal(25, result.Value.ResourceState.Current("gold"));
        Assert.Equal(new[] { "strike", "defend" }, result.Value.Deck.Hand);
        Assert.Equal(new[] { "zap" }, result.Value.Deck.DrawPile);
        Assert.Equal("start", result.Value.CurrentNodeId);
        Assert.Equal(2, result.Value.Map.Nodes.Count);
        Assert.Equal(new[] { "start" }, result.Value.Map.VisitedNodeIds);
    }

    [Fact]
    public void MapCommands_PersistResolvedAndVisitedStateInOrder()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var resolve = manager.ResolveCurrentNode(run.RunId, "start");
        var available = manager.GetAvailableCommands(run.RunId);
        var advance = manager.AdvanceNode(run.RunId, "reward");
        var current = manager.GetRun(run.RunId).Value;

        Assert.True(resolve.IsSuccess, resolve.IsFailure ? resolve.Error : null);
        Assert.True(available.IsSuccess, available.IsFailure ? available.Error : null);
        Assert.Equal(RunCommandTypes.AdvanceNode, Assert.Single(available.Value).Type);
        Assert.True(advance.IsSuccess, advance.IsFailure ? advance.Error : null);
        Assert.Equal("reward", current.CurrentNodeId);
        Assert.Equal(new[] { "reward", "start" }, current.Map.VisitedNodeIds);
        Assert.Equal(new[] { "start" }, current.Map.ResolvedNodeIds);
        Assert.Equal(run.Sequence + 2, current.Sequence);
        Assert.Equal(run.Determinism.Step + 2, current.Determinism.Step);
    }

    [Fact]
    public void RunOwnedCombat_AttachActionAndResolutionAreSingleRunTransitions()
    {
        var manager = CreateManager(runJson: CombatRunJson);
        var run = manager.StartRun(new RunStartOptions(
            "test", "default_run", "hero", Seed: 91UL, ContentRevision: "test")).Value;
        var combatSeed = run.Determinism.DrawUInt64().Value;
        var combat = CombatTransitions.Create(
            [CreateCombatActorState("hero", isHero: true), CreateCombatActorState("enemy", isHero: false)],
            DeterministicContext.Create(combatSeed, run.Determinism.ContentRevision)) with
        {
            RunId = run.RunId,
            RunNodeId = "start"
        };

        var attached = manager.AttachEncounter(
            run.RunId,
            run.Sequence,
            run.Determinism.Step,
            combat);
        var cardInstanceId = attached.Value.Deck.HandInstanceIds[0].ToString();
        var cardDefinitionId = attached.Value.Deck.GetDefinitionId(
            attached.Value.Deck.HandInstanceIds[0])!;
        var terminal = combat with
        {
            Status = CombatStatus.VICTORY,
            Determinism = combat.Determinism.AdvanceStep()
        };
        var committed = manager.CommitCombatAction(
            run.RunId,
            attached.Value.Sequence,
            combat,
            terminal,
            new CombatActionCommand
            {
                RunId = run.RunId,
                ActorId = "hero",
                ActionType = ActionType.POWER,
                PowerId = cardDefinitionId,
                CardInstanceId = Guid.Parse(cardInstanceId),
                TargetId = "enemy"
            },
            cardInstanceId,
            CardConsumeDestination.Discard);
        var resolved = manager.ResolveEncounter(
            run.RunId,
            committed.Value.Sequence,
            combat.CombatId);

        Assert.True(attached.IsSuccess, attached.IsFailure ? attached.Error : null);
        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error : null);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : null);
        Assert.Equal(run.Sequence + 3, resolved.Value.Sequence);
        Assert.Equal(run.Determinism.Step + 3, resolved.Value.Determinism.Step);
        Assert.Null(resolved.Value.ActiveEncounterId);
        var encounter = Assert.Single(resolved.Value.Encounters);
        Assert.True(encounter.Resolved);
        Assert.Equal("VICTORY", encounter.Outcome);
        Assert.Contains("start", resolved.Value.Map.ResolvedNodeIds);
        Assert.Contains(cardDefinitionId, resolved.Value.Deck.DiscardPile);
        Assert.Equal(
            RunCommandTypes.AdvanceNode,
            Assert.Single(manager.GetAvailableCommands(run.RunId).Value).Type);
    }

    [Fact]
    public async Task CommitCombatResolution_PersistsEveryTransitionAsOneCausalBatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heroscript-resolution-{Guid.NewGuid():N}");
        try
        {
            using var repository = new FileRunCommitStore(path, NullLogger.Instance);
            var manager = CreateManager(repository: repository, runJson: CombatRunJson);
            var run = manager.StartRun(new RunStartOptions(
                "test", "default_run", "hero", Seed: 93UL, ContentRevision: "test")).Value;
            var combatSeed = run.Determinism.DrawUInt64().Value;
            var combat = CombatTransitions.Create(
                [CreateCombatActorState("hero", isHero: true), CreateCombatActorState("enemy", isHero: false)],
                DeterministicContext.Create(combatSeed, run.Determinism.ContentRevision)) with
            {
                RunId = run.RunId,
                RunNodeId = "start"
            };
            var attached = manager.AttachEncounter(
                run.RunId,
                run.Sequence,
                run.Determinism.Step,
                combat).Value;
            var rootPayload = JsonSerializer.SerializeToElement(new { actorId = "hero" });
            var rootCommand = new RunCommandIdentity(
                Guid.NewGuid(),
                "COMBAT_ACTION",
                attached.Sequence,
                combat.Determinism.Step,
                CanonicalJson.ComputeHash(rootPayload));
            var first = combat with
            {
                CurrentTurn = 2,
                Determinism = combat.Determinism.AdvanceStep()
            };
            var second = first with
            {
                CurrentTurn = 3,
                Determinism = first.Determinism.AdvanceStep()
            };
            var gameplay = Core.Combat.Modifiers.ModifierTransitions.Apply(attached,
                new() { ModifierId = "persistent-effect" },
                new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }, "status").Value.Run;

            var committed = manager.CommitCombatResolution(new CombatResolutionCommit
            {
                RunId = run.RunId,
                ExpectedSequence = attached.Sequence,
                PreviousCombat = combat,
                RootCommand = rootCommand,
                RootPayload = rootPayload,
                Steps =
                [
                    new CombatResolutionStep
                    {
                        TransitionType = "combat.activation.ended",
                        Combat = first,
                        Deck = attached.Deck,
                        RunSnapshot = gameplay,
                        RunDeterminism = gameplay.Determinism,
                        EffectSteps = [new EffectExecutionStep { EffectInstanceId = "status.tick" }],
                        CardZoneSteps = [new CardZoneFlowStepRecord
                        {
                            FlowId = "activation.ended", StepId = "move",
                            Operation = CardZoneOperation.Move,
                            SourceAddress = "ready|hero", TargetAddress = "spent|hero"
                        }],
                        Payload = JsonSerializer.SerializeToElement(new { actorId = "hero" })
                    },
                    new CombatResolutionStep
                    {
                        TransitionType = "combat.activation.started",
                        Combat = second,
                        Deck = attached.Deck,
                        Payload = JsonSerializer.SerializeToElement(new { actorId = "enemy" })
                    }
                ]
            });

            Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error : null);
            Assert.Equal(attached.Sequence + 1, committed.Value.Sequence);
            Assert.Equal(3, committed.Value.GetActiveEncounter()!.Combat.CurrentTurn);
            Assert.Equal("persistent-effect", Assert.Single(committed.Value.Modifiers).Definition.ModifierId);
            Assert.Empty(attached.Modifiers);
            var resolution = committed.Value.GetCombatResolution(rootCommand.CommandId);
            Assert.NotNull(resolution);
            Assert.Equal(AnimationFrameMode.FullSnapshots, resolution!.Mode);
            Assert.Equal(2, resolution.Frames.Count);
            Assert.Equal(attached.Sequence + 1, resolution.RootSequence);
            Assert.Equal(CanonicalJson.ComputeHash(combat), resolution.InitialCombatStateHash);
            Assert.Equal(CanonicalJson.ComputeHash(second), resolution.FinalCombatStateHash);
            Assert.Equal(64, resolution.ResolutionFingerprint.Length);
            Assert.All(resolution.Frames, frame => Assert.NotNull(frame.StateAfter));
            Assert.Equal(
                "status.tick",
                Assert.Single(resolution.Frames[0].EffectSteps).EffectInstanceId);
            Assert.Empty(resolution.Frames[1].EffectSteps);
            Assert.Equal("activation.ended", Assert.Single(resolution.Frames[0].CardZoneSteps).FlowId);
            Assert.Empty(resolution.Frames[1].CardZoneSteps);
            var commits = await repository.LoadCommitsAsync(run.RunId);
            var transition = commits[^1];
            Assert.Equal(rootCommand.CommandId, transition.RootCommand.CommandId);
            Assert.Equal("COMBAT_ACTION", transition.RootCommand.Type);
            Assert.Equal(2, transition.Frames.Count);
            Assert.Equal(
                new[] { "combat.activation.ended", "combat.activation.started" },
                transition.Frames.Select(frame => frame.Kind));
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void GetRunByCombat_RecoversEmbeddedEncounterAfterManagerRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heroscript-encounter-{Guid.NewGuid():N}");
        try
        {
            using var repository = new FileRunCommitStore(path, NullLogger.Instance);
            var manager = CreateManager(repository: repository, runJson: CombatRunJson);
            var run = manager.StartRun(new RunStartOptions(
                "test", "default_run", "hero", Seed: 92UL, ContentRevision: "test")).Value;
            var combatSeed = run.Determinism.DrawUInt64().Value;
            var combat = CombatTransitions.Create(
                [CreateCombatActorState("hero", isHero: true), CreateCombatActorState("enemy", isHero: false)],
                DeterministicContext.Create(combatSeed, run.Determinism.ContentRevision)) with
            {
                RunId = run.RunId,
                RunNodeId = "start"
            };
            var attached = manager.AttachEncounter(
                run.RunId,
                run.Sequence,
                run.Determinism.Step,
                combat);
            Assert.True(attached.IsSuccess, attached.IsFailure ? attached.Error : null);

            var restarted = CreateManager(repository: repository, runJson: CombatRunJson);
            var recovered = restarted.GetRunByCombat(combat.CombatId);

            Assert.True(recovered.IsSuccess, recovered.IsFailure ? recovered.Error : null);
            Assert.Equal(run.RunId, recovered.Value.RunId);
            Assert.Equal(combat.CombatId, recovered.Value.ActiveEncounterId);
            Assert.Equal(
                CanonicalJson.ComputeHash(combat),
                CanonicalJson.ComputeHash(recovered.Value.GetActiveEncounter()!.Combat));
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void DrawCards_ShufflesDiscardWhenDrawPileIsEmpty()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var discard = manager.DiscardCards(
            run.RunId,
            run.Deck.HandInstanceIds.Select(id => id.ToString()).ToArray());
        Assert.True(discard.IsSuccess, discard.IsFailure ? discard.Error : null);

        var drawn = manager.DrawCards(run.RunId, 3);
        run = manager.GetRun(run.RunId).Value;

        Assert.True(drawn.IsSuccess, drawn.IsFailure ? drawn.Error : null);
        Assert.Equal("zap", drawn.Value[0]);
        Assert.Equal(
            new[] { "defend", "strike" },
            drawn.Value.Skip(1).OrderBy(card => card, StringComparer.Ordinal));
        Assert.Equal(3, run.Deck.Hand.Count);
    }

    [Fact]
    public void ApplyRunResource_SupportsOperationsAndFields()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var gold = manager.ApplyRunResource(run.RunId, "gold", 10, ResourceEffectOperation.SUBTRACT);
        var pp = manager.ApplyRunResource(run.RunId, "power_points", 3, ResourceEffectOperation.ADD);
        var maximum = manager.ApplyRunResource(
            run.RunId,
            "gold",
            40,
            ResourceEffectOperation.SET,
            ResourceValueField.Maximum);
        run = maximum.Value;

        Assert.True(gold.IsSuccess, gold.IsFailure ? gold.Error : null);
        Assert.True(pp.IsSuccess, pp.IsFailure ? pp.Error : null);
        Assert.True(maximum.IsSuccess, maximum.IsFailure ? maximum.Error : null);
        Assert.Equal(15, run.ResourceState.Current("gold"));
        Assert.Equal(40, run.ResourceState.Get("gold")!.Maximum);
        Assert.Equal(3, run.ResourceState.Current("power_points"));
    }

    [Fact]
    public void PickCards_AddsRewardToDiscardPile()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var selection = manager.CreateCardSelection(run.RunId, "basic_reward");
        var pick = manager.PickCards(run.RunId, selection.Value.SelectionInstanceId, new[] { "zap" });
        run = manager.GetRun(run.RunId).Value;

        Assert.True(selection.IsSuccess, selection.IsFailure ? selection.Error : null);
        Assert.True(pick.IsSuccess, pick.IsFailure ? pick.Error : null);
        Assert.True(pick.Value.Completed);
        Assert.Contains("zap", run.Deck.DiscardPile);
    }

    [Fact]
    public void CreateCardSelection_WithPool_GeneratesEnrichedOptions()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var selection = manager.CreateCardSelection(run.RunId, "pool_reward");

        Assert.True(selection.IsSuccess, selection.IsFailure ? selection.Error : null);
        Assert.Equal(2, selection.Value.Options.Count);
        Assert.Equal("basic_rewards", selection.Value.CardPoolId);
        Assert.False(string.IsNullOrWhiteSpace(selection.Value.OfferFingerprint));
        Assert.Contains(selection.Value.Options, option => option.CardId == "heal" && option.Rarity == CardRarity.Common);
        Assert.Contains(selection.Value.Options, option => option.CardId == "fireball" && option.Rarity == CardRarity.Uncommon);
    }

    [Fact]
    public void RerollCardSelection_FreeReroll_ReplacesUnlockedOptionsWithoutSpendingGold()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "pool_reward").Value;

        var reroll = manager.RerollCardSelection(run.RunId, selection.SelectionInstanceId, new[] { "heal" });

        Assert.True(reroll.IsSuccess, reroll.IsFailure ? reroll.Error : null);
        Assert.Equal(25, run.ResourceState.Current("gold"));
        Assert.Equal(1, reroll.Value.RerollsUsed);
        Assert.Equal(0, reroll.Value.FreeRerollsRemaining);
        Assert.Contains(reroll.Value.Options, option => option.CardId == "heal");
        Assert.NotEqual(selection.OfferFingerprint, reroll.Value.OfferFingerprint);
    }

    [Fact]
    public void DecomposeCardSelectionOption_AddsPowerPointsAndBlocksPick()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "pool_reward").Value;

        var decompose = manager.DecomposeCardSelectionOption(run.RunId, selection.SelectionInstanceId, "fireball");
        var pick = manager.PickCards(run.RunId, selection.SelectionInstanceId, new[] { "fireball" });
        run = manager.GetRun(run.RunId).Value;

        Assert.True(decompose.IsSuccess, decompose.IsFailure ? decompose.Error : null);
        Assert.Equal(2, run.ResourceState.Current("power_points"));
        Assert.Contains("fireball", decompose.Value.DecomposedCardIds);
        Assert.True(pick.IsFailure);
        Assert.Contains("Invalid card options", pick.Error);
    }

    [Fact]
    public void PickCards_WhenInvalidPick_DoesNotCompleteSelectionOrAddCards()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "basic_reward").Value;
        var originalDiscard = run.Deck.DiscardPile.ToArray();

        var pick = manager.PickCards(run.RunId, selection.SelectionInstanceId, new[] { "missing_card" });

        Assert.True(pick.IsFailure);
        Assert.False(selection.Completed);
        Assert.Empty(selection.PickedCardIds);
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
    }

    [Fact]
    public void DecomposeCardSelectionOption_WhenAlreadyDecomposed_DoesNotAddPowerPointsAgain()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "pool_reward").Value;
        var first = manager.DecomposeCardSelectionOption(run.RunId, selection.SelectionInstanceId, "fireball");
        var powerPointsAfterFirst = manager.GetRun(run.RunId).Value.ResourceState.Current("power_points");

        var second = manager.DecomposeCardSelectionOption(run.RunId, selection.SelectionInstanceId, "fireball");

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsFailure);
        Assert.Equal(powerPointsAfterFirst, manager.GetRun(run.RunId).Value.ResourceState.Current("power_points"));
        Assert.Single(first.Value.DecomposedCardIds, id => id == "fireball");
    }

    [Fact]
    public void BuyShopItem_SpendsGoldAndAddsCardToDiscardPile()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var shop = manager.CreateShop(run.RunId, "basic_shop");
        var item = manager.BuyShopItem(run.RunId, shop.Value.ShopInstanceId, "buy_zap");
        run = manager.GetRun(run.RunId).Value;

        Assert.True(shop.IsSuccess, shop.IsFailure ? shop.Error : null);
        Assert.True(item.IsSuccess, item.IsFailure ? item.Error : null);
        Assert.True(item.Value.Purchased);
        Assert.Equal(15, run.ResourceState.Current("gold"));
        Assert.Contains("zap", run.Deck.DiscardPile);
    }

    [Fact]
    public void BuyShopItem_WithInsufficientResources_DoesNotMutateRunOrItem()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run = manager.ApplyRunResource(
            run.RunId,
            "gold",
            run.ResourceState.Current("gold"),
            ResourceEffectOperation.SUBTRACT).Value;
        var shop = manager.CreateShop(run.RunId, "basic_shop").Value;
        var originalDiscard = run.Deck.DiscardPile.ToArray();

        var item = manager.BuyShopItem(run.RunId, shop.ShopInstanceId, "buy_zap");

        Assert.True(item.IsFailure);
        Assert.Equal(0, run.ResourceState.Current("gold"));
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
        Assert.False(shop.Items.Single(i => i.ItemId == "buy_zap").Purchased);
    }

    [Fact]
    public void CreateShop_WithPool_GeneratesPricedItems()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var shop = manager.CreateShop(run.RunId, "dynamic_shop");

        Assert.True(shop.IsSuccess, shop.IsFailure ? shop.Error : null);
        Assert.Equal("basic_rewards", shop.Value.CardPoolId);
        Assert.Equal(2, shop.Value.Items.Count);
        Assert.False(string.IsNullOrWhiteSpace(shop.Value.OfferFingerprint));
        Assert.Contains(shop.Value.Items, item => item.CardId == "heal" && Amount(item.Costs, "gold") == 18);
        Assert.Contains(shop.Value.Items, item => item.CardId == "fireball" && Amount(item.Costs, "gold") == 35);
        Assert.All(shop.Value.Items, item => Assert.True(item.PricingBreakdowns["gold"].ContainsKey("final")));
    }

    [Fact]
    public void RerollShop_ChargesGoldAndRegeneratesItems()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var shop = manager.CreateShop(run.RunId, "dynamic_shop").Value;

        var reroll = manager.RerollShop(run.RunId, shop.ShopInstanceId);
        run = manager.GetRun(run.RunId).Value;

        Assert.True(reroll.IsSuccess, reroll.IsFailure ? reroll.Error : null);
        Assert.Equal(15, run.ResourceState.Current("gold"));
        Assert.Equal(1, reroll.Value.RerollsUsed);
        Assert.Equal(15, Amount(reroll.Value.RerollCosts, "gold"));
        Assert.NotEqual(shop.OfferFingerprint, reroll.Value.OfferFingerprint);
        Assert.All(reroll.Value.Items, item => Assert.False(item.Purchased));
    }

    [Fact]
    public void RerollShop_WithInsufficientGold_DoesNotMutateShop()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var shop = manager.CreateShop(run.RunId, "dynamic_shop").Value;
        run = manager.ApplyRunResource(
            run.RunId,
            "gold",
            run.ResourceState.Current("gold"),
            ResourceEffectOperation.SUBTRACT).Value;
        var originalItems = shop.Items.Select(i => i.ItemId).ToArray();
        var originalRerolls = shop.RerollsUsed;
        var originalCosts = shop.RerollCosts;

        var reroll = manager.RerollShop(run.RunId, shop.ShopInstanceId);

        Assert.True(reroll.IsFailure);
        Assert.Equal(0, run.ResourceState.Current("gold"));
        Assert.Equal(originalRerolls, shop.RerollsUsed);
        Assert.Equal(originalCosts, shop.RerollCosts);
        Assert.Equal(originalItems, shop.Items.Select(i => i.ItemId));
    }

    [Fact]
    public void RerollCardSelection_WithInsufficientGold_DoesNotMutateSelection()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "pool_reward").Value;
        var free = manager.RerollCardSelection(run.RunId, selection.SelectionInstanceId);
        Assert.True(free.IsSuccess, free.IsFailure ? free.Error : null);
        run = manager.ApplyRunResource(
            run.RunId,
            "gold",
            run.ResourceState.Current("gold"),
            ResourceEffectOperation.SUBTRACT).Value;
        var originalOptions = selection.Options.Select(o => o.CardId).ToArray();
        var originalRerolls = selection.RerollsUsed;
        var originalFree = selection.FreeRerollsRemaining;
        var originalCosts = selection.RerollCosts;

        var paid = manager.RerollCardSelection(run.RunId, selection.SelectionInstanceId);

        Assert.True(paid.IsFailure);
        Assert.Equal(0, run.ResourceState.Current("gold"));
        Assert.Equal(originalRerolls, selection.RerollsUsed);
        Assert.Equal(originalFree, selection.FreeRerollsRemaining);
        Assert.Equal(originalCosts, selection.RerollCosts);
        Assert.Equal(originalOptions, selection.Options.Select(o => o.CardId));
    }

    [Fact]
    public void ApplyPreparationOption_SpendsResourcesAndAddsConfiguredCards()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation");
        var option = manager.ApplyPreparationOption(run.RunId, preparation.Value.PreparationInstanceId, "pack_supplies");
        run = manager.GetRun(run.RunId).Value;

        Assert.True(preparation.IsSuccess, preparation.IsFailure ? preparation.Error : null);
        Assert.True(option.IsSuccess, option.IsFailure ? option.Error : null);
        Assert.True(option.Value.Applied);
        Assert.Equal(20, run.ResourceState.Current("gold"));
        Assert.Contains("heal", run.Deck.DiscardPile);
    }

    [Fact]
    public void ApplyPreparationOption_AppliesConfiguredScriptModifiers()
    {
        var modifierManager = new Mock<IPinnedContentCatalog<ScriptModifierDefinition>>();
        var manager = CreateManager(scriptModifierManager: modifierManager.Object);
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run = manager.ApplyRunResource(
            run.RunId, "power_points", 2, ResourceEffectOperation.ADD).Value;
        modifierManager
            .Setup(m => m.Get("flat_power_bonus", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Result<ScriptModifierDefinition>.Success(new ScriptModifierDefinition
            {
                ModifierId = "flat_power_bonus",
                DefaultDuration = -1
            }));

        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation").Value;
        var option = manager.ApplyPreparationOption(run.RunId, preparation.PreparationInstanceId, "train_spell");
        run = manager.GetRun(run.RunId).Value;

        Assert.True(option.IsSuccess, option.IsFailure ? option.Error : null);
        Assert.True(option.Value.Applied);
        Assert.Equal(1, run.ResourceState.Current("power_points"));
        Assert.Contains("fireball", run.Deck.DiscardPile);
        var modifier = Assert.Single(run.Modifiers);
        Assert.Equal("flat_power_bonus", modifier.ModifierId);
        Assert.Equal($"run:{run.RunId}", modifier.OwnerId);
        Assert.Equal(modifier.InstanceId, Assert.Single(option.Value.AppliedModifierInstanceIds));
        modifierManager.Verify(
            m => m.Get("flat_power_bonus", It.IsAny<string>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public void ApplyPreparationOption_WhenModifierManagerMissing_RollsBackResourcesAndCards()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run = manager.ApplyRunResource(
            run.RunId, "power_points", 2, ResourceEffectOperation.ADD).Value;
        var originalGold = run.ResourceState.Current("gold");
        var originalPowerPoints = run.ResourceState.Current("power_points");
        var originalDiscard = run.Deck.DiscardPile.ToArray();
        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation").Value;

        var option = manager.ApplyPreparationOption(run.RunId, preparation.PreparationInstanceId, "train_spell");

        Assert.True(option.IsFailure);
        Assert.Equal(originalGold, run.ResourceState.Current("gold"));
        Assert.Equal(originalPowerPoints, run.ResourceState.Current("power_points"));
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
        Assert.Empty(preparation.AppliedOptionIds);
        Assert.False(preparation.Options.Single(o => o.OptionId == "train_spell").Applied);
    }

    [Fact]
    public void ApplyPreparationOption_WhenModifierDefinitionFails_LeavesRunUnchanged()
    {
        var modifierManager = new Mock<IPinnedContentCatalog<ScriptModifierDefinition>>();
        var manager = CreateManager(scriptModifierManager: modifierManager.Object);
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run = manager.ApplyRunResource(
            run.RunId, "power_points", 2, ResourceEffectOperation.ADD).Value;
        var originalGold = run.ResourceState.Current("gold");
        var originalPowerPoints = run.ResourceState.Current("power_points");
        var originalDiscard = run.Deck.DiscardPile.ToArray();
        modifierManager
            .Setup(m => m.Get("flat_power_bonus", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Result<ScriptModifierDefinition>.Failure("modifier rejected"));
        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation").Value;

        var option = manager.ApplyPreparationOption(run.RunId, preparation.PreparationInstanceId, "train_spell");

        Assert.True(option.IsFailure);
        Assert.Equal("modifier rejected", option.Error);
        Assert.Equal(originalGold, run.ResourceState.Current("gold"));
        Assert.Equal(originalPowerPoints, run.ResourceState.Current("power_points"));
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
        Assert.Empty(preparation.AppliedOptionIds);
        Assert.False(preparation.Options.Single(o => o.OptionId == "train_spell").Applied);
        Assert.Empty(preparation.Options.Single(o => o.OptionId == "train_spell").AppliedModifierInstanceIds);
    }

    [Fact]
    public void ApplyPreparationOption_WhenSecondDefinitionFails_DoesNotPublishPartialModifiers()
    {
        var modifierManager = new Mock<IPinnedContentCatalog<ScriptModifierDefinition>>();
        var manager = CreateManager(scriptModifierManager: modifierManager.Object);
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run = manager.ApplyRunResource(
            run.RunId, "power_points", 3, ResourceEffectOperation.ADD).Value;
        var originalGold = run.ResourceState.Current("gold");
        var originalPowerPoints = run.ResourceState.Current("power_points");
        var originalDiscard = run.Deck.DiscardPile.ToArray();
        modifierManager
            .Setup(m => m.Get("flat_power_bonus", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Result<ScriptModifierDefinition>.Success(new ScriptModifierDefinition
            {
                ModifierId = "flat_power_bonus",
                DefaultDuration = -1
            }));
        modifierManager
            .Setup(m => m.Get("missing_modifier", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Result<ScriptModifierDefinition>.Failure("modifier missing"));
        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation").Value;

        var option = manager.ApplyPreparationOption(run.RunId, preparation.PreparationInstanceId, "double_train");

        Assert.True(option.IsFailure);
        Assert.Equal("modifier missing", option.Error);
        Assert.Equal(originalGold, run.ResourceState.Current("gold"));
        Assert.Equal(originalPowerPoints, run.ResourceState.Current("power_points"));
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
        Assert.Empty(preparation.AppliedOptionIds);
        Assert.False(preparation.Options.Single(o => o.OptionId == "double_train").Applied);
        Assert.Empty(manager.GetRun(run.RunId).Value.Modifiers);
    }

    [Fact]
    public void ConsumeCardsFromHand_Discard_RemovesFromHandAndAddsToDiscard()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var cardInstanceId = FindHandInstance(run.Deck, "strike");
        var result = manager.ConsumeCardsFromHand(run.RunId, [cardInstanceId.ToString()], CardConsumeDestination.Discard);
        run = manager.GetRun(run.RunId).Value;

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.DoesNotContain("strike", run.Deck.Hand);
        Assert.Contains("strike", run.Deck.DiscardPile);
    }

    [Fact]
    public void ConsumeCardsFromHand_Exhaust_RemovesFromHandAndAddsToExhaust()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var cardInstanceId = FindHandInstance(run.Deck, "defend");
        var result = manager.ConsumeCardsFromHand(run.RunId, [cardInstanceId.ToString()], CardConsumeDestination.Exhaust);
        run = manager.GetRun(run.RunId).Value;

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.DoesNotContain("defend", run.Deck.Hand);
        Assert.Contains("defend", run.Deck.ExhaustPile);
    }

    [Fact]
    public void ConsumeCardsFromHand_None_KeepsCardInHand()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var cardInstanceId = FindHandInstance(run.Deck, "strike");
        var result = manager.ConsumeCardsFromHand(run.RunId, [cardInstanceId.ToString()], CardConsumeDestination.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Contains("strike", run.Deck.Hand);
        Assert.DoesNotContain("strike", run.Deck.DiscardPile);
        Assert.DoesNotContain("strike", run.Deck.ExhaustPile);
    }

    [Fact]
    public void ConsumeCardsFromHand_MissingCard_Fails()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var missingInstanceId = Guid.Parse("ffffffff-ffff-8fff-bfff-ffffffffffff");
        var result = manager.ConsumeCardsFromHand(run.RunId, [missingInstanceId.ToString()], CardConsumeDestination.Discard);

        Assert.True(result.IsFailure);
        Assert.Contains("Card instance not found in hand", result.Error);
    }

    [Fact]
    public void ConsumeCardsFromHand_DuplicateCards_RemovesSingleOccurrence()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        manager.AddCardsToHand(run.RunId, new[] { "strike" });
        run = manager.GetRun(run.RunId).Value;
        var cardInstanceId = FindHandInstance(run.Deck, "strike");

        var result = manager.ConsumeCardsFromHand(run.RunId, [cardInstanceId.ToString()], CardConsumeDestination.Discard);
        run = manager.GetRun(run.RunId).Value;

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(run.Deck.Hand, card => card == "strike");
        Assert.Single(run.Deck.DiscardPile, card => card == "strike");
    }

    [Fact]
    public void HasCardInHand_ReturnsWhetherCardExists()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var present = manager.HasCardInHand(
            run.RunId,
            FindHandInstance(run.Deck, "strike").ToString());
        var missing = manager.HasCardInHand(
            run.RunId,
            Guid.Parse("ffffffff-ffff-8fff-bfff-ffffffffffff").ToString());

        Assert.True(present.IsSuccess, present.IsFailure ? present.Error : null);
        Assert.True(missing.IsSuccess, missing.IsFailure ? missing.Error : null);
        Assert.True(present.Value);
        Assert.False(missing.Value);
    }

    [Fact]
    public void CommandGateway_DeduplicatesRetryAndRejectsStaleVersion()
    {
        var manager = CreateManager();
        var started = manager.StartRun(new RunStartOptions(
            "test", "default_run", "hero", Seed: 500UL, ContentRevision: "test")).Value;
        var payload = JsonSerializer.SerializeToElement(new { currentNodeId = "start" });
        var command = new GameplayCommandEnvelope(
            new RunCommandIdentity(
                Guid.NewGuid(),
                RunCommandTypes.ResolveNode,
                started.Sequence,
                started.Determinism.Step,
                CanonicalJson.ComputeHash(payload)),
            payload);

        var first = manager.Execute(started.RunId, command);
        var retry = manager.Execute(started.RunId, command);
        var stalePayload = JsonSerializer.SerializeToElement(new { targetNodeId = "reward" });
        var stale = manager.Execute(started.RunId, new GameplayCommandEnvelope(
            new RunCommandIdentity(
                Guid.NewGuid(),
                RunCommandTypes.AdvanceNode,
                started.Sequence,
                started.Determinism.Step,
                CanonicalJson.ComputeHash(stalePayload)),
            stalePayload));

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error : null);
        Assert.False(first.Value.Duplicate);
        Assert.True(retry.Value.Duplicate);
        Assert.Equal(first.Value.Sequence, retry.Value.Sequence);
        Assert.Equal(first.Value.StateHash, retry.Value.StateHash);
        Assert.True(stale.IsFailure);
        Assert.StartsWith(RunCommandErrors.VersionConflictPrefix, stale.Error);
        Assert.Equal(first.Value.Sequence, manager.GetRun(started.RunId).Value.Sequence);
    }

    [Fact]
    public void CommandGateway_DeduplicatesFromDurableCommitAfterRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heroscript-command-{Guid.NewGuid():N}");
        try
        {
            using var repository = new FileRunCommitStore(path, NullLogger.Instance);
            var manager = CreateManager(repository: repository);
            var started = manager.StartRun(new RunStartOptions(
                "test", "default_run", "hero", Seed: 501UL, ContentRevision: "test")).Value;
            var payload = JsonSerializer.SerializeToElement(new { currentNodeId = "start" });
            var command = new GameplayCommandEnvelope(
                new RunCommandIdentity(
                    Guid.NewGuid(),
                    RunCommandTypes.ResolveNode,
                    started.Sequence,
                    started.Determinism.Step,
                    CanonicalJson.ComputeHash(payload)),
                payload);
            var first = manager.Execute(started.RunId, command);

            var restarted = CreateManager(repository: repository);
            var retry = restarted.Execute(started.RunId, command);

            Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
            Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error : null);
            Assert.True(retry.Value.Duplicate);
            Assert.Equal(first.Value.StateHash, retry.Value.StateHash);
            Assert.Equal(first.Value.Sequence, restarted.GetRun(started.RunId).Value.Sequence);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    private static Guid FindHandInstance(DeckState deck, string definitionId) =>
        deck.HandInstanceIds.First(id => string.Equals(
            deck.GetDefinitionId(id),
            definitionId,
            StringComparison.Ordinal));

    private static float Amount(IEnumerable<ResourceAmount> amounts, string resourceId) =>
        amounts.Single(amount => amount.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase)).Amount;

    private static RunState SetResource(RunState state, string resourceId, float value)
    {
        var resources = state.ResourceState.Resources.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        resources[resourceId] = resources[resourceId].Set(value);
        return state with
        {
            ResourceState = state.ResourceState with { Resources = resources }
        };
    }

    private RunManager CreateManager(
        IPinnedContentCatalog<ScriptModifierDefinition>? scriptModifierManager = null,
        IRunCommitStore? repository = null,
        IContentManifestProvider? contentManifestProvider = null,
        string? runJson = null)
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("runs/default_run.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["default_run"] = JsonDocument.Parse(runJson ?? RunJson).RootElement.GetProperty("default_run").Clone()
            });
        _resourceLoader
            .Setup(m => m.LoadResource("card-selections/basic_reward.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["basic_reward"] = JsonDocument.Parse(CardSelectionJson).RootElement.GetProperty("basic_reward").Clone()
            });
        _resourceLoader
            .Setup(m => m.LoadResource("shops/basic_shop.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["basic_shop"] = JsonDocument.Parse(ShopJson).RootElement.GetProperty("basic_shop").Clone()
            });
        _resourceLoader
            .Setup(m => m.LoadResource("preparations/basic_preparation.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["basic_preparation"] = JsonDocument.Parse(PreparationJson).RootElement.GetProperty("basic_preparation").Clone()
            });

        return new RunManager(
            _configManager.Object,
            _resourceLoader.Object,
            scriptModifierCatalog: scriptModifierManager,
            repository: repository,
            contentManifestProvider: contentManifestProvider,
            resources: TestDataBuilders.MockResourceManager().Object);
    }

    private static CombatActorState CreateCombatActorState(string entityId, bool isHero)
    {
        return new CombatActorState
        {
            InstanceId = entityId,
            Name = entityId,
            SideId = isHero ? "player" : "opposition", ControllerBinding = new ControllerBinding { Kind = isHero ? ControllerKind.Player : ControllerKind.AI },
            ResourceState = new ResourceSet
            {
                OwnerId = entityId,
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = new ResourcePool
                    {
                        ResourceId = "health",
                        Current = 10,
                        Maximum = 10,
                        Definition = new ResourceDefinition
                        {
                            ResourceId = "health",
                            DisplayName = "Health",
                            Category = ResourceCategory.VITAL
                        }
                    }
                }
            }
        };
    }

    private RunManager CreateManagerWithContent()
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("runs/default_run.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["default_run"] = JsonDocument.Parse(RunJson).RootElement.GetProperty("default_run").Clone()
            });
        _resourceLoader
            .Setup(m => m.LoadResource("card-selections/pool_reward.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["pool_reward"] = JsonDocument.Parse(PoolCardSelectionJson).RootElement.GetProperty("pool_reward").Clone()
            });
        _resourceLoader
            .Setup(m => m.LoadResource("cards/card_catalog.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(ParseResource(CardCatalogJson));
        _resourceLoader
            .Setup(m => m.LoadResource("card-pools/basic_rewards.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(ParseResource(CardPoolsJson));
        _resourceLoader
            .Setup(m => m.LoadResource("shops/dynamic_shop.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["dynamic_shop"] = JsonDocument.Parse(DynamicShopJson).RootElement.GetProperty("dynamic_shop").Clone()
            });

        var catalog = new CardContentCatalog(_configManager.Object, _resourceLoader.Object);
        var resolver = new CardPoolResolver(_configManager.Object, _resourceLoader.Object, catalog);
        return new RunManager(
            _configManager.Object,
            _resourceLoader.Object,
            resolver,
            catalog,
            resources: TestDataBuilders.MockResourceManager().Object);
    }

    private static Dictionary<string, JsonElement> ParseResource(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private const string RunJson = """
    {
      "default_run": {
        "runId": "default_run",
        "startingResources": { "gold": 25, "power_points": 0 },
        "startingHandSize": 2,
        "startingDeck": ["strike", "defend", "zap"],
        "mapNodes": [
          {
            "nodeId": "start",
            "activity": { "type": "Preparation", "definitionId": "event" },
            "completionPolicy": "Optional",
            "nextNodeIds": ["reward"]
          },
          {
            "nodeId": "reward",
            "activity": { "type": "CardSelection", "definitionId": "basic_reward" },
            "nextNodeIds": []
          }
        ]
      }
    }
    """;

    private const string CombatRunJson = """
    {
      "default_run": {
        "runId": "default_run",
        "startingResources": { "gold": 25, "power_points": 0 },
        "startingHandSize": 1,
        "startingDeck": ["strike", "defend"],
        "mapNodes": [
          {
            "nodeId": "start",
            "activity": { "type": "Encounter" },
            "nextNodeIds": ["reward"]
          },
          {
            "nodeId": "reward",
            "activity": { "type": "CardSelection", "definitionId": "basic_reward" },
            "nextNodeIds": []
          }
        ]
      }
    }
    """;

    private const string CardSelectionJson = """
    {
      "basic_reward": {
        "selectionId": "basic_reward",
        "pickCount": 1,
        "cardPool": ["strike", "defend", "zap"]
      }
    }
    """;

    private const string PoolCardSelectionJson = """
    {
      "pool_reward": {
        "selectionId": "pool_reward",
        "pickCount": 1,
        "offerCount": 2,
        "cardPoolId": "basic_rewards",
        "reroll": {
          "freeRerolls": 1,
          "baseCosts": [{ "resourceId": "gold", "amount": 10 }],
          "costsPerReroll": [{ "resourceId": "gold", "amount": 5 }]
        },
        "decompose": {
          "enabled": true
        }
      }
    }
    """;

    private const string CardCatalogJson = """
    {
      "basic_attack": {
        "cardId": "basic_attack",
        "rarity": "Common",
        "basePrices": [{ "resourceId": "gold", "amount": 10 }],
        "decomposeRewards": [{ "resourceId": "power_points", "amount": 1 }],
        "tags": ["attack", "common", "starter"]
      },
      "fireball": {
        "cardId": "fireball",
        "rarity": "Uncommon",
        "basePrices": [{ "resourceId": "gold", "amount": 25 }],
        "decomposeRewards": [{ "resourceId": "power_points", "amount": 2 }],
        "tags": ["attack", "fire", "magic", "uncommon"]
      },
      "heal": {
        "cardId": "heal",
        "rarity": "Common",
        "basePrices": [{ "resourceId": "gold", "amount": 18 }],
        "decomposeRewards": [{ "resourceId": "power_points", "amount": 1 }],
        "tags": ["heal", "utility", "common"]
      }
    }
    """;

    private const string CardPoolsJson = """
    {
      "basic_rewards": {
        "poolId": "basic_rewards",
        "excludeTags": ["starter"],
        "rarityWeights": {
          "Common": 70,
          "Uncommon": 25
        }
      }
    }
    """;

    private const string ShopJson = """
    {
      "basic_shop": {
        "shopId": "basic_shop",
        "items": [
          { "itemId": "buy_zap", "cardId": "zap", "costs": [{ "resourceId": "gold", "amount": 10 }] }
        ]
      }
    }
    """;

    private const string DynamicShopJson = """
    {
      "dynamic_shop": {
        "shopId": "dynamic_shop",
        "cardPoolId": "basic_rewards",
        "offerCount": 2,
        "pricing": {
          "baseMultiplier": 1.0,
          "rarityMultipliers": {
            "Common": 1.0,
            "Uncommon": 1.25
          },
          "tagMultipliers": {
            "fire": 1.1
          }
        },
        "reroll": {
          "baseCosts": [{ "resourceId": "gold", "amount": 10 }],
          "costsPerReroll": [{ "resourceId": "gold", "amount": 5 }]
        },
        "items": [
          { "itemId": "buy_fireball", "cardId": "fireball", "costs": [{ "resourceId": "gold", "amount": 25 }] }
        ]
      }
    }
    """;

    private const string PreparationJson = """
    {
      "basic_preparation": {
        "preparationId": "basic_preparation",
        "options": [
          { "optionId": "pack_supplies", "costs": [{ "resourceId": "gold", "amount": 5 }], "addCardsToDiscard": ["heal"] },
          {
            "optionId": "train_spell",
            "costs": [{ "resourceId": "power_points", "amount": 1 }],
            "addCardsToDiscard": ["fireball"],
            "applyModifiers": [
              { "ownerId": "run", "modifierId": "flat_power_bonus", "stacks": 1, "duration": -1, "sourceId": "train_spell" }
            ]
          },
          {
            "optionId": "double_train",
            "costs": [{ "resourceId": "power_points", "amount": 2 }],
            "addCardsToDiscard": ["fireball"],
            "applyModifiers": [
              { "ownerId": "run", "modifierId": "flat_power_bonus", "stacks": 1, "duration": -1, "sourceId": "double_train" },
              { "ownerId": "run", "modifierId": "missing_modifier", "stacks": 1, "duration": -1, "sourceId": "double_train" }
            ]
          }
        ]
      }
    }
    """;
}
