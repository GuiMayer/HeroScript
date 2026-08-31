using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat;
using Core.Combat.Models;
using Core.Config;
using Core.Content;
using Core.Effects;
using Core.Logging;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Content;

public sealed class ContentRuntimeTests
{
    [Fact]
    public void Resolve_CachesOneImmutableRuntimePerRevision()
    {
        var revision = new string('a', 64);
        var publication = new Mock<IContentPublicationService>();
        publication
            .Setup(service => service.ResolveBundleAsync(
                revision,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Core.Common.Result<ContentBundle>.Success(CreateBundle(revision, 10f)));
        var resolver = new ContentRuntimeResolver(publication.Object);

        var first = resolver.Resolve(revision);
        var second = resolver.Resolve(revision);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Same(first.Value, second.Value);
        publication.Verify(service => service.ResolveBundleAsync(
            revision,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RevisionedActionLookup_DoesNotReadTheLiveActionCache()
    {
        var revision = new string('b', 64);
        var publication = new Mock<IContentPublicationService>();
        publication
            .Setup(service => service.ResolveBundleAsync(
                revision,
                "default",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Core.Common.Result<ContentBundle>.Success(CreateBundle(revision, 10f)));
        var resolver = new ContentRuntimeResolver(publication.Object);
        var manager = new ActionManager(
            Mock.Of<IConfigManager>(),
            Mock.Of<IResourceLoader>(),
            Mock.Of<ILogger>(),
            contentRuntimes: resolver);

        var result = manager.GetDefinition("strike", revision, "default");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(10f, result.Value.Effects.Single().FlatValue);
    }

    [Fact]
    public void StandaloneArtifacts_UseTheirFileNamesAsDefinitionIds()
    {
        var revision = new string('c', 64);
        var healthPath = "resources/health.json";
        var energyPath = "resources/energy.json";
        var manifest = new ContentManifest
        {
            ConfigName = "default",
            Revision = revision,
            Artifacts =
            [
                new ContentArtifactManifest { Kind = "resources", Path = healthPath },
                new ContentArtifactManifest { Kind = "resources", Path = energyPath }
            ]
        };
        var bundle = new ContentBundle
        {
            Manifest = manifest,
            Artifacts = ImmutableDictionary<string, JsonElement>.Empty
                .WithComparers(StringComparer.Ordinal)
                .Add(healthPath, JsonSerializer.SerializeToElement(new Dictionary<string, ResourceDefinition>
                {
                    ["definition"] = new() { ResourceId = "health" }
                }))
                .Add(energyPath, JsonSerializer.SerializeToElement(new Dictionary<string, ResourceDefinition>
                {
                    ["definition"] = new() { ResourceId = "energy" }
                }))
        };

        var runtime = ContentRuntime.Create(bundle);

        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        Assert.Equal("health", runtime.Value.GetDefinition<ResourceDefinition>("resources", "health").Value.ResourceId);
        Assert.Equal("energy", runtime.Value.GetDefinition<ResourceDefinition>("resources", "energy").Value.ResourceId);
    }

    private static ContentBundle CreateBundle(string revision, float damage)
    {
        const string path = "actions/strike.json";
        var action = new ActionDefinition
        {
            ActionId = "strike",
            DisplayName = "Strike",
            ActionType = ActionType.BASIC_ATTACK,
            Effects =
            [
                new EffectDefinition
                {
                    EffectId = "strike.damage",
                    Type = EffectType.DAMAGE,
                    FlatValue = damage
                }
            ]
        };
        var document = JsonSerializer.SerializeToElement(
            new Dictionary<string, ActionDefinition> { [action.ActionId] = action });

        return new ContentBundle
        {
            Manifest = new ContentManifest
            {
                ConfigName = "default",
                Revision = revision,
                Artifacts =
                [
                    new ContentArtifactManifest
                    {
                        Kind = "actions",
                        Path = path,
                        Hash = "test",
                        DefinitionCount = 1
                    }
                ]
            },
            Artifacts = ImmutableDictionary<string, JsonElement>.Empty
                .WithComparers(StringComparer.Ordinal)
                .Add(path, document)
        };
    }
}
