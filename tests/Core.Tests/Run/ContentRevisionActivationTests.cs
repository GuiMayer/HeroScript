using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Run;
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
            using var repository = new VersionedRunStateRepository(path, NullLogger.Instance);
            var revisionA = new string('a', 64);
            var revisionB = new string('b', 64);
            var manager = CreateManager(repository, revisionA, revisionB);
            var restored = manager.RestoreState(CreateState(revisionA));
            Assert.True(restored.IsSuccess, restored.IsFailure ? restored.Error : null);
            var initial = restored.Value;

            var receipt = manager.Execute(initial.RunId, new RunCommand(
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
            var journal = await repository.LoadJournalAsync(initial.RunId, 0, 10);
            Assert.Equal(2, journal.Count);
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
        var state = CreateState(revisionA) with
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
                    ActiveRuns = "pinned"
                }
            }
        };
        var restored = manager.RestoreState(state);
        Assert.True(restored.IsSuccess, restored.IsFailure ? restored.Error : null);
        state = restored.Value;

        var result = manager.Execute(state.RunId, new RunCommand(
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
    public async Task ActivateContentRevision_PreservesEveryRevisionAcrossSequentialTransitions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heroscript-content-activation-{Guid.NewGuid():N}");
        try
        {
            using var repository = new VersionedRunStateRepository(path, NullLogger.Instance);
            var revisionA = new string('a', 64);
            var revisionB = new string('b', 64);
            var revisionC = new string('c', 64);
            var manager = CreateManager(repository, revisionA, revisionB, revisionC);
            var initial = manager.RestoreState(CreateState(revisionA)).Value;

            var activatedB = Activate(manager, initial, revisionB);
            Assert.True(activatedB.IsSuccess, activatedB.IsFailure ? activatedB.Error : null);
            var activatedC = Activate(manager, activatedB.Value.State, revisionC);
            Assert.True(activatedC.IsSuccess, activatedC.IsFailure ? activatedC.Error : null);
            Assert.Equal(revisionC, activatedC.Value.State.Determinism.ContentRevision);

            var journal = await repository.LoadJournalAsync(initial.RunId, 0, 10);
            Assert.Equal(3, journal.Count);
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
        IRunStateRepository? repository,
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

    private static RunState CreateState(string revision) => new()
    {
        RunId = Guid.NewGuid(),
        Sequence = 1,
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
                ActiveRuns = "allow_versioned_activation",
                ActivationBoundary = "next_command"
            }
        }
    };

    private static Core.Common.Result<RunCommandReceipt> Activate(
        RunManager manager,
        RunState state,
        string revision)
    {
        var payload = JsonSerializer.SerializeToElement(new { revision });
        return manager.Execute(state.RunId, new RunCommand(
            new RunCommandIdentity(
                Guid.NewGuid(),
                RunCommandTypes.ActivateContentRevision,
                state.Sequence,
                state.Determinism.Step,
                CanonicalJson.ComputeHash(payload)),
            payload));
    }
}
