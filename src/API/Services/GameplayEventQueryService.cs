using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Content;
using Core.GameplayEvents;
using Core.Run;

namespace API.Services;

public sealed record GameplayInspectionResult(int Status, object? Value = null, string? Error = null);

/// <summary>Read-only application boundary. Authorizes before reading persisted causal details.</summary>
public sealed class GameplayEventQueryService(IContentRuntimeResolver runtimes, IRunQueryService runs,
    IRunCommitReader commits, IToolAccessPolicy access)
{
    private static readonly Lazy<JsonElement> Schema = new(() =>
    {
        using var stream = typeof(GameplayEventQueryService).Assembly.GetManifestResourceStream("GameplayResponseSchema")
            ?? throw new InvalidOperationException("Embedded gameplay response schema is missing");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    });

    public GameplayInspectionResult Catalog(string settingId, string revision, bool schema = false)
    {
        var runtime = runtimes.Resolve(revision, settingId);
        if (runtime.IsFailure) return new(404, Error: runtime.Error);
        if (runtime.Value.Manifest.ConfigName != settingId || runtime.Value.Manifest.Revision != revision)
            return new(409, Error: "Content scope does not match requested setting and revision");
        var catalog = GameplayCoreCatalog.Create(settingId, revision);
        if (catalog.IsFailure) return new(400, Error: catalog.Error);
        return schema ? new(200, new { settingId, contentRevision = revision, schema = Schema.Value })
            : new(200, new { settingId, contentRevision = revision,
                kinds = catalog.Value.Kinds.Values.OrderBy(item => item.Kind, StringComparer.Ordinal),
                reasons = catalog.Value.Reasons.Values.OrderBy(item => item.ReasonId, StringComparer.Ordinal),
                hierarchy = new { separator = ".", singleSegment = "*", descendants = "**", exclusionsWin = true } });
    }

    public async Task<GameplayInspectionResult> ReadAsync(Guid runId, int sequence, string? contentRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (sequence < 1) return new(400, Error: "Sequence must be positive");
        var loaded = runs.GetRun(runId);
        if (loaded.IsFailure) return new(404, Error: loaded.Error);
        var run = loaded.Value;
        // Full facts may identify cards in hidden zones. Ordinary timeline visibility is insufficient.
        if (!access.Allows(run, ToolCapabilities.TimelineInspectState) || !access.Allows(run, ToolCapabilities.CardInspectFull))
            return new(403, Error: "Causal inspection requires historical and full card inspection capabilities");
        var commit = commits is IRunCommitEnvelopeReader envelopes
            ? (await envelopes.LoadEnvelopePageAsync(runId, sequence - 1, 1, sequence, cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault()
            : await commits.LoadCommitAsync(runId, sequence, cancellationToken).ConfigureAwait(false);
        if (commit == null) return new(404, Error: "Command commit not found");
        if (commit.RunId != runId || commit.Sequence != sequence || commit.GameplayEvents is { } scoped &&
            (scoped.Scope.SettingId != run.SettingId || scoped.Scope.RunId != (run.Lineage?.RootRunId ?? runId).ToString("D")))
            return new(409, Error: "Causal scope does not match requested aggregate");
        var batch = commit.GameplayEvents;
        if (contentRevision != null && batch?.Scope.ContentRevision != contentRevision)
            return new(409, Error: "Causal revision does not match requested revision");
        return new(200, new { runId, sequence, commandId = commit.RootCommand.CommandId,
            recorded = batch != null, batch });
    }
}
