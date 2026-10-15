using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Calculations;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Events;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Resources;
using Core.Run;
using Core.Run.Branching;
using Core.Run.Content;
using Core.Run.Loadouts;
using Core.Run.Replay;
using Core.Run.Runtime;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class InscriptionDurabilityTests
{
    [Theory]
    [InlineData(InscriptionInventoryScope.Actor)]
    [InlineData(InscriptionInventoryScope.Run)]
    public async Task CanonicalEquipmentPersistsRetriesReplaysAndIsolatesBranches(InscriptionInventoryScope scope)
    {
        var (initial, content, _) = InscriptionLoadoutTests.Setup(LoadoutTestContent.Policy(scope));
        var parent = InscriptionAcquisition.Grant(initial, content, "alice", "focus", "reward:one").Value with
        { Sequence = 1, Lineage = RunLineage.Root(initial.RunId), Deck = TestCardZones.FromDefinitions(("collection", Array.Empty<string>())) };
        var sourceHash = CanonicalJson.ComputeHash(parent);
        var fork = new RunBranchStartCommand(parent.RunId, parent.Sequence, "inscriptions", sourceHash);
        var branch = RunBranchTransitions.Create(parent, fork).Value;
        Assert.Equal(sourceHash, CanonicalJson.ComputeHash(parent));
        var policy = content.GetDefinition<LoadoutPolicyDefinition>("loadout-policies", "loadout").Value;
        Assert.True(branch.Loadouts!.Validate(branch.RunId, policy).IsSuccess);
        var directory = Path.Combine(Path.GetTempPath(), "heroscript-inscription-" + Guid.NewGuid().ToString("N"));
        var factory = Factory(content);
        GameplayCommandEnvelope command;
        string finalHash;
        using (var store = new FileRunCommitStore(directory, NullLogger.Instance))
        {
            await store.AppendAsync(InitialCommit(parent, "TEST", new { }));
            await store.AppendAsync(InitialCommit(branch, RunCommandTypes.CreateBranchFromHistory, fork, parent.Determinism.Step));
            var runtime = factory.Create(new(GameplayPersistenceMode.Authoritative, store));
            Assert.True(runtime.Runs.HydrateForReplay(branch).IsSuccess);
            var payload = new ApplyLoadoutCommand("alice", ImmutableSortedDictionary<string, Guid>.Empty.Add("one", InscriptionLoadoutTests.Item(branch)));
            command = new(new(DeterministicId.Create(77, 23, "equip"), RunCommandTypes.ApplyLoadout, branch.Sequence, branch.Determinism.Step), JsonSerializer.SerializeToElement(payload));
            var queries = new LoadoutQueryService(runtime.Runs, Runtimes(content), new(Runtimes(content)));
            var preview = queries.Preview(branch.RunId, new(payload.ActorId, payload.Assignments, branch.Sequence, branch.Determinism.Step));
            Assert.True(preview.IsSuccess, preview.IsFailure ? preview.Error : null);
            Assert.Empty(queries.Read(branch.RunId, "alice").Value.Assignments);
            Assert.Equal(3, preview.Value.After.UsedCapacity);
            Assert.Equal(branch.Determinism.Step, preview.Value.After.ExpectedStep);
            Assert.Single(await store.LoadCommitsAsync(branch.RunId));
            var unavailable = new Mock<IRunCommitStore>();
            unavailable.Setup(value => value.LoadLatestStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, CancellationToken token) => store.LoadLatestStateAsync(id, token));
            unavailable.Setup(value => value.LoadCommitsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, CancellationToken token) => store.LoadCommitsAsync(id, token));
            unavailable.Setup(value => value.FindCommandAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, Guid commandId, CancellationToken token) => store.FindCommandAsync(id, commandId, token));
            unavailable.Setup(value => value.LoadCommitAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, int sequence, CancellationToken token) => store.LoadCommitAsync(id, sequence, token));
            unavailable.Setup(value => value.AppendAsync(It.IsAny<RunCommit>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Synthetic inscription persistence failure"));
            var failedRuntime = factory.Create(new(GameplayPersistenceMode.Authoritative, unavailable.Object));
            Assert.True(failedRuntime.Runs.HydrateForReplay(branch).IsSuccess);
            Assert.True(failedRuntime.Gateway.Execute(branch.RunId, command).IsFailure);
            Assert.Equal(CanonicalJson.ComputeHash(branch), CanonicalJson.ComputeHash(failedRuntime.Runs.GetRun(branch.RunId).Value));
            Assert.Empty((await store.LoadLatestStateAsync(branch.RunId))!.Loadouts!.Actors["alice"].Assignments);
            var applied = runtime.Gateway.Execute(branch.RunId, command);
            Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
            finalHash = applied.Value.Receipt.StateHash;
            Assert.Equal(CanonicalJson.ComputeHash(preview.Value.After.Assignments), CanonicalJson.ComputeHash(applied.Value.Receipt.State.Loadouts!.Actors["alice"].Assignments));
            Assert.Equal(sourceHash, CanonicalJson.ComputeHash(parent));
            var stale = runtime.Gateway.Execute(branch.RunId, command with { Identity = command.Identity with { CommandId = DeterministicId.Create(77, 24, "stale") } });
            Assert.True(stale.IsFailure);
            Assert.StartsWith(RunCommandErrors.VersionConflictPrefix, stale.Error);
            Assert.True(queries.Preview(branch.RunId, new(payload.ActorId, payload.Assignments, branch.Sequence, branch.Determinism.Step)).IsFailure);
        }
        using var restarted = new FileRunCommitStore(directory, NullLogger.Instance);
        var state = (await restarted.LoadLatestStateAsync(branch.RunId))!;
        Assert.Equal(finalHash, CanonicalJson.ComputeHash(state));
        var retry = factory.Create(new(GameplayPersistenceMode.Authoritative, restarted)).Gateway.Execute(branch.RunId, command);
        Assert.True(retry.IsSuccess && retry.Value.Receipt.Duplicate);
        Assert.Equal(finalHash, retry.Value.Receipt.StateHash);
        Assert.Equal(2, (await restarted.LoadCommitsAsync(branch.RunId)).Count);
        var replay = new RunSemanticReplayService(restarted, factory);
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var verified = await replay.VerifyAsync(branch.RunId);
            Assert.True(verified.IsValid, string.Join("; ", verified.Errors));
            Assert.True(verified.Reexecuted);
            Assert.Equal(finalHash, verified.ActualFinalHash);
        }
    }

    [Fact]
    public async Task AuthorizedTransferSurvivesRestartAndSemanticReplayAcrossPrivateInventories()
    {
        var (initial, content, _) = InscriptionLoadoutTests.Setup(LoadoutTestContent.Policy() with { AllowTransfers = true });
        var parent = InscriptionAcquisition.Grant(initial, content, "alice", "focus", "reward:one").Value with
        { Sequence = 1, Lineage = RunLineage.Root(initial.RunId), Deck = TestCardZones.FromDefinitions(("collection", Array.Empty<string>())) };
        var fork = new RunBranchStartCommand(parent.RunId, 1, "transfer", CanonicalJson.ComputeHash(parent));
        var branch = RunBranchTransitions.Create(parent, fork).Value;
        var instanceId = InscriptionLoadoutTests.Item(branch);
        var command = new GameplayCommandEnvelope(new(DeterministicId.Create(77, 25, "transfer"), RunCommandTypes.TransferInscription,
            branch.Sequence, branch.Determinism.Step), JsonSerializer.SerializeToElement(new TransferInscriptionCommand("alice", "bob", instanceId)));
        var directory = Path.Combine(Path.GetTempPath(), "heroscript-inscription-transfer-" + Guid.NewGuid().ToString("N"));
        var factory = Factory(content);
        string finalHash;
        using (var store = new FileRunCommitStore(directory, NullLogger.Instance))
        {
            await store.AppendAsync(InitialCommit(parent, "TEST", new { }));
            await store.AppendAsync(InitialCommit(branch, RunCommandTypes.CreateBranchFromHistory, fork, parent.Determinism.Step));
            var runtime = factory.Create(new(GameplayPersistenceMode.Authoritative, store));
            Assert.True(runtime.Runs.HydrateForReplay(branch).IsSuccess);
            var result = runtime.Gateway.Execute(branch.RunId, command);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            finalHash = result.Value.Receipt.StateHash;
            Assert.Equal(instanceId, InscriptionLoadoutTests.Item(result.Value.Receipt.State, "bob"));
            Assert.Equal(instanceId, InscriptionLoadoutTests.Item(parent, "alice"));
        }
        using var restarted = new FileRunCommitStore(directory, NullLogger.Instance);
        var retry = factory.Create(new(GameplayPersistenceMode.Authoritative, restarted)).Gateway.Execute(branch.RunId, command);
        Assert.True(retry.IsSuccess && retry.Value.Receipt.Duplicate);
        Assert.Equal(finalHash, retry.Value.Receipt.StateHash);
        Assert.Equal(2, (await restarted.LoadCommitsAsync(branch.RunId)).Count);
        var replay = new RunSemanticReplayService(restarted, factory);
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var result = await replay.VerifyAsync(branch.RunId);
            Assert.True(result.IsValid && result.Reexecuted, string.Join("; ", result.Errors));
            Assert.Equal(finalHash, result.ActualFinalHash);
        }
    }

    [Fact]
    public void QueriesRespectPrivateInventoriesAndInvalidateDerivedCacheBySnapshot()
    {
        var (run, content, planner) = InscriptionLoadoutTests.Setup();
        run = InscriptionAcquisition.Grant(run, content, "alice", "focus", "one").Value;
        var runs = new Mock<IRunQueryService>();
        runs.Setup(value => value.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));
        var service = new LoadoutQueryService(runs.Object, Runtimes(content), planner);
        Assert.Empty(service.Read(run, "bob").Value.Items);
        Assert.Single(service.Read(run, "alice").Value.Items);
        var changed = planner.Plan(run, "alice", ImmutableSortedDictionary<string, Guid>.Empty.Add("one", InscriptionLoadoutTests.Item(run))).Value.Candidate;
        Assert.Empty(service.Read(run, "alice").Value.Assignments);
        Assert.Single(service.Read(changed, "alice").Value.Assignments);
        Assert.True(service.Read(run, "ghost").IsFailure);
    }

    private static IContentRuntimeResolver Runtimes(ContentRuntime content)
    {
        var mock = new Mock<IContentRuntimeResolver>();
        mock.Setup(value => value.Resolve(content.Manifest.Revision, "default")).Returns(Result<ContentRuntime>.Success(content));
        return mock.Object;
    }

    private static GameplayRuntimeFactory Factory(ContentRuntime content)
    {
        var manifests = new Mock<IContentManifestProvider>();
        manifests.Setup(value => value.GetByRevision(content.Manifest.Revision)).Returns(Result<ContentManifest>.Success(content.Manifest));
        return new(Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>(), Mock.Of<ICardPoolResolver>(), Mock.Of<ICardContentCatalog>(),
            Mock.Of<IPinnedContentCatalog<ScriptModifierDefinition>>(), manifests.Object, Mock.Of<IResourceCatalog<GameModeDefinition>>(),
            Mock.Of<IGameModeResolver>(), Mock.Of<IContentPublicationService>(), Runtimes(content), Mock.Of<IResourceManager>(),
            Mock.Of<ICombatFactory>(), Mock.Of<ICombatFlowPlanner>(), Mock.Of<ICombatCommandHandler>(), Mock.Of<IAutomaticFlowDriver>(),
            Mock.Of<IEffectTriggerExecutor>(), new GameEventContextAccessor(), GameplayCommandCodec.CreateDefault());
    }

    private static RunCommit InitialCommit(RunState state, string type, object command, ulong beforeStep = 0)
    {
        var payload = JsonSerializer.SerializeToElement(command);
        var hash = CanonicalJson.ComputeHash(state);
        var frames = new[] { new RunCommitFrame { FrameIndex = 0, Step = state.Determinism.Step, Kind = type, ResultHash = hash, Resolution = payload } };
        return new() { RunId = state.RunId, Sequence = 1, RootCommand = new(DeterministicId.Create(77, 0, type), type, 0, beforeStep, CanonicalJson.ComputeHash(payload)),
            Command = payload, StateHash = hash, StateAfter = state, Lineage = state.Lineage, BeforeStep = beforeStep, AfterStep = state.Determinism.Step,
            LogicalTimestamp = state.Determinism.LogicalTimestamp.UtcDateTime, Frames = frames, Facts = RunCommitFacts.FromFrames(frames) };
    }
}
