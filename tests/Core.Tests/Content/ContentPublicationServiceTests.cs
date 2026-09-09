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
