using System.Text.Json;
using System.Text.Json.Nodes;
using API.Contracts;
using Core.Content;
using Mods;

namespace API.Services;

public sealed partial class CardAuthoringService
{
    private static readonly JsonSerializerOptions CompositionJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    internal static string PackageEntryId(PackageSource source) => source.SourceId == "." ? "@root" : source.SourceId;
    internal bool IsStartupSetting(string id) =>
        (configuration.GetSection("Content:StartupSettings").Get<string[]>() ?? []).Contains(id, StringComparer.Ordinal);
    private static string InPackage(string source, string path) => source == "." ? path : $"{source}/{path}";

    internal static IEnumerable<CompositionSource> CompositionSources(CardAuthoringWorkspace workspace, string kind)
    {
        var snapshot = workspace.Snapshots.SingleOrDefault();
        if (snapshot is null)
            yield break;
        if (kind == CompositionAuthoringKinds.Packages)
        {
            foreach (var package in workspace.Catalog.Packages)
                yield return new(PackageEntryId(package), package.Manifest.PackageId,
                    InPackage(package.SourceId, "package.json"), $"{package.Manifest.PackageId} · {package.Manifest.Version}", snapshot);
        }
        else if (kind == CompositionAuthoringKinds.Settings)
        {
            foreach (var setting in workspace.Catalog.Settings)
                yield return new(setting.Definition.SettingId, setting.PackageId,
                    InPackage(setting.PackageSourceId, setting.SourcePath), setting.Definition.DisplayName, snapshot);
        }
    }

    internal async Task<CardAuthoringPlan> PlanCompositionAsync(CardAuthoringWorkspace workspace, CardAuthoringChangeRequest request,
        Func<string, Task<byte[]?>> readFile, IReadOnlyList<AuthoringSourceChange>? compensation, CancellationToken cancellationToken)
    {
        var diagnostics = new List<AuthoringDiagnostic>();
        var files = new List<AuthoringSourceChange>();
        var bundles = new List<ContentBundle>();
        var revisions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var removed = Array.Empty<string>();
        CardAuthoringPlan Plan(CardAuthoringWorkspace? candidate = null)
        {
            var addedChecks = candidate?.Snapshots.Single().Sources.Where(source =>
                    workspace.Snapshots.Single().FindSource(source.Key) is null && files.All(file => file.Path != source.Key))
                .ToDictionary(source => source.Key, source => source.Value, StringComparer.Ordinal);
            return new(new(request.ChangeId, workspace.ETag, diagnostics.All(item => item.Severity != "error"),
                revisions, diagnostics), files, bundles, candidate?.ETag, removed, addedChecks);
        }
        void Reject(string message, string kind = CompositionAuthoringKinds.Settings, string id = "") =>
            diagnostics.Add(Diagnostic("WORKSPACE_COMPOSITION_INVALID", id, "", message, kind: kind));

        if (workspace.Snapshots.Count != 1)
        {
            Reject("Workspace composition requires one directory provider.");
            return Plan();
        }

        var snapshot = workspace.Snapshots.Single();
        if (compensation is not null)
        {
            foreach (var file in compensation.Reverse())
            {
                byte[]? bytes;
                try { bytes = await readFile(file.Path); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Reject(exception.Message);
                    return Plan();
                }
                if (!EqualSource(bytes is null ? (ReadOnlyMemory<byte>?)null : bytes, file.After))
                {
                    Reject($"Source '{file.Path}' changed after the target operation.");
                    return Plan();
                }
                files.Add(new(file.Path, file.After, file.Before));
            }
        }
        else
        {
            var packageSources = workspace.Catalog.Packages.ToDictionary(PackageEntryId, item => item.SourceId, StringComparer.Ordinal);
            foreach (var operation in request.Operations.OfType<CardAuthoringOperation>()
                         .OrderBy(item => item.Kind == CompositionAuthoringKinds.Packages ? 0 : 1))
            {
                var kind = operation.Kind;
                var id = operation.DefinitionId;
                if (!CompositionAuthoringKinds.Supports(kind))
                {
                    Reject("Use a setting's authoring endpoint to change content definitions.", kind, id);
                    continue;
                }
                var isPackage = kind == CompositionAuthoringKinds.Packages;
                var existing = CompositionSources(workspace, kind).FirstOrDefault(source => source.Id == id);
                var creating = operation.Operation == CardAuthoringOperationType.Create;
                if (creating == (existing is not null))
                {
                    Reject(creating ? "This entry already exists." : "This entry does not exist.", kind, id);
                    continue;
                }
                if (creating && !AddressableId(id))
                {
                    Reject("Use an addressable identifier.", kind, id);
                    continue;
                }
                var path = existing?.Path;
                if (path is null && isPackage)
                {
                    if (packageSources.ContainsKey("@root"))
                    {
                        Reject("A root package workspace cannot discover sibling packages.", kind, id);
                        continue;
                    }
                    path = InPackage(id, "package.json");
                }
                if (path is null)
                {
                    if (operation.SourcePackage is not { } owner || !packageSources.TryGetValue(owner, out var source))
                    {
                        Reject("Choose the package directory that will own this setting.", kind, id);
                        continue;
                    }
                    path = InPackage(source, $"settings/{id}.json");
                }
                if (files.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    Reject($"Multiple operations address '{path}'.", kind, id);
                    continue;
                }
                byte[]? before;
                try { before = await readFile(path); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Reject(exception.Message, kind, id);
                    continue;
                }
                if (creating ? before is not null : !EqualSource(snapshot.FindSource(path), before))
                {
                    Reject($"Source '{path}' conflicts with the current workspace.", kind, id);
                    continue;
                }
                if (operation.Operation == CardAuthoringOperationType.Delete)
                {
                    if (isPackage)
                        packageSources.Remove(id);
                    files.Add(new(path, before, null));
                    continue;
                }
                var definition = operation.Definition!.Value;
                try
                {
                    if (isPackage) definition.Deserialize<PackageManifest>(CompositionJson);
                    else definition.Deserialize<SettingDefinition>(CompositionJson);
                }
                catch (JsonException exception) { Reject(exception.Message, kind, id); continue; }
                var document = JsonNode.Parse(definition.GetRawText())!.AsObject();
                var bytes = AuthoringJsonSource.Rewrite(snapshot.FindSource(path), document).ToArray();
                if (before is null || !before.AsSpan().SequenceEqual(bytes))
                    files.Add(new(path, before, bytes));
                if (creating && isPackage)
                    packageSources.Add(id, id);
            }
        }
        if (diagnostics.Count > 0)
            return Plan();

        var overlay = files.ToDictionary(file => file.Path, file =>
            file.After is null ? (ReadOnlyMemory<byte>?)null : file.After, StringComparer.Ordinal);
        var captured = await CaptureAsync(cancellationToken, overlay);
        if (captured.IsFailure)
        {
            Reject(captured.Error);
            return Plan();
        }
        var candidate = captured.Value;
        var nextSettings = candidate.Catalog.Settings.Select(setting => setting.Definition.SettingId).ToHashSet(StringComparer.Ordinal);
        removed = workspace.Catalog.Settings.Select(setting => setting.Definition.SettingId).Where(id => !nextSettings.Contains(id)).ToArray();
        var startup = configuration.GetSection("Content:StartupSettings").Get<string[]>() ?? [];
        if (nextSettings.Count == 0 || startup.Any(id => !string.IsNullOrWhiteSpace(id) && !nextSettings.Contains(id)))
            Reject("The workspace must retain its configured startup settings and at least one game setting.");
        foreach (var setting in workspace.Catalog.Settings.Where(item => removed.Contains(item.Definition.SettingId)))
            if (!overlay.TryGetValue(InPackage(setting.PackageSourceId, setting.SourcePath), out var settingBytes) || settingBytes.HasValue)
                Reject("Delete the setting explicitly before deleting its owning package.", id: setting.Definition.SettingId);
        // A write must remain observable after discovery, including settings created in the same batch as their package.
        foreach (var file in files.Where(file => file.After is not null))
            if (candidate.Snapshots.Single().FindSource(file.Path) is null)
                Reject($"Source '{file.Path}' would not be discovered after the change.");
        foreach (var error in candidate.Catalog.Diagnostics.Where(item => item.Severity == PackageDiagnosticSeverity.Error))
            Reject(error.Message);
        if (diagnostics.Count > 0)
            return Plan();

        var manifestPaths = files.Where(file => file.Path == "package.json" || file.Path.EndsWith("/package.json", StringComparison.Ordinal))
            .Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        if (manifestPaths.Count > 0)
            foreach (var package in candidate.Catalog.Packages)
            {
                var valid = await candidate.Compiler.ValidatePackageSourceAsync(package, candidate.Catalog.Packages, cancellationToken);
                if (valid.IsFailure)
                    Reject(valid.Error, CompositionAuthoringKinds.Packages, PackageEntryId(package));
            }
        foreach (var setting in candidate.Catalog.Settings)
        {
            var id = setting.Definition.SettingId;
            var directlyChanged = overlay.ContainsKey(InPackage(setting.PackageSourceId, setting.SourcePath));
            if (!directlyChanged && manifestPaths.Count == 0)
                continue;
            var dependencies = SettingCompiler.ResolvePackages(setting.Definition, candidate.Catalog.Packages);
            var previous = SettingCompiler.ResolvePackages(setting.Definition, workspace.Catalog.Packages);
            if (!directlyChanged && dependencies.IsSuccess && previous.IsSuccess &&
                !dependencies.Value.Concat(previous.Value).Any(package => manifestPaths.Contains(InPackage(package.SourceId, "package.json"))))
                continue;
            var compiled = await candidate.Compiler.CompileFromCatalogAsync(id, candidate.Catalog, cancellationToken);
            if (compiled.IsFailure)
            {
                Reject(compiled.Error, id: id);
                continue;
            }
            var result = publication.Validate(compiled.Value.Bundle);
            foreach (var error in result.Errors) Reject(error, id: id);
            foreach (var warning in result.Warnings)
                diagnostics.Add(Diagnostic("SETTING_VALIDATION_WARNING", id, "", warning, "warning", id, CompositionAuthoringKinds.Settings));
            bundles.Add(compiled.Value.Bundle);
            revisions[id] = compiled.Value.Bundle.Manifest.Revision;
        }
        return Plan(candidate);
    }

    private static bool AddressableId(string id) => id.Length is > 0 and <= 128 && id is not "." and not ".." &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}

internal sealed record CompositionSource(string Id, string PackageId, string Path, string? Name, AuthoringPackageSnapshot Snapshot);
