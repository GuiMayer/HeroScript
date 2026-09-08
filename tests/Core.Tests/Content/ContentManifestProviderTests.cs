using Core.Content;
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
            Revision = new string('a', 64)
        };

        chain.Add("mutated");
        artifacts.Clear();

        Assert.Equal(new[] { "base", "test" }, manifest.ConfigChain);
        Assert.Single(manifest.Artifacts);
    }

    [Fact]
    public void GetManifest_OnlyReturnsExplicitlyActivatedPublishedContent()
    {
        var provider = new ContentManifestProvider();
        var manifest = Manifest('a');

        Assert.True(provider.GetManifest("test").IsFailure);
        Assert.True(provider.RegisterPublishedManifest(manifest).IsSuccess);
        Assert.True(provider.GetManifest("test").IsFailure);

        Assert.True(provider.ActivatePublishedManifest(manifest).IsSuccess);
        Assert.Equal(manifest, provider.GetManifest("test").Value);
    }

    [Fact]
    public void RegisterHistoricalRevision_DoesNotReplaceActiveRevision()
    {
        var provider = new ContentManifestProvider();
        var active = Manifest('a');
        var historical = Manifest('b');

        Assert.True(provider.ActivatePublishedManifest(active).IsSuccess);
        Assert.True(provider.RegisterPublishedManifest(historical).IsSuccess);

        Assert.Equal(active, provider.GetManifest("test").Value);
        Assert.Equal(active, provider.GetByRevision(active.Revision).Value);
        Assert.Equal(historical, provider.GetByRevision(historical.Revision).Value);
        Assert.Equal(2, provider.GetKnownManifests().Count);
    }

    [Fact]
    public void RegisterPublishedManifest_RejectsNonCanonicalRevision()
    {
        var provider = new ContentManifestProvider();

        var result = provider.RegisterPublishedManifest(Manifest('A'));

        Assert.True(result.IsFailure);
        Assert.Contains("lowercase SHA-256", result.Error, StringComparison.Ordinal);
    }

    private static ContentManifest Manifest(char revisionCharacter) => new()
    {
        ConfigName = "test",
        ConfigChain = ["heroscript.base@1.0.0"],
        Artifacts =
        [
            new ContentArtifactManifest
            {
                Kind = "actions",
                Path = "actions/combat.json",
                Hash = new string('f', 64),
                DefinitionCount = 1
            }
        ],
        Revision = new string(revisionCharacter, 64)
    };
}
