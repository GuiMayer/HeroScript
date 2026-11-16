using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace API.Contracts;

public static class AuthoringErrorCodes
{
    public const string PreconditionRequired = "AUTHORING_PRECONDITION_REQUIRED";
    public const string SourceChanged = "AUTHORING_SOURCE_CHANGED";
    public const string ChangeConflict = "AUTHORING_CHANGE_CONFLICT";
    public const string InvalidCandidate = "AUTHORING_INVALID_CANDIDATE";
    public const string RecoveryRequired = "AUTHORING_RECOVERY_REQUIRED";
}

[JsonConverter(typeof(JsonStringEnumConverter<CardAuthoringOperationType>))]
public enum CardAuthoringOperationType
{
    [JsonStringEnumMemberName("create")]
    Create,
    [JsonStringEnumMemberName("update")]
    Update,
    [JsonStringEnumMemberName("delete")]
    Delete
}

public sealed record CardAuthoringOperation
{
    public required CardAuthoringOperationType Operation { get; init; }
    public required string Kind { get; init; }
    public required string DefinitionId { get; init; }
    public JsonElement? Definition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourcePackage { get; init; }
}

public sealed record CardAuthoringChangeRequest : IValidatableObject
{
    public required Guid ChangeId { get; init; }
    public IReadOnlyList<CardAuthoringOperation?> Operations { get; init; } = [];
    public Guid? UndoOf { get; init; }
    public Guid? RedoOf { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ChangeId == Guid.Empty)
            yield return new("A non-empty change ID is required.", [nameof(ChangeId)]);

        var compensationCount = (UndoOf.HasValue ? 1 : 0) + (RedoOf.HasValue ? 1 : 0);
        if (UndoOf == Guid.Empty || RedoOf == Guid.Empty || UndoOf == ChangeId || RedoOf == ChangeId)
            yield return new("A compensation must reference a different, non-empty change ID.", [nameof(UndoOf), nameof(RedoOf)]);

        if (Operations is null || compensationCount + (Operations.Count > 0 ? 1 : 0) != 1)
        {
            yield return new("Supply operations, undoOf, or redoOf.", [nameof(Operations), nameof(UndoOf), nameof(RedoOf)]);
            yield break;
        }

        var seen = new HashSet<(string Kind, string Id)>();
        for (var index = 0; index < Operations.Count; index++)
        {
            var operation = Operations[index];
            var member = $"{nameof(Operations)}[{index}]";
            if (operation is null ||
                !Enum.IsDefined(operation.Operation) ||
                operation.Kind is null || (!AuthoringDefinitionKinds.Supports(operation.Kind) && !CompositionAuthoringKinds.Supports(operation.Kind)) ||
                string.IsNullOrWhiteSpace(operation.DefinitionId))
            {
                yield return new("Supply a supported authoring operation with a definition ID.", [member]);
                continue;
            }

            if (!seen.Add((operation.Kind, operation.DefinitionId)))
                yield return new("Supply one operation per kind and definition ID.", [member]);

            var definition = operation.Definition;
            if (operation.Operation == CardAuthoringOperationType.Delete)
            {
                if (definition.HasValue)
                    yield return new("Delete accepts a definition ID only.", [member]);
                continue;
            }

            if (definition is not { ValueKind: JsonValueKind.Object } value)
            {
                yield return new("Create and update require a JSON object.", [member]);
                continue;
            }

            if ((CompositionAuthoringKinds.Identity(operation.Kind) ?? AuthoringDefinitionKinds.Identity(operation.Kind)) is { } identity)
            {
                var declaredIds = value.EnumerateObject()
                    .Where(property => property.Name.Equals(identity, StringComparison.OrdinalIgnoreCase))
                    .Select(property => property.Value).ToArray();
                if (declaredIds.Length != 1 || declaredIds[0].ValueKind != JsonValueKind.String ||
                    !string.Equals(declaredIds[0].GetString(), operation.DefinitionId, StringComparison.Ordinal))
                    yield return new($"Create and update require an object with one matching {identity}.", [member]);
            }
        }
    }
}

public sealed record AuthoringDiagnostic(
    string Code,
    string Severity,
    string Kind,
    string DefinitionId,
    string JsonPointer,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record CardAuthoringChangeReceipt(
    Guid ChangeId,
    string State,
    string PreviousSourceVersion,
    string SourceVersion,
    IReadOnlyDictionary<string, string> ActiveRevisions,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<AuthoringDiagnostic> Diagnostics,
    Guid? UndoOf,
    Guid? RedoOf,
    Guid? CompensatedBy = null,
    bool CanUndo = false,
    bool CanRedo = false);

public sealed record AuthoringCompensationRequest(Guid ChangeId);

public sealed record CardAuthoringValidation(
    Guid ChangeId, string SourceVersion, bool IsValid,
    IReadOnlyDictionary<string, string> CandidateRevisions,
    IReadOnlyList<AuthoringDiagnostic> Diagnostics);

public sealed record CardAuthoringSetting(
    string SettingId, string DisplayName, string? WritablePackageId);

public sealed record CardAuthoringSettings(
    IReadOnlyList<CardAuthoringSetting> Items, IReadOnlyList<Mods.PackageDiagnostic> Diagnostics);

public sealed record AuthoringKind(string Kind, bool SupportsSchema, bool CanCreate);

public sealed record AuthoringKindSchema(
    string Kind, System.Text.Json.Nodes.JsonObject Schema, IReadOnlyDictionary<string, string> References, string? IdentityProperty);

public sealed record AuthoringCapabilities(string Version, bool CanValidate, bool CanApply, bool CanUndo, bool CanRedo);

public sealed record AuthoringPage<T>(IReadOnlyList<T> Items, string? NextCursor, string SourceVersion);

public sealed record CardAuthoringSummary(string DefinitionId, string PackageId, bool CanEdit, bool CanDelete, string? DisplayName = null);

public sealed record CardAuthoringDefinition(
    CardAuthoringSummary Card, JsonElement Source, JsonElement Effective,
    IReadOnlyList<Mods.ContentProvenanceEntry> Provenance);

public sealed record CardAuthoringTag(string Id, bool DeclaredOnCard, bool Referenced);

public sealed record AuthoringDefinitionDetail(
    CardAuthoringSummary Item, JsonElement Source, JsonElement Effective,
    IReadOnlyList<Mods.ContentProvenanceEntry> Provenance);

public sealed record AuthoringReference(string Id);
