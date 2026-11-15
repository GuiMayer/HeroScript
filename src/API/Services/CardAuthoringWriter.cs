using System.Text.Json;
using API.Contracts;
using Core.Caching;
using Core.Content;
using Core.Determinism;
using Mods;

namespace API.Services;

public sealed class AuthoringWriteCoordinator : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string[] _packageRoots;
    private readonly string? _root;
    private FileStream? _lease;
    private volatile bool _recoveryRequired;

    public AuthoringWriteCoordinator(IReadOnlyList<string> packageRoots, IConfiguration configuration)
    {
        _packageRoots = packageRoots.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToArray();
        Enabled = configuration.GetValue<bool>("Authoring:ApplyEnabled");
        if (!Enabled)
            return;
        if (_packageRoots.Length != 1 ||
            string.Equals(_packageRoots[0], Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Resources")),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Authoring writes require one explicit workspace package root.");
        _root = _packageRoots[0];
    }

    public bool Enabled { get; }
    internal bool RecoveryRequired => _recoveryRequired;
    internal string JournalRoot => PathFor(".heroscript-authoring");

    internal void Open()
    {
        if (_root is null)
        {
            if (_packageRoots.Any(root => File.Exists(Path.Combine(root, ".heroscript-authoring", "pending.json"))))
                throw new IOException("This workspace has a pending authoring write. Restart with authoring enabled to recover it.");
            return;
        }
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException(_root);
        Directory.CreateDirectory(JournalRoot);
        var lockPath = PathFor(".heroscript-authoring/workspace.lock");
        _lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        if (!Enabled)
            return await operation();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal void RequireRecovery() => _recoveryRequired = true;
    internal void Close()
    {
        _lease?.Dispose();
        _lease = null;
    }

    internal string PathFor(string relative)
    {
        var root = _root ?? throw new InvalidOperationException("Authoring writes are disabled.");
        // Manifest paths become filesystem paths only within the configured root.
        var segments = relative.Split('/');
        if (segments.Any(part => part is "" or "." or ".." || part.Contains(':') || part.Contains('\\') ||
                                 part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new IOException($"Invalid workspace path: {relative}");
        var path = root;
        CheckPath(path);
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            CheckPath(path);
        }
        return path;
    }

    private static void CheckPath(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Authoring cannot use a symbolic link or reparse point: {path}");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    public void Dispose()
    {
        Close();
        _gate.Dispose();
    }
}

public sealed class CardAuthoringWriter(
    AuthoringWriteCoordinator coordinator,
    CardAuthoringService authoring,
    IContentPublicationService publication,
    IContentRuntimeResolver runtimes,
    IContentManifestProvider manifests,
    ICacheCoordinator caches,
    IEnumerable<Action<AuthoringWriteCheckpoint>> checkpoints,
    ILogger<CardAuthoringWriter> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RecoverAsync()
    {
        coordinator.Open();
        if (!coordinator.Enabled)
            return;
        try
        {
            await RecoverPendingAsync();
        }
        catch
        {
            coordinator.Close();
            throw;
        }
    }

    private async Task RecoverPendingAsync()
    {
        // Applied and aborted receipts need no replay. Discover only the single
        // possible in-flight transaction by its well-known independent journal.
        var record = await ReadRecord("pending.json");
        if (record is null)
            return;
        if (record.Receipt.State == "sourcesConfirmed")
        {
            var current = await authoring.CaptureAsync(CancellationToken.None);
            if (current.IsFailure || current.Value.ETag != record.Receipt.SourceVersion)
                throw new IOException("Confirmed authoring sources differ from the recovery journal.");
            Activate(record);
            await CompleteAsync(record, "applied");
        }
        else if (record.Receipt.State == "prepared")
        {
            // Validate the whole observed state before changing any file.
            foreach (var file in record.Files)
            {
                var current = await ReadSource(file.Path);
                if (!Same(current, file.Before) && !Same(current, file.After))
                    throw new IOException($"Pending authoring change conflicts with an external edit of '{file.Path}'.");
            }
            foreach (var file in record.Files.Reverse())
            {
                var current = await ReadSource(file.Path);
                if (!Same(current, file.Before))
                    await ReplaceAsync(file.Path, file.After, file.Before);
            }
            await CompleteAsync(record, "aborted");
        }
        else
        {
            throw new IOException("The authoring journal contains an unknown recovery state.");
        }
    }

    public Task<AuthoringApplyResult> GetAsync(string settingId, Guid changeId, CancellationToken cancellationToken) =>
        coordinator.ExecuteAsync(async () =>
        {
            if (!coordinator.Enabled)
                return new AuthoringApplyResult(404, ApiErrorCodes.ResourceNotFound, "Authoring changes are disabled.");
            var record = await ReadRecord(ReceiptPath(changeId));
            if (record is null && coordinator.RecoveryRequired)
                record = await ReadRecord("pending.json");
            return record?.SettingId == settingId && record.Receipt.ChangeId == changeId
                ? new(200, Receipt: Availability(PublicReceipt(record.Receipt), await ReadSuccessor(changeId)))
                : new(404, ApiErrorCodes.ResourceNotFound, "Authoring change not found.");
        }, cancellationToken);

    public Task<AuthoringApplyResult> ApplyAsync(string settingId, string etag, CardAuthoringChangeRequest request,
        CancellationToken cancellationToken) =>
        coordinator.ExecuteAsync(() => ApplyCoreAsync(settingId, etag, request, cancellationToken), cancellationToken);

    private async Task<AuthoringApplyResult> ApplyCoreAsync(string settingId, string etag,
        CardAuthoringChangeRequest request, CancellationToken cancellationToken)
    {
        if (!coordinator.Enabled)
            return new(403, ApiErrorCodes.Forbidden, "Enable Authoring:ApplyEnabled to apply card changes.");
        if (coordinator.RecoveryRequired)
            return Recoverable("An interrupted authoring change requires a restart.");
        var fingerprint = CanonicalJson.ComputeHash(new { settingId, etag, request });
        var existing = await ReadRecord(ReceiptPath(request.ChangeId));
        if (existing is not null)
        {
            if (existing.Fingerprint != fingerprint)
                return new(409, AuthoringErrorCodes.ChangeConflict, "This change ID has a different request.");
            return existing.Receipt.State == "applied"
                ? new(200, Receipt: existing.Receipt)
                : new(409, AuthoringErrorCodes.ChangeConflict, "This change was aborted. Submit a new change ID.", existing.Receipt);
        }
        var workspace = await authoring.CaptureAsync(cancellationToken);
        if (workspace.IsFailure)
            return new(503, ApiErrorCodes.DependencyUnavailable, workspace.Error);
        if (workspace.Value.ETag != etag)
            return SourceChanged();
        if (!workspace.Value.Catalog.Settings.Any(item => item.Definition.SettingId == settingId))
            return new(404, ApiErrorCodes.ResourceNotFound, "Setting not found.");
        var view = await authoring.GetViewAsync(workspace.Value, settingId, cancellationToken);
        if (view.IsFailure)
            return new(422, AuthoringErrorCodes.InvalidCandidate, view.Error);
        var planned = await PlanAsync(workspace.Value, view.Value, request, cancellationToken);
        if (planned.Error is not null)
            return planned.Error;
        var plan = planned.Plan!;
        if (!plan.Validation.IsValid)
            return new(422, AuthoringErrorCodes.InvalidCandidate, "The candidate failed authoring validation.", Validation: plan.Validation);

        foreach (var bundle in plan.Bundles)
        {
            var prepared = await publication.PrepareBundleAsync(bundle, cancellationToken);
            if (prepared.IsFailure)
                return new(503, ApiErrorCodes.DependencyUnavailable, prepared.Error);
            var runtime = runtimes.Resolve(bundle.Manifest.Revision, bundle.Manifest.ConfigName);
            if (runtime.IsFailure)
                return new(503, ApiErrorCodes.DependencyUnavailable, runtime.Error);
        }

        // Recheck the source snapshot after preparing runtimes and before
        // creating a durable transaction. A new, undeclared file is checked too.
        var latest = await authoring.CaptureAsync(cancellationToken);
        if (latest.IsFailure || latest.Value.ETag != etag)
            return SourceChanged();
        foreach (var file in plan.Files)
            if (!Same(await ReadSource(file.Path), file.Before))
                return SourceChanged();
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = workspace.Value.Snapshots.Single();
        var candidate = snapshot.Sources.ToDictionary(file => file.Key, file => file.Value, StringComparer.Ordinal);
        foreach (var file in plan.Files)
            if (file.After is { } bytes)
                candidate[file.Path] = bytes;
            else
                candidate.Remove(file.Path);
        var nextVersion = AuthoringSourceVersion.CreateETag(candidate.ToDictionary(
            file => $"{snapshot.ProviderId}/{file.Key}", file => file.Value, StringComparer.Ordinal));
        var receipt = new CardAuthoringChangeReceipt(request.ChangeId, "prepared", etag, nextVersion,
            plan.Validation.CandidateRevisions, plan.Files.Select(file => file.Path).ToArray(),
            plan.Validation.Diagnostics, request.UndoOf, request.RedoOf);
        var package = view.Value.Writable!;
        var record = new AuthoringJournalRecord(settingId, fingerprint, receipt, plan.Files,
            new(package.SourceId, package.Manifest.PackageId, package.Manifest.Version));

        // After preparing the journal the transaction must finish or be
        // recoverable independently of the HTTP connection.
        try
        {
            await WriteRecord("pending.json", record);
            Notify(AuthoringWriteCheckpoint.Prepared);
            foreach (var file in plan.Files)
            {
                await ReplaceAsync(file.Path, file.Before, file.After);
                Notify(AuthoringWriteCheckpoint.FileReplaced);
            }
            var written = await authoring.CaptureAsync(CancellationToken.None);
            if (written.IsFailure || written.Value.ETag != nextVersion)
                throw new IOException("The final source snapshot differs from the prepared candidate.");
            record = record with { Receipt = record.Receipt with { State = "sourcesConfirmed" } };
            await WriteRecord("pending.json", record);
            Notify(AuthoringWriteCheckpoint.SourcesConfirmed);
            Activate(record);
            await CompleteAsync(record, "applied");
            return new(200, Receipt: Availability(record.Receipt with { State = "applied" }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            coordinator.RequireRecovery();
            logger.LogError(exception, "Authoring change {ChangeId} requires recovery", request.ChangeId);
            return Recoverable("The write was interrupted. Restart the engine to recover the workspace.");
        }
    }

    public async Task<AuthoringApplyResult> PreviewAsync(CardAuthoringWorkspace workspace, CardAuthoringView view,
        CardAuthoringChangeRequest request, CancellationToken cancellationToken)
    {
        var planned = await PlanAsync(workspace, view, request, cancellationToken);
        if (planned.Error is not null)
            return planned.Error;
        var validation = planned.Plan!.Validation;
        return validation.IsValid
            ? new(200, Validation: validation)
            : new(422, AuthoringErrorCodes.InvalidCandidate, "The candidate failed authoring validation.", Validation: validation);
    }

    private async Task<(CardAuthoringPlan? Plan, AuthoringApplyResult? Error)> PlanAsync(CardAuthoringWorkspace workspace,
        CardAuthoringView view, CardAuthoringChangeRequest request, CancellationToken cancellationToken)
    {
        var targetId = request.UndoOf ?? request.RedoOf;
        if (targetId is null)
            return (await authoring.PlanAsync(workspace, view, request, cancellationToken), null);
        if (!coordinator.Enabled)
            return (null, new(403, ApiErrorCodes.Forbidden, "Enable source authoring to access compensation history."));

        var target = await ReadRecord(ReceiptPath(targetId.Value));
        if (target is null || target.SettingId != view.Compilation.Setting.SettingId)
            return (null, new(404, ApiErrorCodes.ResourceNotFound, "Compensation target not found in this setting."));

        if (target.Package is { } identity && (view.Writable is not { } writable ||
            identity != new AuthoringPackageIdentity(writable.SourceId, writable.Manifest.PackageId, writable.Manifest.Version)))
            return (null, new(409, AuthoringErrorCodes.ChangeConflict, "The target operation belongs to a different writable package."));

        var successor = await ReadSuccessor(targetId.Value);
        var availability = Availability(target.Receipt, successor);
        if (request.UndoOf.HasValue ? !availability.CanUndo : !availability.CanRedo)
            return (null, new(409, AuthoringErrorCodes.ChangeConflict,
                successor.HasValue ? "This change already has a compensating operation." :
                "The target change is not eligible for this compensation.", availability));

        var plan = await authoring.PlanCompensationAsync(workspace, view, request.ChangeId, target.Files, cancellationToken);
        if (plan.IsFailure)
            return (null, new(409, AuthoringErrorCodes.ChangeConflict, plan.Error, availability));
        foreach (var file in plan.Value.Files)
            if (!Same(await ReadSource(file.Path), file.Before))
                return (null, SourceChanged());
        return (plan.Value, null);
    }

    private static CardAuthoringChangeReceipt Availability(CardAuthoringChangeReceipt receipt, Guid? successor = null)
    {
        var available = receipt.State == "applied" && successor is null;
        return receipt with
        {
            CompensatedBy = successor,
            CanUndo = available && !receipt.UndoOf.HasValue,
            CanRedo = available && receipt.UndoOf.HasValue
        };
    }

    private void Activate(AuthoringJournalRecord record)
    {
        var prepared = new List<ContentManifest>();
        foreach (var (setting, revision) in record.Receipt.ActiveRevisions)
        {
            var runtime = runtimes.Resolve(revision, setting);
            if (runtime.IsFailure)
                throw new IOException(runtime.Error);
            prepared.Add(runtime.Value.Manifest);
        }
        var result = manifests.ActivatePublishedManifests(prepared);
        if (result.IsFailure)
            throw new IOException(result.Error);
        caches.InvalidateAll(includeRevisioned: false);
    }

    private async Task CompleteAsync(AuthoringJournalRecord record, string state)
    {
        var receipt = Availability(record.Receipt with { State = state });
        if (state == "aborted")
            receipt = receipt with { SourceVersion = receipt.PreviousSourceVersion, ActiveRevisions = new Dictionary<string, string>() };
        await WriteRecord(ReceiptPath(receipt.ChangeId), record with { Receipt = receipt });
        if (state == "applied" && (receipt.UndoOf ?? receipt.RedoOf) is { } target)
        {
            var successor = await ReadSuccessor(target);
            if (successor is { } existing && existing != receipt.ChangeId)
                throw new IOException($"Compensation history for '{target}' points to another operation.");
            await WriteAtomic(coordinator.PathFor($".heroscript-authoring/{SuccessorPath(target)}"),
                JsonSerializer.SerializeToUtf8Bytes(receipt.ChangeId, Json));
        }
        File.Delete(coordinator.PathFor(".heroscript-authoring/pending.json"));
    }

    private async Task<Guid?> ReadSuccessor(Guid id)
    {
        var path = coordinator.PathFor($".heroscript-authoring/{SuccessorPath(id)}");
        return File.Exists(path) ? JsonSerializer.Deserialize<Guid>(await File.ReadAllBytesAsync(path), Json) : null;
    }

    private static CardAuthoringChangeReceipt PublicReceipt(CardAuthoringChangeReceipt receipt) =>
        receipt.State is "prepared" or "sourcesConfirmed"
            ? receipt with { State = "recovering", ActiveRevisions = new Dictionary<string, string>() }
            : receipt;

    private async Task<AuthoringJournalRecord?> ReadRecord(string name)
    {
        var path = coordinator.PathFor($".heroscript-authoring/{name}");
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<AuthoringJournalRecord>(await File.ReadAllBytesAsync(path), Json)
            ?? throw new IOException("The authoring journal is empty.");
    }

    private Task WriteRecord(string name, AuthoringJournalRecord record) =>
        WriteAtomic(coordinator.PathFor($".heroscript-authoring/{name}"), JsonSerializer.SerializeToUtf8Bytes(record, Json));

    private async Task ReplaceAsync(string relative, byte[]? expected, byte[]? replacement)
    {
        var path = coordinator.PathFor(relative);
        await Verify(relative, expected);
        if (replacement is null)
            File.Delete(path);
        else
            await WriteAtomic(path, replacement, overwrite: expected is not null, beforeMove: () => Verify(relative, expected));
    }

    private async Task Verify(string relative, byte[]? expected)
    {
        if (!Same(await ReadSource(relative), expected))
            throw new IOException($"Source '{relative}' differs from the expected transaction image.");
    }

    private async Task<byte[]?> ReadSource(string relative)
    {
        if (relative.Split('/').Any(part => part.Equals(".heroscript-authoring", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".git", StringComparison.OrdinalIgnoreCase) || part.EndsWith('.') || part.EndsWith(' ')))
            throw new IOException($"Invalid source path: {relative}");
        var path = coordinator.PathFor(relative);
        if (Directory.Exists(path))
            throw new IOException($"Expected a source file: {relative}");
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    private static bool Same(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static async Task WriteAtomic(string path, byte[] bytes, bool overwrite = true, Func<Task>? beforeMove = null)
    {
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        var staged = Path.Combine(folder, $".heroscript-authoring-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (beforeMove is not null)
                await beforeMove();
            File.Move(staged, path, overwrite);
        }
        finally
        {
            if (File.Exists(staged))
                File.Delete(staged);
        }
    }

    private void Notify(AuthoringWriteCheckpoint checkpoint)
    {
        foreach (var observer in checkpoints)
            observer(checkpoint);
    }

    private static string ReceiptPath(Guid id) => $"{id:N}.json";
    private static string SuccessorPath(Guid id) => $"compensated-{id:N}.json";
    private static AuthoringApplyResult SourceChanged() => new(412, AuthoringErrorCodes.SourceChanged,
        "Workspace sources changed. Obtain a fresh source ETag and retry.");
    private static AuthoringApplyResult Recoverable(string detail) => new(503, AuthoringErrorCodes.RecoveryRequired, detail);

    private sealed record AuthoringJournalRecord(string SettingId, string Fingerprint,
        CardAuthoringChangeReceipt Receipt, IReadOnlyList<AuthoringSourceChange> Files, AuthoringPackageIdentity? Package = null);

    private sealed record AuthoringPackageIdentity(string SourceId, string PackageId, string Version);
}

public enum AuthoringWriteCheckpoint { Prepared, FileReplaced, SourcesConfirmed }

public sealed record AuthoringApplyResult(int Status, string? Code = null, string? Detail = null,
    CardAuthoringChangeReceipt? Receipt = null, CardAuthoringValidation? Validation = null);
