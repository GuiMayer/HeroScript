using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using API.Contracts;
using Core.Determinism;
using Mods;

namespace API.Services;

public sealed partial class CardAuthoringService
{
    private static string CustomizationPath(string kind, string id) =>
        $"patches/{kind}/{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))}.authoring.json";

    private static bool IsCustomization(PackageContentEnvelope entry) =>
        entry.Operation == PackageContentOperation.Patch &&
        entry.Target is { Kind: { } kind, DefinitionId: { } id, PackageId.Length: > 0 } &&
        AuthoringDefinitionKinds.SupportsCustomization(kind) && entry.SourcePath == CustomizationPath(kind, id);

    private async Task<AuthoringDiagnostic?> PlanCustomizationAsync(
        CardAuthoringWorkspace workspace, CardAuthoringView view, PackageSource writable, CardAuthoringCard card,
        CardAuthoringOperation operation, List<PackageContentEnvelope> entries, List<PackageDependency> dependencies,
        Dictionary<string, JsonObject> sourceChanges, List<string> removals, CancellationToken cancellationToken)
    {
        var kind = operation.Kind;
        AuthoringDiagnostic Reject(string message) => Diagnostic(Code(kind, "CUSTOMIZATION_UNAVAILABLE"), card.Id, "", message, kind: kind);

        if (!card.CanCustomize)
            return Reject("Choose a different writable package or resolve the existing patches on this definition.");

        var path = CustomizationPath(kind, card.Id);
        var key = AuthoringPackageSnapshot.SourceKey(writable, path);
        var existing = card.Customization;

        if (operation.Operation == CardAuthoringOperationType.ResetCustomization)
        {
            if (existing is null)
                return Reject("This package does not customize the definition.");
            entries.Remove(existing);
            removals.Add(key);
            return null;
        }

        var baseline = card.Effective;
        if (existing is not null)
        {
            var source = await ReadSourceAsync(workspace, card, cancellationToken);
            if (source.IsFailure)
                return Reject(source.Error);
            baseline = source.Value;
        }
        var desired = kinds.Normalize(kind, card.Id, operation.Definition!.Value);
        if (desired.IsFailure)
            return Reject(desired.Error);
        if (JsonElement.DeepEquals(baseline, desired.Value))
        {
            if (existing is not null)
            {
                entries.Remove(existing);
                removals.Add(key);
            }
            return null;
        }

        var snapshot = workspace.SnapshotFor(writable);
        if (existing is null && snapshot.Sources.Any(file => string.Equals(file.Key, key, StringComparison.OrdinalIgnoreCase)))
            return Reject("The customization path is already in use.");

        var (payload, strategy) = SettingCompiler.CreateDefinitionPatch(baseline, desired.Value);
        var entry = new PackageContentEnvelope
        {
            Operation = PackageContentOperation.Patch,
            SourcePath = path,
            Target = new()
            {
                PackageId = card.Package.Manifest.PackageId,
                Kind = kind,
                ArtifactPath = card.Entry.ArtifactPath!.Replace('\\', '/'),
                DefinitionId = card.Id
            },
            ExpectedHash = CanonicalJson.ComputeHash(baseline),
            PatchStrategy = strategy
        };

        if (existing is null)
        {
            // A fresh package can sort before the base. Declare the dependency when needed.
            var earlierPackages = view.Compilation.Packages.TakeWhile(item => item.PackageId != writable.Manifest.PackageId);
            if (!earlierPackages.Any(item => item.PackageId == entry.Target.PackageId) &&
                !dependencies.Any(item => item.PackageId == entry.Target.PackageId))
                dependencies.Add(new() { PackageId = entry.Target.PackageId, VersionRange = card.Package.Manifest.Version });
            entries.Add(entry);
        }
        else
        {
            entries[entries.IndexOf(existing)] = entry;
        }
        sourceChanges[key] = JsonNode.Parse(payload.GetRawText())!.AsObject();
        return null;
    }
}
