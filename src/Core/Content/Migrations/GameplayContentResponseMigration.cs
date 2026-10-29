using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Core.StatusEffects;

namespace Core.Content.Migrations;

public sealed record GameplayDefinitionMigration(string ArtifactPath, string DefinitionId, JsonElement Before, JsonElement After);
public sealed record GameplayContentMigrationPlan(ContentBundle Candidate, ImmutableArray<GameplayDefinitionMigration> Changes,
    ContentGraphValidationResult Validation);

/// <summary>Plans against the entire effective pinned bundle. No file, revision store, run or RNG is changed.</summary>
public static class GameplayContentResponseMigration
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public static Result<GameplayContentMigrationPlan> Plan(ContentBundle original)
    {
        if (original.Manifest.SchemaVersion is not (1 or 2))
            return Result<GameplayContentMigrationPlan>.Failure("Unsupported source content manifest schema for migration");
        var sourceRevision = Revision(original.Manifest);
        if (original.Manifest.Revision != sourceRevision)
            return Result<GameplayContentMigrationPlan>.Failure("Migration requires a bundle with a canonical source revision");
        var artifacts = original.Artifacts.ToBuilder();
        var changes = ImmutableArray.CreateBuilder<GameplayDefinitionMigration>();
        var catalog = GameplayEvents.GameplayCoreCatalog.Create(original.Manifest.ConfigName, sourceRevision);
        if (catalog.IsFailure) return Result<GameplayContentMigrationPlan>.Failure(catalog.Error);
        foreach (var artifact in original.Manifest.Artifacts)
        {
            if (!artifacts.TryGetValue(artifact.Path, out var document) || document.ValueKind != JsonValueKind.Object || Hash(document) != artifact.Hash)
                return Result<GameplayContentMigrationPlan>.Failure($"Source artifact hash mismatch: {artifact.Path}");
            if (artifact.Kind is not ("status-effects" or "relics" or "phase-sequences")) continue;
            var values = document.Deserialize<Dictionary<string, JsonElement>>()!;
            foreach (var (id, definition) in values.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray())
            {
                var converted = Definition(artifact.Kind, definition, catalog.Value);
                if (converted.IsFailure) return Result<GameplayContentMigrationPlan>.Failure($"{artifact.Path}/{id}: {converted.Error}");
                if (CanonicalJson.ComputeHash(definition) == CanonicalJson.ComputeHash(converted.Value)) continue;
                values[id] = converted.Value;
                changes.Add(new(artifact.Path, id, definition.Clone(), converted.Value));
            }
            artifacts[artifact.Path] = JsonSerializer.SerializeToElement(values);
        }
        if (changes.Count == 0)
            return Result<GameplayContentMigrationPlan>.Success(new(original, [], ShadowValidator().Validate(original)));
        var manifest = original.Manifest with { SchemaVersion = 2, Artifacts = original.Manifest.Artifacts.Select(artifact => artifact with {
            Hash = Hash(artifacts[artifact.Path]), DefinitionCount = artifacts[artifact.Path].EnumerateObject().Count() }).ToArray() };
        manifest = manifest with { Revision = Revision(manifest) };
        var candidate = original with { Manifest = manifest, Artifacts = artifacts.ToImmutable() };
        return Result<GameplayContentMigrationPlan>.Success(new(candidate, changes.ToImmutable(), ShadowValidator().Validate(candidate)));
    }

    public static Result<JsonElement> Definition(string kind, JsonElement original, GameplayEvents.GameplayEventCatalog catalog)
    {
        if (original.ValueKind != JsonValueKind.Object) return Result<JsonElement>.Failure("Migration definition must be an object");
        try
        {
            var converted = kind switch
            {
                "status-effects" => Convert<StatusEffectDefinition>(original, value => GameplayBoundaryResponseMigration.Status(value, catalog)),
                "relics" => Convert<RelicDefinition>(original, value => GameplayBoundaryResponseMigration.Relic(value, catalog)),
                "phase-sequences" => Convert<PhaseSequenceDefinition>(original, value => GameplayBoundaryResponseMigration.Phases(value, catalog)),
                _ => Result<JsonObject?>.Success(null)
            };
            if (converted.IsFailure) return Result<JsonElement>.Failure(converted.Error);
            if (converted.Value == null) return Result<JsonElement>.Success(original);
            var patched = JsonNode.Parse(original.GetRawText())!.AsObject();
            if (kind == "phase-sequences")
            {
                var phases = patched[Key(patched, "phases")!]!.AsArray();
                var replacements = converted.Value["phases"]!.AsArray();
                for (var index = 0; index < phases.Count; index++)
                {
                    var target = phases[index]!.AsObject(); var replacement = replacements[index]!.AsObject();
                    if (replacement["responses"] == null) continue;
                    Remove(target, "entryEffects"); Remove(target, "exitEffects");
                    Set(target, "responses", replacement["responses"]!.DeepClone());
                }
            }
            else
            {
                Remove(patched, "triggers"); if (kind == "relics") Remove(patched, "reactions");
                Set(patched, "responses", converted.Value["responses"]!.DeepClone());
            }
            return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(patched));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { return Result<JsonElement>.Failure($"Invalid migration input: {exception.Message}"); }
    }

    private static Result<JsonObject?> Convert<T>(JsonElement value, Func<T, Result<T>> migrate) where T : class
    {
        var before = value.Deserialize<T>(Options)!; var after = migrate(before);
        return after.IsFailure ? Result<JsonObject?>.Failure(after.Error) : Result<JsonObject?>.Success(
            ReferenceEquals(before, after.Value) ? null : JsonSerializer.SerializeToNode(after.Value, Options)!.AsObject());
    }
    private static string Hash(JsonElement document) => CanonicalJson.ComputeHash(document.Deserialize<Dictionary<string, JsonElement>>()!);
    private static string Revision(ContentManifest manifest) => CanonicalJson.ComputeHash(new {
        manifest.SchemaVersion, manifest.ConfigName, manifest.ConfigChain, manifest.Artifacts });
    private static ContentGraphValidator ShadowValidator() => new(new ContentKindRegistry(ContentKindRegistry.Default.Kinds,
        allowShadowResponseDeclarations: true));
    private static string? Key(JsonObject node, string name)
    {
        var keys = node.Select(item => item.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (keys.Length > 1) throw new JsonException($"Ambiguous schema field: {name}");
        return keys.SingleOrDefault();
    }
    private static void Remove(JsonObject node, string name) { if (Key(node, name) is { } key) node.Remove(key); }
    private static void Set(JsonObject node, string name, JsonNode value) { Remove(node, name); node[name] = value; }
}
