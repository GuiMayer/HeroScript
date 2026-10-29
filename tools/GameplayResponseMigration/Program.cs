using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content.Migrations;
using Core.GameplayEvents;
using Core.Run;
using Core.StatusEffects;

// Read-only preflight. File application is deliberately unavailable until publication cutover is certified.
if (args.Length is not (2 or 4) || args[0] != "--setting" || args.Length == 4 && args[2] != "--output")
{
    Console.Error.WriteLine("Usage: --setting <configuration directory> [--output <report.json>]. Content files are never changed.");
    return 2;
}
var root = Path.GetFullPath(args[1]);
if (!Directory.Exists(Path.Combine(root, "Resources")))
{
    Console.Error.WriteLine("The setting directory must contain Resources.");
    return 2;
}
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
options.Converters.Add(new JsonStringEnumConverter());
var catalog = GameplayCoreCatalog.Create(Path.GetFileName(root), "migration.preflight").Value;
var files = new List<FileProposal>(); var errors = new List<MigrationError>(); var examined = 0;
foreach (var kind in new[] { "status-effects", "relics", "phase-sequences" })
{
    var folder = Path.Combine(root, "Resources", kind);
    if (!Directory.Exists(folder)) continue;
    foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var originalBytes = File.ReadAllBytes(path);
        JsonObject document;
        try { document = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new JsonException("Expected a definition dictionary"); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        { errors.Add(new(relative, null, exception.Message)); continue; }
        var candidate = document.DeepClone().AsObject(); var diffs = new List<DefinitionDiff>();
        foreach (var (id, node) in document.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            examined++;
            try
            {
                if (node is not JsonObject definition) throw new JsonException("Expected a definition object");
                var converted = kind switch
                {
                    "status-effects" => Convert(definition, (StatusEffectDefinition value) => GameplayBoundaryResponseMigration.Status(value, catalog)),
                    "relics" => Convert(definition, (RelicDefinition value) => GameplayBoundaryResponseMigration.Relic(value, catalog)),
                    _ => Convert(definition, (PhaseSequenceDefinition value) => GameplayBoundaryResponseMigration.Phases(value, catalog))
                };
                if (converted.IsFailure) { errors.Add(new(relative, id, converted.Error)); continue; }
                if (converted.Value == null) continue;
                var patched = definition.DeepClone().AsObject();
                if (kind == "phase-sequences")
                {
                    var key = Key(patched, "phases") ?? throw new JsonException("Phases were not supplied");
                    var phases = patched[key]!.AsArray(); var migrated = converted.Value["phases"]!.AsArray();
                    for (var index = 0; index < phases.Count; index++)
                    {
                        var target = phases[index]!.AsObject(); var source = migrated[index]!.AsObject();
                        if (source["responses"] == null) continue;
                        Remove(target, "entryEffects"); Remove(target, "exitEffects");
                        Set(target, "responses", source["responses"]!.DeepClone());
                    }
                }
                else
                {
                    Remove(patched, "triggers"); if (kind == "relics") Remove(patched, "reactions");
                    Set(patched, "responses", converted.Value["responses"]!.DeepClone());
                }
                candidate[id] = patched;
                diffs.Add(new(id, definition.DeepClone(), patched.DeepClone()));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
            { errors.Add(new(relative, id, exception.Message)); }
        }
        if (diffs.Count > 0) files.Add(new(relative, Hash(originalBytes), Hash(Encoding.UTF8.GetBytes(candidate.ToJsonString(options) + "\n")), candidate, diffs));
    }
}
var report = new { schemaVersion = 1, mode = "readOnlyPreflight", settingDirectory = root, examinedDefinitions = examined,
    changedFiles = files.Count, changedDefinitions = files.Sum(file => file.Differences.Count), publicationReady = false,
    limitation = "Publication cutover, bundle-wide formula expansion and unsupported stochastic identities remain uncertified. No content files were changed.", files, errors };
var json = JsonSerializer.Serialize(report, options) + "\n";
if (args.Length == 4)
{
    var output = Path.GetFullPath(args[3]);
    if (output.StartsWith(Path.Combine(root, "Resources") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        output.Equals(Path.Combine(root, "package.json"), StringComparison.OrdinalIgnoreCase))
    { Console.Error.WriteLine("The report cannot overwrite setting content."); return 2; }
    File.WriteAllText(output, json, new UTF8Encoding(false));
}
else Console.Write(json);
return errors.Count == 0 ? 0 : 2;

Result<JsonObject?> Convert<T>(JsonObject node, Func<T, Result<T>> migrate) where T : class
{
    var before = node.Deserialize<T>(options) ?? throw new JsonException("Definition is null");
    var after = migrate(before);
    return after.IsFailure ? Result<JsonObject?>.Failure(after.Error) : Result<JsonObject?>.Success(
        ReferenceEquals(before, after.Value) ? null : JsonSerializer.SerializeToNode(after.Value, options)!.AsObject());
}
static string? Key(JsonObject node, string name)
{
    var keys = node.Select(item => item.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (keys.Length > 1) throw new JsonException($"Ambiguous schema field: {name}");
    return keys.SingleOrDefault();
}
static void Remove(JsonObject node, string name) { if (Key(node, name) is { } key) node.Remove(key); }
static void Set(JsonObject node, string name, JsonNode value) { Remove(node, name); node[name] = value; }
static string Hash(byte[] bytes) => System.Convert.ToHexStringLower(SHA256.HashData(bytes));
sealed record DefinitionDiff(string DefinitionId, JsonNode Before, JsonNode After);
sealed record FileProposal(string Path, string OriginalSha256, string ProposedSha256, JsonObject ProposedDocument, IReadOnlyList<DefinitionDiff> Differences);
sealed record MigrationError(string Path, string? DefinitionId, string Message);
