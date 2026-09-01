using System.Text.Json;
using Core.Config;
using Core.Content;
using Moq;
using Xunit;

namespace Core.Tests.Content;

[Trait("Category", "Unit")]
public sealed class ContentManifestProviderTests
{
    [Fact]
    public void ContentManifest_CopiesCallerOwnedCollections()
    {
        var chain = new List<string> { "base", "test" };
        var artifacts = new List<ContentArtifactManifest>
        {
            new() { Kind = "actions", Path = "actions/combat.json", Hash = "hash", DefinitionCount = 1 }
        };
        var manifest = new ContentManifest
        {
            ConfigName = "test",
            ConfigChain = chain,
            Artifacts = artifacts,
            Revision = "revision"
        };

        chain.Add("mutated");
        artifacts.Clear();

        Assert.Equal(new[] { "base", "test" }, manifest.ConfigChain);
        Assert.Single(manifest.Artifacts);
    }

    [Fact]
    public void GetManifest_IsIndependentOfDefinitionInsertionOrder()
    {
        var first = CreateProvider(new Dictionary<string, JsonElement>
        {
            ["strike"] = Element("""{ "damage": 6, "cost": 1 }"""),
            ["defend"] = Element("""{ "block": 5, "cost": 1 }""")
        });
        var second = CreateProvider(new Dictionary<string, JsonElement>
        {
            ["defend"] = Element("""{ "cost": 1, "block": 5 }"""),
            ["strike"] = Element("""{ "cost": 1, "damage": 6 }""")
        });

        var firstManifest = first.GetManifest("test");
        var secondManifest = second.GetManifest("test");

        Assert.True(firstManifest.IsSuccess, firstManifest.IsFailure ? firstManifest.Error : null);
        Assert.True(secondManifest.IsSuccess, secondManifest.IsFailure ? secondManifest.Error : null);
        Assert.Equal(firstManifest.Value.Revision, secondManifest.Value.Revision);
        var artifact = Assert.Single(firstManifest.Value.Artifacts);
        Assert.Equal("actions", artifact.Kind);
        Assert.Equal("actions/combat.json", artifact.Path);
        Assert.Equal(2, artifact.DefinitionCount);
    }

    [Fact]
    public void RefreshManifest_WhenContentChanges_KeepsBothImmutableRevisionsAddressable()
    {
        var config = new Mock<IConfigManager>();
        var loader = new Mock<IResourceLoader>();
        var definitions = new Dictionary<string, JsonElement>
        {
            ["strike"] = Element("""{ "damage": 6 }""")
        };
        config.Setup(manager => manager.ResolveInheritanceChain("test"))
            .Returns(new[] { "base", "test" });
        loader.Setup(resourceLoader => resourceLoader.DiscoverResources(
                "actions",
                It.IsAny<IEnumerable<string>>(),
                "*.json"))
            .Returns(new[] { "combat" });
        loader.Setup(resourceLoader => resourceLoader.LoadResource(
                "actions/combat.json",
                It.IsAny<IEnumerable<string>>(),
                false))
            .Returns(() => definitions);
        var provider = new ContentManifestProvider(config.Object, loader.Object);

        var first = provider.GetManifest("test").Value;
        definitions = new Dictionary<string, JsonElement>
        {
            ["strike"] = Element("""{ "damage": 7 }""")
        };
        var second = provider.RefreshManifest("test").Value;

        Assert.NotEqual(first.Revision, second.Revision);
        Assert.Equal(first, provider.GetByRevision(first.Revision).Value);
        Assert.Equal(second, provider.GetByRevision(second.Revision).Value);
        Assert.Equal(2, provider.GetKnownManifests().Count);
    }

    [Fact]
    public void BuildCandidate_DoesNotReplaceCurrent_UntilPublishedManifestIsActivated()
    {
        var config = new Mock<IConfigManager>();
        var loader = new Mock<IResourceLoader>();
        var definitions = new Dictionary<string, JsonElement>
        {
            ["strike"] = Element("""{ "damage": 6 }""")
        };
        config.Setup(manager => manager.ResolveInheritanceChain("test"))
            .Returns(new[] { "test" });
        loader.Setup(resourceLoader => resourceLoader.DiscoverResources(
                "actions", It.IsAny<IEnumerable<string>>(), "*.json"))
            .Returns(new[] { "combat" });
        loader.Setup(resourceLoader => resourceLoader.LoadResource(
                "actions/combat.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(() => definitions);
        var provider = new ContentManifestProvider(config.Object, loader.Object);
        var current = provider.GetManifest("test").Value;
        definitions = new Dictionary<string, JsonElement>
        {
            ["strike"] = Element("""{ "damage": 9 }""")
        };

        var candidate = provider.BuildCandidate("test").Value;

        Assert.NotEqual(current.Revision, candidate.Revision);
        Assert.Equal(current.Revision, provider.GetManifest("test").Value.Revision);
        Assert.True(provider.GetByRevision(candidate.Revision).IsFailure);

        Assert.True(provider.ActivatePublishedManifest(candidate).IsSuccess);
        Assert.Equal(candidate.Revision, provider.GetManifest("test").Value.Revision);
    }

    private static ContentManifestProvider CreateProvider(Dictionary<string, JsonElement> definitions)
    {
        var config = new Mock<IConfigManager>();
        var loader = new Mock<IResourceLoader>();
        config.Setup(manager => manager.ResolveInheritanceChain("test"))
            .Returns(new[] { "base", "test" });
        loader.Setup(resourceLoader => resourceLoader.DiscoverResources(
                "actions",
                It.IsAny<IEnumerable<string>>(),
                "*.json"))
            .Returns(new[] { "combat" });
        loader.Setup(resourceLoader => resourceLoader.LoadResource(
                "actions/combat.json",
                It.IsAny<IEnumerable<string>>(),
                false))
            .Returns(definitions);
        return new ContentManifestProvider(config.Object, loader.Object);
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
