using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;

namespace Core.Content;

/// <summary>
/// Immutable rule graph captured from exactly one content revision.
/// Gameplay services resolve definitions here instead of consulting live files.
/// </summary>
public sealed class ContentRuntime
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly ImmutableDictionary<string, ImmutableDictionary<string, JsonElement>> _definitions;

    private ContentRuntime(
        ContentManifest manifest,
        ImmutableDictionary<string, ImmutableDictionary<string, JsonElement>> definitions)
    {
        Manifest = manifest;
        _definitions = definitions;
    }

    public ContentManifest Manifest { get; }

    public static Result<ContentRuntime> Create(ContentBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (string.IsNullOrWhiteSpace(bundle.Manifest.Revision))
            return Result<ContentRuntime>.Failure("Content bundle revision is required");

        var byKind = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, JsonElement>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var kindGroup in bundle.Manifest.Artifacts
                     .OrderBy(artifact => artifact.Kind, StringComparer.Ordinal)
                     .ThenBy(artifact => artifact.Path, StringComparer.Ordinal)
                     .GroupBy(artifact => artifact.Kind, StringComparer.OrdinalIgnoreCase))
        {
            var definitions = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
            foreach (var artifact in kindGroup)
            {
                if (!bundle.Artifacts.TryGetValue(artifact.Path, out var document) ||
                    document.ValueKind != JsonValueKind.Object)
                {
                    return Result<ContentRuntime>.Failure($"Content artifact payload is missing or invalid: {artifact.Path}");
                }

                foreach (var definition in document.EnumerateObject())
                {
                    // ResourceLoader wraps a standalone JSON object as
                    // "definition". That key is local to the artifact and
                    // cannot identify multiple files of the same kind.
                    var definitionId = string.Equals(
                        definition.Name,
                        "definition",
                        StringComparison.Ordinal)
                        ? Path.GetFileNameWithoutExtension(artifact.Path)
                        : definition.Name;
                    definitions[definitionId] = definition.Value.Clone();
                }
            }

            byKind[kindGroup.Key] = definitions.ToImmutable();
        }

        return Result<ContentRuntime>.Success(new ContentRuntime(bundle.Manifest, byKind.ToImmutable()));
    }

    public Result<T> GetDefinition<T>(string kind, string definitionId)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return Result<T>.Failure("Content kind is required");
        if (string.IsNullOrWhiteSpace(definitionId))
            return Result<T>.Failure("Definition id is required");
        if (!_definitions.TryGetValue(kind, out var definitions) ||
            !definitions.TryGetValue(definitionId, out var json))
        {
            return Result<T>.Failure(
                $"Definition '{definitionId}' of kind '{kind}' was not found in revision {Manifest.Revision}");
        }

        try
        {
            var definition = json.Deserialize<T>(SerializerOptions);
            return definition == null
                ? Result<T>.Failure($"Definition '{definitionId}' of kind '{kind}' is invalid")
                : Result<T>.Success(definition);
        }
        catch (Exception exception)
        {
            return Result<T>.Failure(
                $"Definition '{definitionId}' of kind '{kind}' could not be deserialized: {exception.Message}",
                exception);
        }
    }

    public IReadOnlyDictionary<string, JsonElement> GetDefinitions(string kind) =>
        _definitions.TryGetValue(kind, out var definitions)
            ? definitions
            : ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public interface IContentRuntimeResolver
{
    Result<ContentRuntime> Resolve(string revision, string? configName = null);
    void Invalidate(string? revision = null);
}

/// <summary>
/// Process-wide cache of immutable runtimes. A revision is content-addressed,
/// so it is safe for every run using that revision to share the same instance.
/// </summary>
public sealed class ContentRuntimeResolver : IContentRuntimeResolver
{
    private readonly IContentPublicationService _publications;
    private readonly ConcurrentDictionary<string, ContentRuntime> _runtimes =
        new(StringComparer.Ordinal);

    public ContentRuntimeResolver(IContentPublicationService publications)
    {
        _publications = publications ?? throw new ArgumentNullException(nameof(publications));
    }

    public Result<ContentRuntime> Resolve(string revision, string? configName = null)
    {
        if (string.IsNullOrWhiteSpace(revision))
            return Result<ContentRuntime>.Failure("Content revision is required");
        if (_runtimes.TryGetValue(revision, out var cached))
        {
            if (!string.IsNullOrWhiteSpace(configName) &&
                !string.Equals(cached.Manifest.ConfigName, configName, StringComparison.OrdinalIgnoreCase))
            {
                return Result<ContentRuntime>.Failure(
                    $"Content revision '{revision}' belongs to configuration " +
                    $"'{cached.Manifest.ConfigName}', not '{configName}'");
            }

            return Result<ContentRuntime>.Success(cached);
        }

        var bundle = _publications
            .ResolveBundleAsync(revision, configName)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        if (bundle.IsFailure)
            return Result<ContentRuntime>.Failure(bundle.Error);

        var runtime = ContentRuntime.Create(bundle.Value);
        if (runtime.IsFailure)
            return runtime;

        var winner = _runtimes.GetOrAdd(revision, runtime.Value);
        return Result<ContentRuntime>.Success(winner);
    }

    public void Invalidate(string? revision = null)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            _runtimes.Clear();
            return;
        }

        _runtimes.TryRemove(revision, out _);
    }
}
