using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;

namespace Mods;

public interface IPackageProvider
{
    string ProviderId { get; }
    Task<PackageDiscovery> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<Result<JsonElement>> ReadJsonAsync(
        PackageSource package,
        string relativePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads packages only from a root explicitly supplied by the application.
/// It never probes environment variables, working directories or parent paths.
/// </summary>
public sealed class DirectoryPackageProvider : IPackageProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _rootPath;

    public DirectoryPackageProvider(string providerId, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Package provider id is required", nameof(providerId));
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Package provider root is required", nameof(rootPath));
        ProviderId = providerId.Trim();
        _rootPath = Path.GetFullPath(rootPath);
    }

    public string ProviderId { get; }

    public async Task<PackageDiscovery> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var packages = ImmutableArray.CreateBuilder<PackageSource>();
        var settings = ImmutableArray.CreateBuilder<SettingSource>();
        var diagnostics = ImmutableArray.CreateBuilder<PackageDiagnostic>();
        if (!Directory.Exists(_rootPath))
        {
            diagnostics.Add(Diagnostic(
                PackageDiagnosticSeverity.Warning,
                "PACKAGE_ROOT_MISSING",
                $"Configured package root does not exist: {_rootPath}"));
            return new PackageDiscovery { Diagnostics = diagnostics.ToImmutable() };
        }

        foreach (var packageDirectory in FindPackageDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceId = NormalizeRelative(Path.GetRelativePath(_rootPath, packageDirectory));
            if (HasReparsePoint(packageDirectory))
            {
                diagnostics.Add(Diagnostic(
                    PackageDiagnosticSeverity.Error,
                    "PACKAGE_LINK_REJECTED",
                    "Package directories cannot be symbolic links or reparse points",
                    sourcePath: sourceId));
                continue;
            }

            var files = Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeRelative(Path.GetRelativePath(packageDirectory, path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (var collision in files
                         .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Distinct(StringComparer.Ordinal).Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    PackageDiagnosticSeverity.Error,
                    "PACKAGE_PATH_CASE_COLLISION",
                    $"Package contains paths that differ only by case: {string.Join(", ", collision)}",
                    sourcePath: sourceId));
            }

            var manifestPath = Path.Combine(packageDirectory, "package.json");
            var manifestResult = await ReadDocumentAsync<PackageManifest>(manifestPath, cancellationToken)
                .ConfigureAwait(false);
            if (manifestResult.IsFailure)
            {
                diagnostics.Add(Diagnostic(
                    PackageDiagnosticSeverity.Error,
                    "PACKAGE_MANIFEST_INVALID",
                    manifestResult.Error,
                    sourcePath: sourceId));
                continue;
            }

            var package = new PackageSource
            {
                ProviderId = ProviderId,
                SourceId = sourceId,
                Manifest = manifestResult.Value
            };
            packages.Add(package);

            foreach (var settingPath in files.Where(path =>
                         path.StartsWith("settings/", StringComparison.OrdinalIgnoreCase) &&
                         path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var fullPath = ResolvePackagePath(packageDirectory, settingPath);
                if (fullPath.IsFailure)
                {
                    diagnostics.Add(Diagnostic(
                        PackageDiagnosticSeverity.Error,
                        "SETTING_PATH_INVALID",
                        fullPath.Error,
                        package.Manifest.PackageId,
                        settingPath));
                    continue;
                }
                var setting = await ReadDocumentAsync<SettingDefinition>(fullPath.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (setting.IsFailure)
                {
                    diagnostics.Add(Diagnostic(
                        PackageDiagnosticSeverity.Error,
                        "SETTING_INVALID",
                        setting.Error,
                        package.Manifest.PackageId,
                        settingPath));
                    continue;
                }
                settings.Add(new SettingSource
                {
                    ProviderId = ProviderId,
                    PackageId = package.Manifest.PackageId,
                    SourcePath = settingPath,
                    Definition = setting.Value
                });
            }
        }

        return new PackageDiscovery
        {
            Packages = packages
                .OrderBy(package => package.Manifest.PackageId, StringComparer.Ordinal)
                .ThenBy(package => package.Manifest.Version, StringComparer.Ordinal)
                .ThenBy(package => package.SourceId, StringComparer.Ordinal)
                .ToImmutableArray(),
            Settings = settings
                .OrderBy(setting => setting.Definition.SettingId, StringComparer.Ordinal)
                .ThenBy(setting => setting.PackageId, StringComparer.Ordinal)
                .ThenBy(setting => setting.SourcePath, StringComparer.Ordinal)
                .ToImmutableArray(),
            Diagnostics = diagnostics
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.PackageId, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
                .ToImmutableArray()
        };
    }

    public async Task<Result<JsonElement>> ReadJsonAsync(
        PackageSource package,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!string.Equals(package.ProviderId, ProviderId, StringComparison.Ordinal))
            return Result<JsonElement>.Failure($"Package belongs to provider '{package.ProviderId}', not '{ProviderId}'");
        var packageDirectory = ResolveSourceDirectory(package.SourceId);
        if (packageDirectory.IsFailure)
            return Result<JsonElement>.Failure(packageDirectory.Error);
        var path = ResolvePackagePath(packageDirectory.Value, relativePath);
        if (path.IsFailure)
            return Result<JsonElement>.Failure(path.Error);
        if (!File.Exists(path.Value))
            return Result<JsonElement>.Failure($"Package file not found: {relativePath}");
        if (ContainsReparsePoint(packageDirectory.Value, path.Value))
            return Result<JsonElement>.Failure($"Package file cannot be a symbolic link: {relativePath}");

        try
        {
            await using var stream = File.OpenRead(path.Value);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Result<JsonElement>.Success(document.RootElement.Clone());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result<JsonElement>.Failure($"Failed to read package file '{relativePath}': {exception.Message}");
        }
    }

    private IEnumerable<string> FindPackageDirectories()
    {
        if (File.Exists(Path.Combine(_rootPath, "package.json")))
            return [_rootPath];
        return Directory.EnumerateDirectories(_rootPath, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => File.Exists(Path.Combine(directory, "package.json")))
            .OrderBy(directory => NormalizeRelative(Path.GetRelativePath(_rootPath, directory)), StringComparer.Ordinal)
            .ToArray();
    }

    private Result<string> ResolveSourceDirectory(string sourceId)
    {
        if (sourceId == ".")
            return Result<string>.Success(_rootPath);
        return ResolveWithin(_rootPath, sourceId, "package source");
    }

    private static Result<string> ResolvePackagePath(string packageDirectory, string relativePath) =>
        ResolveWithin(packageDirectory, relativePath, "package path");

    private static Result<string> ResolveWithin(string root, string relativePath, string label)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return Result<string>.Failure($"Invalid {label}: {relativePath}");
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            return Result<string>.Failure($"Path traversal is not allowed: {relativePath}");
        }
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return Result<string>.Failure($"Path escapes the package root: {relativePath}");
        return Result<string>.Success(fullPath);
    }

    private static async Task<Result<T>> ReadDocumentAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return value == null
                ? Result<T>.Failure($"JSON document is empty: {path}")
                : Result<T>.Success(value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result<T>.Failure($"Failed to read JSON document '{path}': {exception.Message}");
        }
    }

    private static bool HasReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool ContainsReparsePoint(string root, string path)
    {
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        FileSystemInfo? current = new FileInfo(path);
        while (current != null && current.FullName.Length >= rootPath.Length)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                return true;
            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), rootPath, StringComparison.OrdinalIgnoreCase))
                break;
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }
        return false;
    }

    private static string NormalizeRelative(string path) =>
        string.IsNullOrEmpty(path) ? "." : path.Replace('\\', '/');

    private static PackageDiagnostic Diagnostic(
        PackageDiagnosticSeverity severity,
        string code,
        string message,
        string? packageId = null,
        string? sourcePath = null) => new()
        {
            Severity = severity,
            Code = code,
            Message = message,
            PackageId = packageId,
            SourcePath = sourcePath
        };
}
