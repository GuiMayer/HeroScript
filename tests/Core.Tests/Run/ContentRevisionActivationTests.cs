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

    private static RunManager CreateManager(
        IRunStateRepository? repository,
        string revisionA,
        string revisionB)
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
}
