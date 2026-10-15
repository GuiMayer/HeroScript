using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Content;
using Core.Determinism;
using Moq;
using Xunit;

namespace Core.Tests.Content;

public sealed class ContentPublicationServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"heroscript-content-{Guid.NewGuid():N}");

    private static ContentBundle BoardBundle(int value = 1)
    {
        var definitions = JsonSerializer.SerializeToElement(new { test = new { value } });
        var artifacts = new[] { new ContentArtifactManifest
        {
            Kind = "boards", Path = "boards/test.json", DefinitionCount = 1,
            Hash = CanonicalJson.ComputeHash(definitions.Deserialize<Dictionary<string, JsonElement>>()!)
        } };
        var payload = new { SchemaVersion = 1, ConfigName = "test", ConfigChain = new[] { "test" }, Artifacts = artifacts };
        return new()
        {
            Manifest = new() { SchemaVersion = 1, ConfigName = "test", ConfigChain = ["test"],
                Artifacts = artifacts, Revision = CanonicalJson.ComputeHash(payload) },
            Artifacts = ImmutableDictionary<string, JsonElement>.Empty.Add("boards/test.json", definitions)
        };
    }

    [Fact]
    public async Task ValidatedReadsReuseOwnedBundleButRejectSameLengthTamperingAndDeletion()
    {
        using var document = JsonDocument.Parse("{\"test\":{\"value\":1}}");
        var bundle = BoardBundle() with
        { Artifacts = ImmutableDictionary<string, JsonElement>.Empty.Add("boards/test.json", document.RootElement) };
        using var publisher = new ContentPublicationService(_directory, new ContentManifestProvider());
        Assert.True((await publisher.PublishBundleAsync(bundle)).IsSuccess);
        document.Dispose();
        var revision = bundle.Manifest.Revision;
        var first = await publisher.GetPublishedAsync(revision);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        var reads = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => publisher.GetPublishedAsync(revision)));
        Assert.All(reads, read => Assert.Same(first.Value, read.Value));
        Assert.True(publisher.GetStats().Hits >= 13);
        Assert.Equal(1, first.Value.Artifacts["boards/test.json"].GetProperty("test").GetProperty("value").GetInt32());
        var path = Path.Combine(_directory, "published", $"{revision}.json");
        var original = await File.ReadAllTextAsync(path);
        var corrupted = original.Replace("\"value\": 1", "\"value\": 2", StringComparison.Ordinal);
        Assert.NotEqual(original, corrupted);
        Assert.Equal(original.Length, corrupted.Length);
        var timestamp = File.GetLastWriteTimeUtc(path);
        await File.WriteAllTextAsync(path, corrupted);
        File.SetLastWriteTimeUtc(path, timestamp);
        var rejected = await publisher.GetPublishedAsync(revision);
        Assert.True(rejected.IsFailure);
        Assert.Contains("Artifact hash mismatch", rejected.Error);
        using (var restarted = new ContentPublicationService(_directory, new ContentManifestProvider()))
            Assert.True((await restarted.GetPublishedAsync(revision)).IsFailure);
        await File.WriteAllTextAsync(path, original);
        Assert.True((await publisher.GetPublishedAsync(revision)).IsSuccess);
        File.Delete(path);
        Assert.True((await publisher.GetPublishedAsync(revision)).IsFailure);
    }

    [Fact]
    public async Task ValidationCacheSupportsEvictionInvalidationAndDisabledMode()
    {
        using var publisher = new ContentPublicationService(_directory, new ContentManifestProvider(), validationCacheCapacity: 1);
        var a = BoardBundle(1);
        var b = BoardBundle(2);
        Assert.True((await publisher.PublishBundleAsync(a)).IsSuccess);
        var original = (await publisher.GetPublishedAsync(a.Manifest.Revision)).Value;
        Assert.True((await publisher.PublishBundleAsync(b)).IsSuccess);
        Assert.True(publisher.GetStats().Evictions > 0);
        var loaded = (await publisher.GetPublishedAsync(a.Manifest.Revision)).Value;
        Assert.NotSame(original, loaded);
        Assert.Equal(CanonicalJson.Serialize(original), CanonicalJson.Serialize(loaded));
        publisher.Invalidate(a.Manifest.Revision);
        Assert.Equal(0, publisher.GetStats().Count);
        Assert.NotSame(loaded, (await publisher.GetPublishedAsync(a.Manifest.Revision)).Value);
        publisher.Invalidate();
        Assert.Equal(0, publisher.GetStats().Count);
        using var disabled = new ContentPublicationService(_directory, new ContentManifestProvider(), validationCacheCapacity: 0);
        var read1 = (await disabled.GetPublishedAsync(a.Manifest.Revision)).Value;
        var read2 = (await disabled.GetPublishedAsync(a.Manifest.Revision)).Value;
        Assert.NotSame(read1, read2);
        Assert.Equal(CanonicalJson.Serialize(read1), CanonicalJson.Serialize(read2));
        Assert.Equal(0, disabled.GetStats().Capacity);
    }

    [Fact]
    public async Task ArbitraryValidatorsAreNotMemoized()
    {
        var graph = new Mock<IContentGraphValidator>();
        graph.Setup(item => item.Validate(It.IsAny<ContentBundle>())).Returns(new ContentGraphValidationResult());
        using var publisher = new ContentPublicationService(_directory, new ContentManifestProvider(), graph.Object);
        var bundle = BoardBundle();
        Assert.True((await publisher.PublishBundleAsync(bundle)).IsSuccess);
        Assert.True((await publisher.GetPublishedAsync(bundle.Manifest.Revision)).IsSuccess);
        graph.Setup(item => item.Validate(It.IsAny<ContentBundle>()))
            .Returns(new ContentGraphValidationResult { Errors = ["External rule changed"] });
        Assert.True((await publisher.GetPublishedAsync(bundle.Manifest.Revision)).IsFailure);
        graph.Verify(item => item.Validate(It.IsAny<ContentBundle>()), Times.Exactly(3));
        Assert.Equal(0, publisher.GetStats().Capacity);
    }

    [Fact]
    public async Task EncodingChangesAreRevalidatedWithoutChangingContentIdentity()
    {
        using var publisher = new ContentPublicationService(_directory, new ContentManifestProvider());
        var bundle = BoardBundle();
        Assert.True((await publisher.PublishBundleAsync(bundle)).IsSuccess);
        var original = (await publisher.GetPublishedAsync(bundle.Manifest.Revision)).Value;
        var path = Path.Combine(_directory, "published", $"{bundle.Manifest.Revision}.json");
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json, System.Text.Encoding.Unicode);
        var revalidated = await publisher.GetPublishedAsync(bundle.Manifest.Revision);
        Assert.True(revalidated.IsSuccess, revalidated.IsFailure ? revalidated.Error : null);
        Assert.NotSame(original, revalidated.Value);
        Assert.Equal(CanonicalJson.Serialize(original), CanonicalJson.Serialize(revalidated.Value));
        Assert.Same(revalidated.Value, (await publisher.GetPublishedAsync(bundle.Manifest.Revision)).Value);

        // Another valid bundle cannot be substituted under an already cached revision.
        var other = BoardBundle(2);
        Assert.True((await publisher.PublishBundleAsync(other)).IsSuccess);
        await File.WriteAllBytesAsync(path, await File.ReadAllBytesAsync(
            Path.Combine(_directory, "published", $"{other.Manifest.Revision}.json")));
        var substituted = await publisher.GetPublishedAsync(bundle.Manifest.Revision);
        Assert.True(substituted.IsFailure);
        Assert.Contains("identity mismatch", substituted.Error);
    }

    [Fact]
    public void CustomRegistriesCannotOptIntoBuiltInValidationReuse()
    {
        using var publisher = new ContentPublicationService(_directory, new ContentManifestProvider(),
            new ContentGraphValidator(new Mock<IContentKindRegistry>().Object));
        Assert.Equal(0, publisher.GetStats().Capacity);
    }

    [Fact]
    public async Task GetPublished_CorruptArtifactBytesFailFastAfterRestart()
    {
        var definitions = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["test"] = JsonSerializer.SerializeToElement(new { value = 1 })
        };
        const string artifactPath = "test-kinds/test.json";
        var artifact = new ContentArtifactManifest
        {
            Kind = "test-kinds",
            Path = artifactPath,
            Hash = CanonicalJson.ComputeHash(definitions),
            DefinitionCount = definitions.Count
        };
        var manifestPayload = new
        {
            SchemaVersion = 1,
            ConfigName = "test",
            ConfigChain = new[] { "test" },
            Artifacts = new[] { artifact }
        };
        var revision = CanonicalJson.ComputeHash(manifestPayload);
        var bundle = new ContentBundle
        {
            Manifest = new ContentManifest
            {
                SchemaVersion = 1,
                ConfigName = "test",
                ConfigChain = ["test"],
                Artifacts = [artifact],
                Revision = revision
            },
            Artifacts = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [artifactPath] = JsonSerializer.SerializeToElement(definitions)
            }.ToImmutableDictionary(StringComparer.Ordinal)
        };
        var graph = new Mock<IContentGraphValidator>();
        graph.Setup(item => item.Validate(It.IsAny<ContentBundle>()))
            .Returns(new ContentGraphValidationResult());
        using (var publisher = new ContentPublicationService(
                   _directory,
                   new ContentManifestProvider(),
                   graph.Object))
        {
            var published = await publisher.PublishBundleAsync(bundle);
            Assert.True(published.IsSuccess, published.IsFailure ? published.Error : null);
        }

        var publishedPath = Path.Combine(_directory, "published", $"{revision}.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(publishedPath))!;
        document["Artifacts"]![artifactPath]!["test"]!["value"] = 2;
        await File.WriteAllTextAsync(publishedPath, document.ToJsonString());

        using var restarted = new ContentPublicationService(
            _directory,
            new ContentManifestProvider(),
            graph.Object);
        var loaded = await restarted.GetPublishedAsync(revision);

        Assert.True(loaded.IsFailure);
        Assert.Contains("corrupt", loaded.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Artifact hash mismatch", loaded.Error, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
