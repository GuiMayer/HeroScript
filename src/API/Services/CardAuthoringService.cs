using System.Text.Json;
using System.Text.Json.Nodes;
using API.Contracts;
using Core.Common;
using Core.Content;
using Core.Run.Content;
using Mods;

namespace API.Services;

public sealed partial class CardAuthoringService(
    IEnumerable<IPackageProvider> providers,
    ISettingCompiler settings,
    IContentKindRegistry kinds,
    IContentPublicationService publication,
    IConfiguration configuration)
{
    private const string Cards = "cards";

    public async Task<SettingCatalog> DiscoverAsync(CancellationToken cancellationToken) =>
        await settings.GetCatalogAsync(cancellationToken);

    public async Task<bool> IsManagedAsync(string settingId, CancellationToken cancellationToken)
    {
        var catalog = await DiscoverAsync(cancellationToken);
        var setting = catalog.Settings.FirstOrDefault(item => item.Definition.SettingId == settingId);
        if (setting is null)
            return false;
        var managed = catalog.Settings.Select(item => WritablePackage(item.Definition)).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var packages = SettingCompiler.ResolvePackages(setting.Definition, catalog.Packages);
        return packages.IsFailure || packages.Value.Any(package => managed.Contains(package.Manifest.PackageId));
    }

    public string? WritablePackage(SettingDefinition setting)
    {
        var legacy = configuration[$"Authoring:WritablePackages:{setting.SettingId}"];
        var configured = setting.AuthoringPackageId ?? (string.IsNullOrWhiteSpace(legacy)
            ? setting.Packages.FirstOrDefault()?.PackageId : legacy);
        return setting.Packages.Any(reference => reference.PackageId == configured) ? configured : null;
    }

    public async Task<Result<CardAuthoringWorkspace>> CaptureAsync(CancellationToken cancellationToken,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>?>? overlay = null)
    {
        var snapshots = new List<AuthoringPackageSnapshot>();
        foreach (var provider in providers)
        {
            if (provider is not DirectoryPackageProvider directory)
                return Result<CardAuthoringWorkspace>.Failure($"Provider '{provider.ProviderId}' does not support source snapshots.");
            var snapshot = await directory.CaptureAuthoringSnapshotAsync(cancellationToken, overlay);
            if (snapshot.IsFailure)
                return Result<CardAuthoringWorkspace>.Failure(snapshot.Error);
            snapshots.Add(snapshot.Value);
        }

        var compiler = new SettingCompiler(snapshots, kinds);
        var catalog = await compiler.GetCatalogAsync(cancellationToken);
        var files = snapshots.SelectMany(snapshot => snapshot.Sources.Select(file =>
            new KeyValuePair<string, ReadOnlyMemory<byte>>($"{snapshot.ProviderId}/{file.Key}", file.Value)));
        var etag = AuthoringSourceVersion.CreateETag(files.ToDictionary(StringComparer.Ordinal));
        return Result<CardAuthoringWorkspace>.Success(new(snapshots, compiler, catalog, etag));
    }

    public async Task<Result<CardAuthoringView>> GetViewAsync(CardAuthoringWorkspace workspace, string settingId,
        CancellationToken cancellationToken)
    {
        var compiled = await workspace.Compiler.CompileFromCatalogAsync(settingId, workspace.Catalog, cancellationToken);
        if (compiled.IsFailure)
            return Result<CardAuthoringView>.Failure(compiled.Error);
        var compilation = compiled.Value;
        var selected = SettingCompiler.ResolvePackages(compilation.Setting, workspace.Catalog.Packages).Value;
        var writable = selected.SingleOrDefault(package => package.Manifest.PackageId == WritablePackage(compilation.Setting));
        var definitions = new Dictionary<string, SortedDictionary<string, CardAuthoringCard>>(StringComparer.Ordinal);
        var patches = selected.SelectMany(package => package.Manifest.Content)
            .Where(entry => entry.Operation == PackageContentOperation.Patch)
            .Select(entry => entry.Target!).ToArray();
        foreach (var package in selected)
        {
            var entries = package.Manifest.Content.Where(entry =>
                entry.Operation == PackageContentOperation.Definition && AuthoringDefinitionKinds.Supports(entry.Kind));
            foreach (var entry in entries)
            {
                if (!definitions.TryGetValue(entry.Kind, out var items))
                    definitions.Add(entry.Kind, items = new(StringComparer.Ordinal));
                var artifact = entry.ArtifactPath!.Replace('\\', '/');
                var sharedSource = package.Manifest.Content.Count(other =>
                    string.Equals(AuthoringPackageSnapshot.SourceKey(package, other.SourcePath),
                        AuthoringPackageSnapshot.SourceKey(package, entry.SourcePath), StringComparison.OrdinalIgnoreCase)) > 1;
                foreach (var card in compilation.Bundle.Artifacts[artifact].EnumerateObject())
                {
                    var patched = patches.Any(target => target.Kind == entry.Kind &&
                        target.ArtifactPath.Replace('\\', '/') == artifact &&
                        (target.DefinitionId is null || target.DefinitionId == card.Name));
                    var record = new CardAuthoringCard(card.Name, card.Value, package, entry,
                        package == writable && !patched && !sharedSource);
                    if (!items.TryAdd(card.Name, record))
                        return Result<CardAuthoringView>.Failure($"Duplicate definition '{entry.Kind}/{card.Name}'.");
                }
            }
        }

        return Result<CardAuthoringView>.Success(new(compilation, writable, definitions));
    }

    public async Task<Result<JsonElement>> ReadSourceAsync(CardAuthoringWorkspace workspace, CardAuthoringCard card,
        CancellationToken cancellationToken)
    {
        var source = await workspace.SnapshotFor(card.Package).ReadJsonAsync(card.Package, card.Entry.SourcePath, cancellationToken);
        if (source.IsFailure)
            return source;
        if (card.Entry.DefinitionId is null)
        {
            if (!source.Value.TryGetProperty(card.Id, out var value))
                return Result<JsonElement>.Failure($"Definition '{card.Id}' has no source definition.");
            return kinds.Normalize(card.Entry.Kind, card.Id, value);
        }
        return kinds.Normalize(card.Entry.Kind, card.Id, source.Value);
    }

    public async Task<CardAuthoringValidation> ValidateAsync(CardAuthoringWorkspace workspace, CardAuthoringView view,
        CardAuthoringChangeRequest request, CancellationToken cancellationToken) =>
        (await PlanAsync(workspace, view, request, cancellationToken)).Validation;

    public async Task<CardAuthoringPlan> PlanAsync(CardAuthoringWorkspace workspace, CardAuthoringView view,
        CardAuthoringChangeRequest request, CancellationToken cancellationToken)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("authoring_validation");
        var diagnostics = new List<AuthoringDiagnostic>();
        var revisions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        CardAuthoringValidation Validation() => new(request.ChangeId, workspace.ETag,
            diagnostics.All(diagnostic => diagnostic.Severity != "error"), revisions, diagnostics);
        CardAuthoringPlan InvalidPlan() => new(Validation(), [], []);

        var writable = view.Writable;
        if (writable is null)
        {
            var kind = request.Operations.OfType<CardAuthoringOperation>().First().Kind;
            diagnostics.Add(Diagnostic(Code(kind, "PACKAGE_READ_ONLY"), "", "", "No writable package is configured for this setting.", kind: kind));
            return InvalidPlan();
        }

        var snapshot = workspace.SnapshotFor(writable);
        var entries = writable.Manifest.Content.ToList();
        var sourceChanges = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var removals = new List<string>();
        foreach (var operation in request.Operations.OfType<CardAuthoringOperation>())
        {
            var id = operation.DefinitionId;
            var kind = operation.Kind;
            var identity = AuthoringDefinitionKinds.Identity(kind);
            if (operation.Operation == CardAuthoringOperationType.Create &&
                (id.Length > 128 || id.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                    character is not ('.' or '_' or '-')) || id is "." or ".."))
            {
                diagnostics.Add(Diagnostic(Code(kind, "ID_INVALID"), id, identity is null ? "" : $"/{identity}",
                    "Use an addressable identifier.", kind: kind));
                continue;
            }
            view.ForKind(kind).TryGetValue(id, out var card);
            if (operation.Operation == CardAuthoringOperationType.Create ? card is not null : card is null)
            {
                diagnostics.Add(Diagnostic(Code(kind, card is null ? "NOT_FOUND" : "ALREADY_EXISTS"), id, "",
                    card is null ? "Definition does not exist." : "Definition already exists.", kind: kind));
                continue;
            }
            if (card is { CanEdit: false })
            {
                diagnostics.Add(Diagnostic(Code(kind, "READ_ONLY"), id, "", "The definition is inherited, patched, or uses a shared source file.", kind: kind));
                continue;
            }
            if (operation.Definition is { } definition)
            {
                var invalidRarity = kind == Cards && definition.EnumerateObject().Any(property =>
                    property.Name.Equals("rarity", StringComparison.OrdinalIgnoreCase) &&
                    (property.Value.ValueKind != JsonValueKind.String ||
                     !Enum.GetNames<CardRarity>().Contains(property.Value.GetString(), StringComparer.OrdinalIgnoreCase)));
                if (invalidRarity)
                {
                    diagnostics.Add(Diagnostic("CARD_RARITY_INVALID", id, "/rarity", "Use a recognized card rarity name."));
                    continue;
                }
                var invalidTag = kind == Cards && definition.EnumerateObject().Any(property =>
                    property.Name.Equals("tags", StringComparison.OrdinalIgnoreCase) &&
                    (property.Value.ValueKind != JsonValueKind.Array ||
                     property.Value.EnumerateArray().Any(tag => tag.ValueKind != JsonValueKind.String ||
                         string.IsNullOrWhiteSpace(tag.GetString()))));
                if (invalidTag)
                {
                    diagnostics.Add(Diagnostic("CARD_TAGS_INVALID", id, "/tags", "Tags must be non-empty strings."));
                    continue;
                }
                var validation = ContentKindRegistry.Canonical.Validate(kind, id, definition);
                if (validation.IsFailure)
                {
                    diagnostics.Add(Diagnostic(Code(kind, "SCHEMA_INVALID"), id, "", validation.Error, kind: kind));
                    continue;
                }
            }

            var entry = card?.Entry ?? entries.FirstOrDefault(candidate => candidate is
                { Operation: PackageContentOperation.Definition, DefinitionId: null } && candidate.Kind == kind &&
                entries.Count(other => SameSource(writable, candidate, other)) == 1 &&
                !workspace.Catalog.Packages.SelectMany(package => package.Manifest.Content)
                    .Any(other => other.Target is { } target && target.Kind == kind &&
                        target.ArtifactPath.Replace('\\', '/') == candidate.ArtifactPath!.Replace('\\', '/')));
            if (entry is null)
            {
                var catalogName = kind == Cards ? "authoring.json" : $"{writable.Manifest.PackageId}.authoring.json";
                entry = new PackageContentEnvelope
                {
                    Kind = kind, Operation = PackageContentOperation.Definition,
                    SourcePath = $"Resources/{kind}/{catalogName}", ArtifactPath = $"{kind}/{catalogName}"
                };
                var sourceKey = AuthoringPackageSnapshot.SourceKey(writable, entry.SourcePath);
                if (snapshot.Sources.Any(file => string.Equals(file.Key, sourceKey, StringComparison.OrdinalIgnoreCase)))
                {
                    diagnostics.Add(Diagnostic(Code(kind, "CATALOG_CONFLICT"), id, "", "The proposed catalog path is already declared.", kind: kind));
                    continue;
                }
                entries.Add(entry);
                sourceChanges.Add(sourceKey, new());
            }

            var key = AuthoringPackageSnapshot.SourceKey(writable, entry.SourcePath);
            if (entry.DefinitionId is not null)
            {
                if (operation.Operation == CardAuthoringOperationType.Delete)
                {
                    entries.Remove(entry);
                    removals.Add(key);
                }
                else
                    sourceChanges[key] = JsonNode.Parse(operation.Definition!.Value.GetRawText())!.AsObject();
                continue;
            }

            if (!sourceChanges.TryGetValue(key, out var document))
            {
                var source = await snapshot.ReadJsonAsync(writable, entry.SourcePath, cancellationToken);
                if (source.IsFailure)
                {
                    diagnostics.Add(Diagnostic(Code(kind, "SOURCE_UNAVAILABLE"), id, "", source.Error, kind: kind));
                    continue;
                }
                sourceChanges.Add(key, document = JsonNode.Parse(source.Value.GetRawText())!.AsObject());
            }

            if (operation.Operation == CardAuthoringOperationType.Delete)
                document.Remove(id);
            else
                document[id] = JsonNode.Parse(operation.Definition!.Value.GetRawText());
        }

        if (diagnostics.Count > 0)
            return InvalidPlan();

        var changedBytes = sourceChanges.ToDictionary(file => file.Key,
            file => AuthoringJsonSource.Rewrite(snapshot.FindSource(file.Key), file.Value), StringComparer.Ordinal);
        if (!entries.SequenceEqual(writable.Manifest.Content))
        {
            var key = AuthoringPackageSnapshot.SourceKey(writable, "package.json");
            var original = snapshot.FindSource(key)!.Value;
            var originalManifest = JsonNode.Parse(DirectoryPackageProvider.WithoutBom(original).Span)!.AsObject();
            var contentKey = originalManifest.Select(property => property.Key)
                .FirstOrDefault(name => name.Equals("content", StringComparison.OrdinalIgnoreCase)) ?? "content";
            var originalEntries = originalManifest[contentKey] as JsonArray;
            var candidateEntries = new JsonArray();
            foreach (var entry in entries)
            {
                var originalIndex = Array.IndexOf(writable.Manifest.Content.ToArray(), entry);
                candidateEntries.Add(originalIndex >= 0 && originalEntries is not null
                    ? originalEntries[originalIndex]?.DeepClone()
                    : new JsonObject
                    {
                        ["operation"] = "definition", ["kind"] = entry.Kind,
                        ["sourcePath"] = entry.SourcePath, ["artifactPath"] = entry.ArtifactPath
                    });
            }
            originalManifest[contentKey] = candidateEntries;
            changedBytes[key] = AuthoringJsonSource.Rewrite(original, originalManifest);
        }
        var files = changedBytes.Where(file => snapshot.FindSource(file.Key) is not { } previous ||
                !previous.Span.SequenceEqual(file.Value.Span))
            .Select(file => new AuthoringSourceChange(file.Key, snapshot.FindSource(file.Key)?.ToArray(), file.Value.ToArray()))
            .Concat(removals.Select(path => new AuthoringSourceChange(path, snapshot.FindSource(path)!.Value.ToArray(), null)))
            .ToArray();
        return await ValidateCandidateAsync(workspace, writable, writable.Manifest with { Content = entries }, files,
            request.ChangeId, request.Operations.OfType<CardAuthoringOperation>().Select(item => (item.Kind, item.DefinitionId)).ToArray(),
            cancellationToken);
    }

    public async Task<Result<CardAuthoringPlan>> PlanCompensationAsync(CardAuthoringWorkspace workspace,
        CardAuthoringView view, Guid changeId, IReadOnlyList<AuthoringSourceChange> targetFiles,
        CancellationToken cancellationToken)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("authoring_validation");
        var writable = view.Writable;
        if (writable is null)
            return Result<CardAuthoringPlan>.Failure("No writable package is configured for this setting.");

        var snapshot = workspace.SnapshotFor(writable);
        var manifestKey = AuthoringPackageSnapshot.SourceKey(writable, "package.json");
        var restoredManifest = writable.Manifest;
        var manifestChange = targetFiles.SingleOrDefault(file => file.Path == manifestKey);
        if (manifestChange is not null)
        {
            if (manifestChange.Before is null || !EqualSource(snapshot.FindSource(manifestKey), manifestChange.After))
                return Result<CardAuthoringPlan>.Failure($"Package manifest '{manifestKey}' changed after the target operation.");
            var historicalManifest = DirectoryPackageProvider.ReadCapturedManifest(manifestChange.Before);
            if (historicalManifest is null || historicalManifest.PackageId != restoredManifest.PackageId ||
                historicalManifest.Version != restoredManifest.Version)
                return Result<CardAuthoringPlan>.Failure("The target operation belongs to a different package version.");
            restoredManifest = historicalManifest;
        }

        var currentEntries = writable.Manifest.Content.Where(IsEditableSource).ToLookup(
            entry => AuthoringPackageSnapshot.SourceKey(writable, entry.SourcePath), StringComparer.Ordinal);
        var restoredEntries = restoredManifest.Content.Where(IsEditableSource).ToLookup(
            entry => AuthoringPackageSnapshot.SourceKey(writable, entry.SourcePath), StringComparer.Ordinal);
        var changes = new List<AuthoringSourceChange>(targetFiles.Count);
        var affected = new HashSet<(string Kind, string Id)>();
        foreach (var source in targetFiles.Reverse())
        {
            var current = snapshot.FindSource(source.Path);
            var next = source.Before;
            if (source.Path != manifestKey)
            {
                if ((source.After is not null && currentEntries[source.Path].Count() != 1) ||
                    (source.Before is not null && restoredEntries[source.Path].Count() != 1))
                    return Result<CardAuthoringPlan>.Failure($"Source '{source.Path}' is no longer an authorized definition source.");

                var entry = currentEntries[source.Path].SingleOrDefault() ?? restoredEntries[source.Path].Single();
                if (writable.Manifest.Content.Count(item => SameSource(writable, item, entry)) > 1 ||
                    restoredManifest.Content.Count(item => SameSource(writable, item, entry)) > 1)
                    return Result<CardAuthoringPlan>.Failure($"Source '{source.Path}' is shared by several catalog entries.");
                if (entry.DefinitionId is { } definitionId)
                    affected.Add((entry.Kind, definitionId));
                else
                    affected.UnionWith(AuthoringJsonSource.ChangedProperties(source.Before, source.After).Select(id => (entry.Kind, id)));

                if (!EqualSource(current, source.After))
                {
                    if (current is null || source.Before is null || source.After is null ||
                        entry.DefinitionId is not null)
                        return Result<CardAuthoringPlan>.Failure($"Source '{source.Path}' changed after the target operation.");
                    var merged = AuthoringJsonSource.ReverseObjectChanges(current.Value, source.Before, source.After);
                    if (merged is null)
                        return Result<CardAuthoringPlan>.Failure($"An affected definition in '{source.Path}' changed after the target operation.");
                    next = merged.Value.ToArray();
                }
            }

            changes.Add(new(source.Path, current?.ToArray(), next));
        }

        if (affected.Any(item => view.ForKind(item.Kind).TryGetValue(item.Id, out var card) && !card.CanEdit))
            return Result<CardAuthoringPlan>.Failure("An affected definition is inherited, patched, or uses a shared source file.");

        return Result<CardAuthoringPlan>.Success(await ValidateCandidateAsync(workspace, writable, restoredManifest,
            changes, changeId, affected.ToArray(), cancellationToken));
    }

    private async Task<CardAuthoringPlan> ValidateCandidateAsync(CardAuthoringWorkspace workspace, PackageSource writable,
        PackageManifest manifest, IReadOnlyList<AuthoringSourceChange> files, Guid changeId,
        IReadOnlyList<(string Kind, string Id)> affected,
        CancellationToken cancellationToken)
    {
        var snapshot = workspace.SnapshotFor(writable);
        var candidateSnapshot = snapshot.WithCandidate(writable, manifest, files.Where(file => file.After is not null)
            .ToDictionary(file => file.Path, file => (ReadOnlyMemory<byte>)file.After!, StringComparer.Ordinal),
            files.Where(file => file.After is null).Select(file => file.Path));
        var compiler = new SettingCompiler(workspace.Snapshots.Select(item => item == snapshot ? candidateSnapshot : item), kinds);
        var catalog = await compiler.GetCatalogAsync(cancellationToken);
        var bundles = new List<ContentBundle>();
        var diagnostics = new List<AuthoringDiagnostic>();
        var revisions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in workspace.Catalog.Settings)
        {
            var dependencies = SettingCompiler.ResolvePackages(setting.Definition, workspace.Catalog.Packages);
            if (dependencies.IsFailure || !dependencies.Value.Contains(writable))
                continue;

            var settingId = setting.Definition.SettingId;
            var compiled = await compiler.CompileFromCatalogAsync(settingId, catalog, cancellationToken);
            if (compiled.IsFailure)
            {
                diagnostics.Add(Diagnostic("SETTING_COMPILATION_FAILED", "", "", compiled.Error, settingId: settingId));
                continue;
            }

            var result = publication.Validate(compiled.Value.Bundle);
            foreach (var error in result.Errors)
            {
                var (kind, id) = affected.FirstOrDefault(item =>
                    error.StartsWith($"{item.Kind}/{item.Id} ", StringComparison.Ordinal) ||
                    error.StartsWith($"{item.Kind}/{item.Id}:", StringComparison.Ordinal) ||
                    error.EndsWith($"missing {item.Kind}/{item.Id}", StringComparison.Ordinal));
                var diagnosticKind = kind ?? affected.FirstOrDefault().Kind ?? Cards;
                diagnostics.Add(Diagnostic(Code(diagnosticKind, "GRAPH_INVALID"), id ?? "", "", error,
                    settingId: settingId, kind: diagnosticKind));
            }
            diagnostics.AddRange(result.Warnings.Select(warning =>
                Diagnostic("SETTING_VALIDATION_WARNING", "", "", warning, "warning", settingId)));
            revisions.Add(settingId, compiled.Value.Bundle.Manifest.Revision);
            bundles.Add(compiled.Value.Bundle);
        }
        return new(new(changeId, workspace.ETag, diagnostics.All(diagnostic => diagnostic.Severity != "error"),
            revisions, diagnostics), files, bundles);
    }

    private static bool IsEditableSource(PackageContentEnvelope entry) =>
        entry.Operation == PackageContentOperation.Definition && AuthoringDefinitionKinds.Supports(entry.Kind);

    private static string Code(string kind, string suffix) => kind == Cards ? $"CARD_{suffix}" : $"DEFINITION_{suffix}";

    private static bool EqualSource(ReadOnlyMemory<byte>? current, byte[]? expected) =>
        current is null ? expected is null : expected is not null && current.Value.Span.SequenceEqual(expected);

    private static bool SameSource(PackageSource package, PackageContentEnvelope left, PackageContentEnvelope right) =>
        string.Equals(AuthoringPackageSnapshot.SourceKey(package, left.SourcePath),
            AuthoringPackageSnapshot.SourceKey(package, right.SourcePath), StringComparison.OrdinalIgnoreCase);

    private static AuthoringDiagnostic Diagnostic(string code, string id, string pointer, string message,
        string severity = "error", string? settingId = null, string kind = Cards)
    {
        var parameters = new Dictionary<string, string> { ["message"] = message };
        if (settingId is not null)
            parameters["settingId"] = settingId;
        return new(code, severity, kind, id, pointer, parameters);
    }
}

public sealed record CardAuthoringWorkspace(
    IReadOnlyList<AuthoringPackageSnapshot> Snapshots, SettingCompiler Compiler, SettingCatalog Catalog, string ETag)
{
    public AuthoringPackageSnapshot SnapshotFor(PackageSource package) =>
        Snapshots.Single(snapshot => snapshot.ProviderId == package.ProviderId);
}

public sealed record CardAuthoringView(SettingCompilation Compilation, PackageSource? Writable,
    IReadOnlyDictionary<string, SortedDictionary<string, CardAuthoringCard>> Definitions)
{
    private static readonly IReadOnlyDictionary<string, CardAuthoringCard> Empty =
        System.Collections.Immutable.ImmutableDictionary<string, CardAuthoringCard>.Empty;

    public IReadOnlyDictionary<string, CardAuthoringCard> Cards => ForKind("cards");
    public IReadOnlyDictionary<string, CardAuthoringCard> ForKind(string kind) =>
        Definitions.TryGetValue(kind, out var definitions) ? definitions : Empty;
}

public sealed record CardAuthoringCard(string Id, JsonElement Effective, PackageSource Package,
    PackageContentEnvelope Entry, bool CanEdit);

public sealed record CardAuthoringPlan(CardAuthoringValidation Validation, IReadOnlyList<AuthoringSourceChange> Files,
    IReadOnlyList<ContentBundle> Bundles, string? CandidateSourceVersion = null, IReadOnlyList<string>? RemovedSettings = null,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? AddedSourceChecks = null);

public sealed record AuthoringSourceChange(string Path, byte[]? Before, byte[]? After);
