using Core.Common;

namespace Core.Run.Content;

public sealed record ContentArtifactChange(
    string Path,
    string Kind,
    string Change,
    string? PreviousHash,
    string? TargetHash);

public sealed record ContentRevisionActivationPreview(
    Guid RunId,
    string CurrentRevision,
    string TargetRevision,
    string ActivationBoundary,
    bool Compatible,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ContentArtifactChange> ArtifactChanges);

/// <summary>
/// Read-only planning boundary for content activation. Tooling can inspect the
/// exact revision delta and compatibility outcome before issuing a command.
/// </summary>
public interface IContentRevisionActivationPreviewService
{
    Result<ContentRevisionActivationPreview> Preview(Guid runId, string targetRevision);
}
