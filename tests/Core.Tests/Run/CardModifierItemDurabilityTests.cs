using System.Text.Json;
using Core.Determinism;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Run;
using Core.Run.Branching;
using Core.Run.Content;
using Core.Run.Replay;
using Core.Run.Runtime;
using Xunit;
using Core.Abstractions.Persistence;
using Moq;

namespace Core.Tests.Run;

public sealed partial class CardTransformationCompositionTests
{
    [Theory]
    [InlineData(CardModifierItemUse.Consume)]
    [InlineData(CardModifierItemUse.Bind)]
    public async Task ModifierAcquisitionAndApplicationPersistReplayAndIsolateBranches(CardModifierItemUse use)
    {
        var directory = Path.Combine(Path.GetTempPath(), "heroscript-modifier-items-" + Guid.NewGuid().ToString("N"));
        var content = Runtime([Numeric("plus", 2)], modifierItem: new() { ItemDefinitionId = "unit", UpgradeId = "plus", Use = use });
        var factory = Factory(content);
        var parent = State() with { CurrentNodeId = "forge", Map = new() { Nodes = [new() { NodeId = "forge",
            Activity = new() { Type = RunActivityType.CardUpgrade, Parameters = new Dictionary<string, JsonElement> {
                ["modifierItemIds"] = JsonSerializer.SerializeToElement(new[] { "unit" }) } } }] } };
        var parentHash = CanonicalJson.ComputeHash(parent);
        var fork = new RunBranchStartCommand(parent.RunId, parent.Sequence, "modifier-test", parentHash);
        var branch = RunBranchTransitions.Create(parent, fork).Value;
        string finalHash;
        GameplayCommandEnvelope apply;
        using (var store = new FileRunCommitStore(directory, NullLogger.Instance))
        {
            await store.AppendAsync(InitialCommit(parent, "TEST", new { }));
            await store.AppendAsync(InitialCommit(branch, RunCommandTypes.CreateBranchFromHistory, fork, parent.Determinism.Step));
            var runtime = factory.Create(new(GameplayPersistenceMode.Authoritative, store));
            Assert.True(runtime.Runs.HydrateForReplay(branch).IsSuccess);
            var acquired = runtime.Gateway.Execute(branch.RunId, Envelope(branch, RunCommandTypes.AcquireCardModifierItem,
                new AcquireCardModifierItemCommand("unit"), 20));
            Assert.True(acquired.IsSuccess, acquired.IsFailure ? acquired.Error : null);
            var owned = acquired.Value.Receipt.State;
            Assert.Empty(owned.Deck.GetCard(CardId)!.Upgrades);
            var unit = Assert.Single(owned.CardModifierItems!.Items);
            apply = Envelope(owned, RunCommandTypes.ApplyCardModifierItem, new ApplyCardModifierItemCommand(unit.ItemInstanceId, CardId), 21);
            var failingStore = new Mock<IRunCommitStore>();
            failingStore.Setup(value => value.LoadLatestStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, CancellationToken token) => store.LoadLatestStateAsync(id, token));
            failingStore.Setup(value => value.LoadCommitsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, CancellationToken token) => store.LoadCommitsAsync(id, token));
            failingStore.Setup(value => value.FindCommandAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, Guid commandId, CancellationToken token) => store.FindCommandAsync(id, commandId, token));
            failingStore.Setup(value => value.LoadCommitAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, int sequence, CancellationToken token) => store.LoadCommitAsync(id, sequence, token));
            failingStore.Setup(value => value.AppendAsync(It.IsAny<RunCommit>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Synthetic storage failure"));
            var failedRuntime = factory.Create(new(GameplayPersistenceMode.Authoritative, failingStore.Object));
            Assert.True(failedRuntime.Runs.HydrateForReplay(owned).IsSuccess);
            var failed = failedRuntime.Gateway.Execute(branch.RunId, apply);
            Assert.True(failed.IsFailure);
            Assert.Equal(CanonicalJson.ComputeHash(owned), CanonicalJson.ComputeHash(failedRuntime.Runs.GetRun(branch.RunId).Value));
            Assert.Equal(CardModifierItemState.Available, (await store.LoadLatestStateAsync(branch.RunId))!.CardModifierItems!.Items[0].State);
            var applied = runtime.Gateway.Execute(branch.RunId, apply);
            Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
            finalHash = applied.Value.Receipt.StateHash;
            Assert.Equal(parentHash, CanonicalJson.ComputeHash(parent));
            Assert.Equal(8, Resolve(applied.Value.Receipt.State.Deck.GetCard(CardId)!, content).All<CardEffectComponentDefinition>()[0].Effect.FlatValue);
        }
        using var restarted = new FileRunCommitStore(directory, NullLogger.Instance);
        var restored = (await restarted.LoadLatestStateAsync(branch.RunId))!;
        Assert.Equal(finalHash, CanonicalJson.ComputeHash(restored));
        var retry = factory.Create(new(GameplayPersistenceMode.Authoritative, restarted)).Gateway.Execute(branch.RunId, apply);
        Assert.True(retry.IsSuccess && retry.Value.Receipt.Duplicate);
        Assert.Equal(finalHash, retry.Value.Receipt.StateHash);
        var replay = new RunSemanticReplayService(restarted, factory);
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var verified = await replay.VerifyAsync(branch.RunId);
            Assert.True(verified.IsValid, string.Join("; ", verified.Errors));
            Assert.True(verified.Reexecuted);
            Assert.Equal(finalHash, verified.ActualFinalHash);
        }
        Assert.Single(await restarted.LoadCommitsAsync(parent.RunId));
        // Keep isolated temporary history for diagnostics; no deletion touches user data.
    }
}
