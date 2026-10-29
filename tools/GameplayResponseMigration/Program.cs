using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Core.Content.Migrations;
using Core.GameplayEvents;

// Conversion is shared with Core. Application is an explicit, validated operation with preimage protection.
try { return Run(args); }
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
{ Console.Error.WriteLine(exception.Message); return 2; }

static int Run(string[] args)
{
    if (args.Length is not (2 or 4) || args[0] is not ("--setting" or "--root") || args.Length == 4 && args[2] is not ("--output" or "--apply"))
    {
        Console.Error.WriteLine("Usage: --setting <directory> [--output <report.json>] or --root <package root> [--output <report.json> | --apply <backup directory>].");
        return 2;
    }
    var root = Path.GetFullPath(args[1]);
    var allPackages = args[0] == "--root";
    if (!Directory.Exists(root) || !allPackages && !Directory.Exists(Path.Combine(root, "Resources")))
    { Console.Error.WriteLine("The setting directory must contain Resources."); return 2; }
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
    options.Converters.Add(new JsonStringEnumConverter());
    var catalog = GameplayCoreCatalog.Create(new DirectoryInfo(root).Name, "migration.preflight");
    if (catalog.IsFailure) { Console.Error.WriteLine(catalog.Error); return 2; }
    var files = new List<FileProposal>(); var errors = new List<MigrationError>(); var examined = 0;
    var settings = allPackages ? Directory.GetDirectories(root).Where(path => Directory.Exists(Path.Combine(path, "Resources"))).Order(StringComparer.Ordinal).ToArray() : [root];
    foreach (var setting in settings)
    foreach (var kind in new[] { "status-effects", "relics", "phase-sequences" })
    {
        var folder = Path.Combine(setting, "Resources", kind);
        if (!Directory.Exists(folder)) continue;
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var originalBytes = File.ReadAllBytes(path);
            JsonObject document;
            try
            {
                using var reader = new StreamReader(new MemoryStream(originalBytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                document = JsonNode.Parse(reader.ReadToEnd())?.AsObject() ?? throw new JsonException("Expected a definition dictionary");
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            { errors.Add(new(relative, null, exception.Message)); continue; }
            var candidate = document.DeepClone().AsObject(); var diffs = new List<DefinitionDiff>();
            foreach (var (id, node) in document.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                examined++;
                var original = JsonSerializer.SerializeToElement(node);
                var converted = GameplayContentResponseMigration.Definition(kind, original, catalog.Value);
                if (converted.IsFailure) { errors.Add(new(relative, id, converted.Error)); continue; }
                var patched = JsonNode.Parse(converted.Value.GetRawText());
                if (JsonNode.DeepEquals(node, patched)) continue;
                candidate[id] = patched;
                diffs.Add(new(id, node!.DeepClone(), patched!.DeepClone()));
            }
            if (diffs.Count > 0) files.Add(new(relative, Hash(originalBytes),
                Hash(Encoding.UTF8.GetBytes(candidate.ToJsonString(options) + "\n")), candidate, diffs));
        }
    }
    if (args.Length == 4 && args[2] == "--apply")
    {
        if (!allPackages || errors.Count != 0) { Console.Error.WriteLine("Apply requires a complete package root with no conversion errors."); return 2; }
        return MigrationApplication.Apply(root, Path.GetFullPath(args[3]), files, options);
    }
    var report = new { schemaVersion = 1, mode = "readOnlyPreflight", settingDirectory = root, examinedDefinitions = examined,
        changedFiles = files.Count, changedDefinitions = files.Sum(file => file.Differences.Count), publicationReady = false,
        limitation = "Publication cutover, bundle-wide formula expansion and unsupported stochastic identities remain uncertified. No content files were changed.", files, errors };
    var json = JsonSerializer.Serialize(report, options) + "\n";
    if (args.Length == 4)
    {
        var output = Path.GetFullPath(args[3]);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (output.Equals(root.TrimEnd(Path.DirectorySeparatorChar), comparison) ||
            output.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
        { Console.Error.WriteLine("The report must be outside the setting directory."); return 2; }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, json, new UTF8Encoding(false));
    }
    else Console.Write(json);
    return errors.Count == 0 ? 0 : 2;
}

static string Hash(byte[] bytes) => System.Convert.ToHexStringLower(SHA256.HashData(bytes));
sealed record DefinitionDiff(string DefinitionId, JsonNode Before, JsonNode After);
sealed record FileProposal(string Path, string OriginalSha256, string ProposedSha256, JsonObject ProposedDocument, IReadOnlyList<DefinitionDiff> Differences);
sealed record MigrationError(string Path, string? DefinitionId, string Message);
