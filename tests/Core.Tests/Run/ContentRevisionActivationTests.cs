using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Config;
using Core.Combat.Models;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.Run.Branching;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class ContentRevisionActivationTests
{
    [Fact]
    public async Task ActivateContentRevision_IsJournaledAndChangesOnlyFutureContext()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heroscript-content-activation-{Guid.NewGuid():N}");
        try
        {
            using var repository = new FileRunCommitStore(path, NullLogger.Instance);
            var revisionA = new string('a', 64);
            var revisionB = new string('b', 64);
            var manager = CreateManager(repository, revisionA, revisionB);
            var source = CreateState(revisionA);
            await SeedInitialCommit(repository, source);
            var restored = manager.HydrateForReplay(source);
            Assert.True(restored.IsSuccess, restored.IsFailure ? restored.Error : null);
            var initial = restored.Value;

            var receipt = manager.Execute(initial.RunId, new GameplayCommandEnvelope(
                new RunCommandIdentity(
                    Guid.NewGuid(),
                    RunCommandTypes.ActivateContentRevision,
                    initial.Sequence,
                    initial.Determinism.Step,
                    CanonicalJson.ComputeHash(JsonSerializer.SerializeToElement(new { revision = revisionB }))),
                JsonSerializer.SerializeToElement(new { revision = revisionB })));

            Assert.True(receipt.IsSuccess, receipt.IsFailure ? receipt.Error : null);
            Assert.Equal(revisionB, receipt.Value.State.Determinism.ContentRevision);
            Assert.Equal(revisionB, receipt.Value.State.ContentManifest!.Revision);
            Assert.Equal(initial.Sequence + 1, receipt.Value.Sequence);
            var journal = (await repository.LoadCommitsAsync(initial.RunId))
                .Select(commit => commit.ToJournalEntry())
                .ToArray();
            Assert.Equal(2, journal.Length);
            Assert.Equal(RunCommandTypes.ActivateContentRevision, journal[1].CommandType);
            Assert.Equal(revisionB, journal[1].Command.GetProperty("revision").GetString());
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void ActivateContentRevision_IsDeniedWhenModeDoesNotAllowIt()
    {
        var revisionA = new string('a', 64);
        var manager = CreateManager(null, revisionA, new string('b', 64));
        var source = CreateState(revisionA);
        var state = source with
        {
            ResolvedMode = new ResolvedGameMode
            {
                CapabilityPolicy = new CapabilityPolicyDefinition
                {
                    CapabilityPolicyId = "published",
                    AllowHotReloadActivation = false
                },
                ContentBindingPolicy = new ContentBindingPolicyDefinition
                {
                    ContentBindingPolicyId = "pinned",
                    ActiveRuns = ActiveRunContentBinding.Pinned
                }
            }
        };
        var restored = manager.HydrateForReplay(state);
        Assert.True(restored.IsSuccess, restored.IsFailure ? restored.Error : null);
        state = restored.Value;

        var result = manager.Execute(state.RunId, new GameplayCommandEnvelope(
            new RunCommandIdentity(
                Guid.NewGuid(),
                RunCommandTypes.ActivateContentRevision,
                state.Sequence,
                state.Determinism.Step),
            JsonSerializer.SerializeToElement(new { revision = new string('b', 64) })));

        Assert.True(result.IsFailure);
        Assert.Contains("does not allow", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActivateContentRevision_IsRejectedWhileCombatIsActive()
    {
        var revisionA = new string('a', 64);
        var revisionB = new string('b', 64);
        var manager = CreateManager(null, revisionA, revisionB);
        var combatId = Guid.NewGuid();
        var source = CreateState(revisionA);
        var state = source with
        {
            ActiveEncounterId = combatId,
            Encounters =
            [
                new RunEncounterState
                {
                    NodeId = "combat",
                    Combat = new CombatState
                    {
                        CombatId = combatId,
                        RunId = source.RunId,
                        Status = CombatStatus.ACTIVE,
                        Determinism = DeterministicContext.Create(17UL, revisionA)
                    }
                }
            ]
        };
        state = manager.HydrateForReplay(state).Value;

        var preview = manager.Preview(state.RunId, revisionB);
        var result = Activate(manager, state, revisionB);

        Assert.True(preview.IsSuccess, preview.IsFailure ? preview.Error : null);
        Assert.False(preview.Value.Compatible);
        Assert.Contains(preview.Value.Blockers, blocker =>
            blocker.Contains("active combat", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.IsFailure);
        Assert.Contains("active combat", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(revisionA, manager.GetRun(state.RunId).Value.Determinism.ContentRevision);
    }

    [Fact]
    public void ActivateContentRevision_PreservesCompletedCombatProvenance()
    {
        var revisionA = new string('a', 64);
        var revisionB = new string('b', 64);
        var manager = CreateManager(null, revisionA, revisionB);
        var combatId = Guid.NewGuid();
        var source = CreateState(revisionA);
        var state = source with
        {
            Encounters =
            [
                new RunEncounterState
                {
                    NodeId = "combat",
                    Resolved = true,
                    Outcome = "victory",
                    Combat = new CombatState
                    {
                        CombatId = combatId,
                        RunId = source.RunId,
                        Status = CombatStatus.VICTORY,
                        Determinism = DeterministicContext.Create(17UL, revisionA)
                    }
                }
            ]
        };
        state = manager.HydrateForReplay(state).Value;

        var result = Activate(manager, state, revisionB);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(revisionB, result.Value.State.Determinism.ContentRevision);
        Assert.Equal(revisionA, result.Value.State.Encounters[0].Combat.Determinism.ContentRevision);
    }

    [Fact]
    public void ActivateContentRevision_RejectsPinnedUpgradeIncompatibleWithNewBase()
    {
        var revisionA = new string('a', 64);
        var revisionB = new string('b', 64);
        var runtime = Runtime(revisionB, "effect.new");
        var manager = CreateManagerWithRuntime(revisionA, revisionB, runtime);
        var cardId = Guid.Parse("10000000-0000-8000-8000-000000000001");
        var state = CreateState(revisionA) with
        {
            Deck = new DeckState
            {
                CardInstances = new Dictionary<Guid, CardInstanceState>
                {
                    [cardId] = new()
                    {
                        CardInstanceId = cardId,
                        DefinitionId = "strike",
                        Upgrades =
                        [
                            new CardUpgradeState
                            {
                                UpgradeId = "old-upgrade",
                                Patches =
                                [
                                    new CardEffectNumericPatchDefinition
                                    {
                                        ComponentId = "effect.old",
                                        Attribute = CardEffectNumericAttribute.FlatValue,
                                        Value = 2
                                    }
                                ]
                            }
                        ]
                    }
                },
                HandInstanceIds = [cardId]
            }
        };
        state = manager.HydrateForReplay(state).Value;

        var result = Activate(manager, state, revisionB);

        Assert.True(result.IsFailure);
        Assert.Contains("Component not found: effect.old", result.Error);
        Assert.Equal(revisionA, manager.GetRun(state.RunId).Value.Determinism.ContentRevision);
    }

    [Fact]
    public void ActivateContentRevision_RebindsRunResourcesAndAppliesNewConstraints()
    {
        var revisionA = new string('a', 64);
        var revisionB = new string('b', 64);
        var oldDefinition = Resource("Old Health", canExceedMax: true);
        var newDefinition = Resource("New Health", canExceedMax: false);
        var runtime = RuntimeWithResources(revisionB, newDefinition);
        var manager = CreateManagerWithRuntime(revisionA, revisionB, runtime);
        var state = CreateState(revisionA) with
        {
            ResourceState = new ResourceSet
            {
                OwnerId = "run",
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = new()
                    {
                        ResourceId = "health",
                        Current = 150,
                        Minimum = 0,
                        Maximum = 100,
                        Definition = oldDefinition
                    }
                }
            }
        };
        state = manager.HydrateForReplay(state).Value;

        var result = Activate(manager, state, revisionB);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(150, state.ResourceState.Current("health"));
        Assert.Equal(100, result.Value.State.ResourceState.Current("health"));
        Assert.Equal("New Health", result.Value.State.ResourceState.Get("health")!.Definition.DisplayName);
    }

    [Fact]
    public void ActivateContentRevision_RejectsRemovedResourceAtomically()
    {
        var revisionA = new string('a', 64);
        var revisionB = new string('b', 64);
        var oldDefinition = Resource("Health", canExceedMax: false);
        var runtime = RuntimeWithResources(revisionB, Resource("Mana", false) with { ResourceId = "mana" });
        var manager = CreateManagerWithRuntime(revisionA, revisionB, runtime);
        var state = CreateState(revisionA) with
        {
            ResourceState = new ResourceSet
            {
                OwnerId = "run",
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = new()
                    {
                        ResourceId = "health",
                        Current = 50,
                        Maximum = 100,
                        Definition = oldDefinition
                    }
                }
            }
        };
        state = manager.HydrateForReplay(state).Value;

        var result = Activate(manager, state, revisionB);

        Assert.True(result.IsFailure);
        Assert.Contains("health", result.Error);
        var unchanged = manager.GetRun(state.RunId).Value;
        Assert.Equal(revisionA, unchanged.Determinism.ContentRevision);
        Assert.Equal(50, unchanged.ResourceState.Current("health"));
    }

    [Fact]
    public void ActivateContentRevision_RequiresRuntimeForContentBoundState()
    {
        var revisionA = new string('a', 64);
        var revisionB = new string('b', 64);
        var definition = Resource("Health", canExceedMax: false);
        var manager = CreateManager(null, revisionA, revisionB);
        var state = CreateState(revisionA) with
        {
            ResourceState = new ResourceSet
            {
                OwnerId = "run",
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = new()
                    {
                        ResourceId = "health",
                        Current = 50,
                        Maximum = 100,
                        Definition = definition
                    }
                }
            }
        };
        state = manager.HydrateForReplay(state).Value;

        var result = Activate(manager, state, revisionB);

        Assert.True(result.IsFailure);
        Assert.Contains("runtime resolver", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(revisionA, manager.GetRun(state.RunId).Value.Determinism.ContentRevision);
    }

    [Fact]
    public async Task ActivateContentRevision_PreservesEveryRevisionAcrossSequentialTransitions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heroscript-content-activation-{Guid.NewGuid():N}");
        try
        {
            using var repository = new FileRunCommitStore(path, NullLogger.Instance);
            var revisionA = new string('a', 64);
            var revisionB = new string('b', 64);
            var revisionC = new string('c', 64);
            var manager = CreateManager(repository, revisionA, revisionB, revisionC);
            var source = CreateState(revisionA);
            await SeedInitialCommit(repository, source);
            var initial = manager.HydrateForReplay(source).Value;

            var activatedB = Activate(manager, initial, revisionB);
            Assert.True(activatedB.IsSuccess, activatedB.IsFailure ? activatedB.Error : null);
            var activatedC = Activate(manager, activatedB.Value.State, revisionC);
            Assert.True(activatedC.IsSuccess, activatedC.IsFailure ? activatedC.Error : null);
            Assert.Equal(revisionC, activatedC.Value.State.Determinism.ContentRevision);

            var journal = (await repository.LoadCommitsAsync(initial.RunId))
                .Select(commit => commit.ToJournalEntry())
                .ToArray();
            Assert.Equal(3, journal.Length);
            Assert.Equal(revisionB, journal[1].Command.GetProperty("revision").GetString());
            Assert.Equal(revisionC, journal[2].Command.GetProperty("revision").GetString());
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    private static RunManager CreateManager(
        IRunCommitStore? repository,
        string revisionA,
        params string[] additionalRevisions)
    {
        var config = new Mock<IConfigManager>();
        var resources = new Mock<IResourceLoader>();
        var manifests = new Mock<IContentManifestProvider>();
        var manifestsByRevision = new[] { revisionA }
            .Concat(additionalRevisions)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                revision => revision,
                revision => new ContentManifest { ConfigName = "test", Revision = revision },
                StringComparer.Ordinal);
        manifests.Setup(item => item.GetManifest("test"))
            .Returns(Core.Common.Result<ContentManifest>.Success(manifestsByRevision[revisionA]));
        manifests.Setup(item => item.GetByRevision(It.IsAny<string>()))
            .Returns((string revision) => manifestsByRevision.TryGetValue(revision, out var manifest)
                ? Core.Common.Result<ContentManifest>.Success(manifest)
                : Core.Common.Result<ContentManifest>.Failure($"Manifest not found: {revision}"));
        return new RunManager(
            config.Object,
            resources.Object,
            repository: repository,
            contentManifestProvider: manifests.Object);
    }

    private static RunManager CreateManagerWithRuntime(
        string revisionA,
        string revisionB,
        ContentRuntime targetRuntime)
    {
        var config = new Mock<IConfigManager>();
        var resources = new Mock<IResourceLoader>();
        var manifests = new Mock<IContentManifestProvider>();
        var manifestA = new ContentManifest { ConfigName = "test", Revision = revisionA };
        var manifestB = new ContentManifest { ConfigName = "test", Revision = revisionB };
        manifests.Setup(item => item.GetManifest("test"))
            .Returns(Core.Common.Result<ContentManifest>.Success(manifestA));
        manifests.Setup(item => item.GetByRevision(revisionA))
            .Returns(Core.Common.Result<ContentManifest>.Success(manifestA));
        manifests.Setup(item => item.GetByRevision(revisionB))
            .Returns(Core.Common.Result<ContentManifest>.Success(manifestB));
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(item => item.Resolve(revisionB, "test"))
            .Returns(Core.Common.Result<ContentRuntime>.Success(targetRuntime));
        return new RunManager(
            config.Object,
            resources.Object,
            contentManifestProvider: manifests.Object,
            contentRuntimes: runtimes.Object);
    }

    private static ContentRuntime Runtime(string revision, string componentId)
    {
        const string path = "cards/catalog.json";
        var artifacts = new Dictionary<string, JsonElement>
        {
            [path] = JsonSerializer.SerializeToElement(new Dictionary<string, CardContentDefinition>
            {
                ["strike"] = new()
                {
                    CardId = "strike",
                    Components =
                    [
                        new CardEffectComponentDefinition
                        {
                            ComponentId = componentId,
                            Effect = new EffectDefinition
                            {
                                EffectId = "strike.damage",
                                Type = EffectType.DAMAGE,
                                TargetResource = "health",
                                FlatValue = 5
                            }
                        },
                        new CardDispositionComponentDefinition
                        {
                            ComponentId = "disposition",
                            Destination = CardConsumeDestination.Discard
                        }
                    ]
                }
            })
        }.ToImmutableDictionary(StringComparer.Ordinal);
        var created = ContentRuntime.Create(new ContentBundle
        {
            Manifest = new ContentManifest
            {
                ConfigName = "test",
                Revision = revision,
                Artifacts =
                [
                    new ContentArtifactManifest
                    {
                        Kind = "cards",
                        Path = path,
                        DefinitionCount = 1
                    }
                ]
            },
            Artifacts = artifacts
        });
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error : null);
        return created.Value;
    }

    private static ContentRuntime RuntimeWithResources(
        string revision,
        params ResourceDefinition[] definitions)
    {
        const string path = "resources/catalog.json";
        var artifacts = new Dictionary<string, JsonElement>
        {
            [path] = JsonSerializer.SerializeToElement(definitions.ToDictionary(
                definition => definition.ResourceId,
                StringComparer.Ordinal))
        }.ToImmutableDictionary(StringComparer.Ordinal);
        var created = ContentRuntime.Create(new ContentBundle
        {
            Manifest = new ContentManifest
            {
                ConfigName = "test",
                Revision = revision,
                Artifacts =
                [
                    new ContentArtifactManifest
                    {
                        Kind = "resources",
                        Path = path,
                        DefinitionCount = definitions.Length
                    }
                ]
            },
            Artifacts = artifacts
        });
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error : null);
        return created.Value;
    }

    private static ResourceDefinition Resource(string displayName, bool canExceedMax) => new()
    {
        ResourceId = "health",
        DisplayName = displayName,
        DefaultMin = 0,
        DefaultMax = 100,
        DefaultCurrent = 100,
        CanExceedMax = canExceedMax
    };

    private static RunState CreateState(string revision)
    {
        var runId = Guid.NewGuid();
        return new RunState
        {
            RunId = runId,
            Sequence = 1,
            Lineage = RunLineage.Root(runId),
            ConfigName = "test",
            ModeId = "combat_sandbox",
            ContentManifest = new ContentManifest { ConfigName = "test", Revision = revision },
            Determinism = DeterministicContext.Create(17UL, revision).AdvanceStep(),
            ResolvedMode = new ResolvedGameMode
            {
                CapabilityPolicy = new CapabilityPolicyDefinition
                {
                    CapabilityPolicyId = "theorycraft_tools",
                    AllowHotReloadActivation = true
                },
                ContentBindingPolicy = new ContentBindingPolicyDefinition
                {
                    ContentBindingPolicyId = "development_versioned",
                    ActiveRuns = ActiveRunContentBinding.Versioned,
                    ActivationBoundary = ContentActivationBoundary.OutsideCombat
                }
            }
        };
    }

    private static async Task SeedInitialCommit(IRunCommitStore repository, RunState state)
    {
        var payload = JsonSerializer.SerializeToElement(new { revision = state.Determinism.ContentRevision });
        var frame = new RunCommitFrame
        {
            FrameIndex = 0,
            Step = state.Determinism.Step,
            Scope = "run",
            Kind = RunCommandTypes.StartRun,
            ResultHash = CanonicalJson.ComputeHash(state),
            Resolution = payload
        };
        await repository.AppendAsync(new RunCommit
        {
            RunId = state.RunId,
            Sequence = 1,
            RootCommand = new RunCommandIdentity(
                DeterministicId.Create(state.Determinism.Seed, 1, "content-activation-start"),
                RunCommandTypes.StartRun,
                0,
                0,
                CanonicalJson.ComputeHash(payload)),
            Command = payload,
            StateHash = CanonicalJson.ComputeHash(state),
            BeforeStep = 0,
            AfterStep = state.Determinism.Step,
            LogicalTimestamp = state.Determinism.LogicalTimestamp.UtcDateTime,
            StateAfter = state,
            Lineage = state.Lineage,
            Frames = [frame],
            Facts = RunCommitFacts.FromFrames([frame])
        });
    }

    private static Core.Common.Result<RunCommandReceipt> Activate(
        RunManager manager,
        RunState state,
        string revision)
    {
        var payload = JsonSerializer.SerializeToElement(new { revision });
        return manager.Execute(state.RunId, new GameplayCommandEnvelope(
            new RunCommandIdentity(
                Guid.NewGuid(),
                RunCommandTypes.ActivateContentRevision,
                state.Sequence,
                state.Determinism.Step,
                CanonicalJson.ComputeHash(payload)),
            payload));
    }
}
