using Core.Common;

namespace Core.Content;

public interface IContentManifestProvider
{
    Result<ContentManifest> GetManifest(string configName);
    Result<ContentManifest> BuildCandidate(string configName);
    Result<ContentManifest> RefreshManifest(string configName);
    Result<ContentManifest> GetByRevision(string revision);
    IReadOnlyList<ContentManifest> GetKnownManifests();
    Result RegisterPublishedManifest(ContentManifest manifest);
    Result ActivatePublishedManifest(ContentManifest manifest);
}
