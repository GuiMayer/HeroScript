using Core.Config;
using Core.Content;
using Core.Content.Migrations;
using Core.Determinism;
using Mods;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class ResponseAuthoringCutoverTests
{
    [Fact]
    public async Task CanonicalSchemaPublishesAndRestartsWithoutChangingHistoricalBundle()
    {
        var root = ResourceProviderFactory.FindProjectRoot()!;
        var source = await new SettingCompiler([new DirectoryPackageProvider("shipped", Path.Combine(root, "data/configs"))]).CompileBundleAsync("default");
        Assert.True(source.IsSuccess, source.IsFailure ? source.Error : null);
        var oldHash = CanonicalJson.ComputeHash(source.Value);
        var plan = GameplayContentResponseMigration.Plan(source.Value);
        Assert.True(plan.IsSuccess, plan.IsFailure ? plan.Error : null);
        Assert.Equal(2, plan.Value.Candidate.Manifest.SchemaVersion);
        var validation = new ContentGraphValidator().Validate(plan.Value.Candidate);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        var directory = Path.Combine(Path.GetTempPath(), "heroscript-response-cutover-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new ContentPublicationService(directory, new ContentManifestProvider()))
            {
                Assert.True((await store.PublishBundleAsync(source.Value)).IsSuccess);
                var published = await store.PublishBundleAsync(plan.Value.Candidate);
                Assert.True(published.IsSuccess, published.IsFailure ? published.Error : null);
            }
            using var restarted = new ContentPublicationService(directory, new ContentManifestProvider());
            var old = await restarted.GetPublishedAsync(source.Value.Manifest.Revision);
            var current = await restarted.GetPublishedAsync(plan.Value.Candidate.Manifest.Revision);
            Assert.True(old.IsSuccess, old.IsFailure ? old.Error : null);
            Assert.True(current.IsSuccess, current.IsFailure ? current.Error : null);
            Assert.Equal(oldHash, CanonicalJson.ComputeHash(old.Value));
            Assert.Equal(CanonicalJson.ComputeHash(plan.Value.Candidate), CanonicalJson.ComputeHash(current.Value));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CurrentSchemaRejectsHistoricalHooksAndSchemaOneCannotGrantCanonicalAuthoring()
    {
        var root = ResourceProviderFactory.FindProjectRoot()!;
        var source = (await new SettingCompiler([new DirectoryPackageProvider("shipped", Path.Combine(root, "data/configs"))]).CompileBundleAsync("default")).Value;
        var plan = GameplayContentResponseMigration.Plan(source).Value;
        var strictLegacy = new ContentGraphValidator().Validate(source with { Manifest = source.Manifest with { SchemaVersion = 2 } });
        Assert.False(strictLegacy.IsValid);
        Assert.Contains(strictLegacy.Errors, message => message.Contains("historical", StringComparison.Ordinal));
        var wrongSchema = ContentRuntime.Create(plan.Candidate with { Manifest = plan.Candidate.Manifest with { SchemaVersion = 1 } });
        Assert.True(wrongSchema.IsFailure);
        Assert.Equal("34", DeterministicContext.CurrentEngineVersion); // published schema-1 execution and hashes remain compatible
    }
}
