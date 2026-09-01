using System.Collections.Immutable;
using Core.Caching;
using Core.Common;
using Core.Content;
using Moq;
using Xunit;

namespace Core.Tests.Content;

public sealed class ContentReloadServiceTests
{
    [Fact]
    public async Task ReloadAsync_InvalidatesMutableCaches_Publishes_AndPrewarmsRevision()
    {
        var revision = new string('a', 64);
        var bundle = Bundle(revision);
        var draft = new ContentDraft
        {
            DraftId = Guid.NewGuid(),
            Version = 1,
            ConfigName = "default",
            Bundle = bundle
        };
        var caches = new Mock<ICacheCoordinator>();
        caches.Setup(service => service.InvalidateAll(null, false))
            .Returns(new CacheInvalidationReport(
                [new CacheInvalidationEntry("raw", CacheLayer.Source, true)]));
        var publications = new Mock<IContentPublicationService>();
        publications.Setup(service => service.CreateDraftAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ContentDraft>.Success(draft));
        publications.Setup(service => service.Validate(bundle))
            .Returns(new ContentValidationResult { Manifest = bundle.Manifest });
        publications.Setup(service => service.PublishDraftAsync(draft.DraftId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ContentBundle>.Success(bundle));
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve(revision, "default"))
            .Returns(Result<ContentRuntime>.Success(ContentRuntime.Create(bundle).Value));
        using var service = new ContentReloadService(caches.Object, publications.Object, runtimes.Object);

        var result = await service.ReloadAsync("default");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(revision, result.Value.Revision);
        Assert.Equal(1, result.Value.CacheInvalidation.InvalidatedCount);
        caches.Verify(cache => cache.InvalidateAll(null, false), Times.Once);
        runtimes.Verify(runtime => runtime.Resolve(revision, "default"), Times.Once);
    }

    [Fact]
    public async Task ReloadAsync_DoesNotPublishAnInvalidCandidate()
    {
        var bundle = Bundle(new string('b', 64));
        var draft = new ContentDraft
        {
            DraftId = Guid.NewGuid(),
            Version = 1,
            ConfigName = "default",
            Bundle = bundle
        };
        var caches = new Mock<ICacheCoordinator>();
        caches.Setup(service => service.InvalidateAll(null, false))
            .Returns(new CacheInvalidationReport([]));
        var publications = new Mock<IContentPublicationService>();
        publications.Setup(service => service.CreateDraftAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ContentDraft>.Success(draft));
        publications.Setup(service => service.Validate(bundle))
            .Returns(new ContentValidationResult { Errors = ["invalid"] });
        using var service = new ContentReloadService(
            caches.Object,
            publications.Object,
            Mock.Of<IContentRuntimeResolver>());

        var result = await service.ReloadAsync("default");

        Assert.True(result.IsFailure);
        publications.Verify(service => service.PublishDraftAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ContentBundle Bundle(string revision)
    {
        return new ContentBundle
        {
            Manifest = new ContentManifest
            {
                ConfigName = "default",
                ConfigChain = ImmutableArray.Create("default"),
                Revision = revision
            }
        };
    }
}
