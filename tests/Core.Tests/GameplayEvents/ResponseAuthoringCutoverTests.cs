using System.Text.Json;
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
        var source = Core.Common.Result<ContentBundle>.Success(HistoricalBundle());
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
    public void CurrentSchemaRejectsHistoricalHooksAndSchemaOneCannotGrantCanonicalAuthoring()
    {
        var source = HistoricalBundle();
        var plan = GameplayContentResponseMigration.Plan(source).Value;
        var strictLegacy = new ContentGraphValidator().Validate(source with { Manifest = source.Manifest with { SchemaVersion = 2 } });
        Assert.False(strictLegacy.IsValid);
        Assert.Contains(strictLegacy.Errors, message => message.Contains("historical", StringComparison.Ordinal));
        var wrongSchema = ContentRuntime.Create(plan.Candidate with { Manifest = plan.Candidate.Manifest with { SchemaVersion = 1 } });
        Assert.True(wrongSchema.IsFailure);
        Assert.Equal("34", DeterministicContext.CurrentEngineVersion); // published schema-1 execution and hashes remain compatible
    }
    private static ContentBundle HistoricalBundle()
    {
        var document = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, Core.StatusEffects.StatusEffectDefinition> {
            ["charges"] = new() { StatusId = "charges", Triggers = [new() { TriggerId = "tick", Boundary = "EndActivation",
                Effects = [new() { Type = Core.Effects.EffectType.REMOVE_STATUS, StatusId = "charges" }] }] } });
        var artifact = new ContentArtifactManifest { Kind = "status-effects", Path = "status-effects/test.json", DefinitionCount = 1,
            Hash = CanonicalJson.ComputeHash(document.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>()!) };
        var manifest = new ContentManifest { SchemaVersion = 1, ConfigName = "historical", ConfigChain = ["historical@1.0.0"], Artifacts = [artifact] };
        manifest = manifest with { Revision = CanonicalJson.ComputeHash(new { manifest.SchemaVersion, manifest.ConfigName, manifest.ConfigChain, manifest.Artifacts }) };
        return new() { Manifest = manifest, Artifacts = System.Collections.Immutable.ImmutableDictionary<string, System.Text.Json.JsonElement>.Empty.Add(artifact.Path, document) };
    }

}
