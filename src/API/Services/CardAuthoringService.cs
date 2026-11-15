using System.Text.Json;
using System.Text.Json.Nodes;
using API.Contracts;
using Core.Common;
using Core.Content;
using Core.Run.Content;
using Mods;

namespace API.Services;

public sealed class CardAuthoringService(
    IEnumerable<IPackageProvider> providers,
    ISettingCompiler settings,
    IContentKindRegistry kinds,
    IContentPublicationService publication,
    IConfiguration configuration)
{
    private const string Cards = "cards";
    private const string NewCatalog = "Resources/cards/authoring.json";

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
        var configured = configuration[$"Authoring:WritablePackages:{setting.SettingId}"];
        return setting.Packages.Any(reference => reference.PackageId == configured) ? configured : null;
    }

    public async Task<Result<CardAuthoringWorkspace>> CaptureAsync(CancellationToken cancellationToken)
    {
        var snapshots = new List<AuthoringPackageSnapshot>();
        foreach (var provider in providers)
        {
            if (provider is not DirectoryPackageProvider directory)
                return Result<CardAuthoringWorkspace>.Failure($"Provider '{provider.ProviderId}' does not support source snapshots.");
            var snapshot = await directory.CaptureAuthoringSnapshotAsync(cancellationToken);
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
        var cards = new SortedDictionary<string, CardAuthoringCard>(StringComparer.Ordinal);
        var patches = selected.SelectMany(package => package.Manifest.Content)
            .Where(entry => entry.Operation == PackageContentOperation.Patch && entry.Target?.Kind == Cards)
            .Select(entry => entry.Target!).ToArray();
        foreach (var package in selected)
        {
            var entries = package.Manifest.Content.Where(entry =>
                entry.Operation == PackageContentOperation.Definition && entry.Kind == Cards);
            foreach (var entry in entries)
            {
                var artifact = entry.ArtifactPath!.Replace('\\', '/');
                var sharedSource = package.Manifest.Content.Count(other =>
                    string.Equals(AuthoringPackageSnapshot.SourceKey(package, other.SourcePath),
                        AuthoringPackageSnapshot.SourceKey(package, entry.SourcePath), StringComparison.OrdinalIgnoreCase)) > 1;
                foreach (var card in compilation.Bundle.Artifacts[artifact].EnumerateObject())
                {
                    var patched = patches.Any(target =>
                        target.ArtifactPath.Replace('\\', '/') == artifact &&
                        (target.DefinitionId is null || target.DefinitionId == card.Name));
                    var record = new CardAuthoringCard(card.Name, card.Value, package, entry,
                        package == writable && !patched && !sharedSource);
                    if (!cards.TryAdd(card.Name, record))
                        return Result<CardAuthoringView>.Failure($"Duplicate definition 'cards/{card.Name}'.");
                }
            }
        }

        return Result<CardAuthoringView>.Success(new(compilation, writable, cards));
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
                return Result<JsonElement>.Failure($"Card '{card.Id}' has no source definition.");
            return kinds.Normalize(Cards, card.Id, value);
        }
        return kinds.Normalize(Cards, card.Id, source.Value);
    }

    public async Task<CardAuthoringValidation> ValidateAsync(CardAuthoringWorkspace workspace, CardAuthoringView view,
        CardAuthoringChangeRequest request, CancellationToken cancellationToken) =>
        (await PlanAsync(workspace, view, request, cancellationToken)).Validation;

    public async Task<CardAuthoringPlan> PlanAsync(CardAuthoringWorkspace workspace, CardAuthoringView view,
        CardAuthoringChangeRequest request, CancellationToken cancellationToken)
    {
        var diagnostics = new List<AuthoringDiagnostic>();
        var revisions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        CardAuthoringValidation Validation() => new(request.ChangeId, workspace.ETag,
            diagnostics.All(diagnostic => diagnostic.Severity != "error"), revisions, diagnostics);
        CardAuthoringPlan InvalidPlan() => new(Validation(), [], []);

        var writable = view.Writable;
        if (writable is null)
        {
            diagnostics.Add(Diagnostic("CARD_PACKAGE_READ_ONLY", "", "", "No writable package is configured for this setting."));
            return InvalidPlan();
        }

        var snapshot = workspace.SnapshotFor(writable);
        var entries = writable.Manifest.Content.ToList();
        var sourceChanges = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var removals = new List<string>();
        foreach (var operation in request.Operations.OfType<CardAuthoringOperation>())
        {
            var id = operation.DefinitionId;
            if (operation.Operation == CardAuthoringOperationType.Create &&
                (id.Length > 128 || id.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                    character is not ('.' or '_' or '-')) || id is "." or ".."))
            {
                diagnostics.Add(Diagnostic("CARD_ID_INVALID", id, "/cardId", "Use an addressable card identifier."));
                continue;
            }
            view.Cards.TryGetValue(id, out var card);
            if (operation.Operation == CardAuthoringOperationType.Create ? card is not null : card is null)
            {
                diagnostics.Add(Diagnostic(card is null ? "CARD_NOT_FOUND" : "CARD_ALREADY_EXISTS", id, "",
                    card is null ? "Card does not exist." : "Card already exists."));
                continue;
            }
            if (card is { CanEdit: false })
            {
                diagnostics.Add(Diagnostic("CARD_READ_ONLY", id, "", "The card is inherited, patched, or uses a shared source file."));
                continue;
            }
            if (operation.Definition is { } definition)
            {
                var invalidRarity = definition.EnumerateObject().Any(property =>
                    property.Name.Equals("rarity", StringComparison.OrdinalIgnoreCase) &&
                    (property.Value.ValueKind != JsonValueKind.String ||
                     !Enum.GetNames<CardRarity>().Contains(property.Value.GetString(), StringComparer.OrdinalIgnoreCase)));
                if (invalidRarity)
                {
                    diagnostics.Add(Diagnostic("CARD_RARITY_INVALID", id, "/rarity", "Use a recognized card rarity name."));
                    continue;
                }
                var invalidTag = definition.EnumerateObject().Any(property =>
                    property.Name.Equals("tags", StringComparison.OrdinalIgnoreCase) &&
                    (property.Value.ValueKind != JsonValueKind.Array ||
                     property.Value.EnumerateArray().Any(tag => tag.ValueKind != JsonValueKind.String ||
                         string.IsNullOrWhiteSpace(tag.GetString()))));
                if (invalidTag)
                {
                    diagnostics.Add(Diagnostic("CARD_TAGS_INVALID", id, "/tags", "Tags must be non-empty strings."));
                    continue;
                }
                var validation = ContentKindRegistry.Canonical.Validate(Cards, id, definition);
                if (validation.IsFailure)
                {
                    diagnostics.Add(Diagnostic("CARD_SCHEMA_INVALID", id, "", validation.Error));
                    continue;
                }
            }

            var entry = card?.Entry ?? entries.FirstOrDefault(candidate => candidate is
                { Operation: PackageContentOperation.Definition, Kind: Cards, DefinitionId: null } &&
                entries.Count(other => SameSource(writable, candidate, other)) == 1 &&
                !workspace.Catalog.Packages.SelectMany(package => package.Manifest.Content)
                    .Any(other => other.Target is { Kind: Cards } target &&
                        target.ArtifactPath.Replace('\\', '/') == candidate.ArtifactPath!.Replace('\\', '/')));
            if (entry is null)
            {
                entry = new PackageContentEnvelope
                {
                    Kind = Cards, Operation = PackageContentOperation.Definition,
                    SourcePath = NewCatalog, ArtifactPath = "cards/authoring.json"
                };
                var sourceKey = AuthoringPackageSnapshot.SourceKey(writable, entry.SourcePath);
                if (snapshot.Sources.Any(file => string.Equals(file.Key, sourceKey, StringComparison.OrdinalIgnoreCase)))
                {
                    diagnostics.Add(Diagnostic("CARD_CATALOG_CONFLICT", id, "", "The proposed card catalog path is already declared."));
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
                    diagnostics.Add(Diagnostic("CARD_SOURCE_UNAVAILABLE", id, "", source.Error));
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
        var candidateSnapshot = snapshot.WithCandidate(writable, writable.Manifest with { Content = entries },
            changedBytes);
        var compiler = new SettingCompiler(workspace.Snapshots.Select(item => item == snapshot ? candidateSnapshot : item), kinds);
        var catalog = await compiler.GetCatalogAsync(cancellationToken);
        var bundles = new List<ContentBundle>();
        var operations = request.Operations.OfType<CardAuthoringOperation>().ToArray();
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
                var operation = operations.FirstOrDefault(item =>
                    error.StartsWith($"{Cards}/{item.DefinitionId} ", StringComparison.Ordinal) ||
                    error.StartsWith($"{Cards}/{item.DefinitionId}:", StringComparison.Ordinal) ||
                    error.EndsWith($"missing {Cards}/{item.DefinitionId}", StringComparison.Ordinal));
                diagnostics.Add(Diagnostic("CARD_GRAPH_INVALID", operation?.DefinitionId ?? "", "", error, settingId: settingId));
            }
            diagnostics.AddRange(result.Warnings.Select(warning =>
                Diagnostic("SETTING_VALIDATION_WARNING", "", "", warning, "warning", settingId)));
            revisions.Add(settingId, compiled.Value.Bundle.Manifest.Revision);
            bundles.Add(compiled.Value.Bundle);
        }
        var files = changedBytes.Where(file => snapshot.FindSource(file.Key) is not { } previous ||
                !previous.Span.SequenceEqual(file.Value.Span))
            .Select(file => new AuthoringSourceChange(file.Key, snapshot.FindSource(file.Key)?.ToArray(), file.Value.ToArray()))
            .Concat(removals.Select(path => new AuthoringSourceChange(path, snapshot.FindSource(path)!.Value.ToArray(), null)))
            .ToArray();
        return new(Validation(), files, bundles);
    }

    private static bool SameSource(PackageSource package, PackageContentEnvelope left, PackageContentEnvelope right) =>
        string.Equals(AuthoringPackageSnapshot.SourceKey(package, left.SourcePath),
            AuthoringPackageSnapshot.SourceKey(package, right.SourcePath), StringComparison.OrdinalIgnoreCase);

    private static AuthoringDiagnostic Diagnostic(string code, string id, string pointer, string message,
        string severity = "error", string? settingId = null)
    {
        var parameters = new Dictionary<string, string> { ["message"] = message };
        if (settingId is not null)
            parameters["settingId"] = settingId;
        return new(code, severity, Cards, id, pointer, parameters);
    }
}

public sealed record CardAuthoringWorkspace(
    IReadOnlyList<AuthoringPackageSnapshot> Snapshots, SettingCompiler Compiler, SettingCatalog Catalog, string ETag)
{
    public AuthoringPackageSnapshot SnapshotFor(PackageSource package) =>
        Snapshots.Single(snapshot => snapshot.ProviderId == package.ProviderId);
}

public sealed record CardAuthoringView(SettingCompilation Compilation, PackageSource? Writable,
    IReadOnlyDictionary<string, CardAuthoringCard> Cards);

public sealed record CardAuthoringCard(string Id, JsonElement Effective, PackageSource Package,
    PackageContentEnvelope Entry, bool CanEdit);

public sealed record CardAuthoringPlan(CardAuthoringValidation Validation, IReadOnlyList<AuthoringSourceChange> Files,
    IReadOnlyList<ContentBundle> Bundles);

public sealed record AuthoringSourceChange(string Path, byte[]? Before, byte[]? After);
