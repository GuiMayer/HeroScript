using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Caching;
using Core.Common;
using Core.Config;
using Core.Logging;

namespace Core.Combat.TurnPhase;

public sealed class PhaseSequenceLoader : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly ILogger _logger;
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly LruCache<string, PhaseSequenceDefinition> _cache;

    public PhaseSequenceLoader(
        ILogger logger,
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        int cacheCapacity = 64)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _cache = new LruCache<string, PhaseSequenceDefinition>(cacheCapacity);
    }

    public string CacheName => "PhaseSequences";

    public Result<PhaseSequenceDefinition> LoadFromResource(
        string sequenceId,
        string configName = "default",
        bool useCache = true)
    {
        if (string.IsNullOrWhiteSpace(sequenceId))
            return Result<PhaseSequenceDefinition>.Failure("Sequence id cannot be empty");
        if (string.IsNullOrWhiteSpace(configName))
            return Result<PhaseSequenceDefinition>.Failure("Config name cannot be empty");

        var cacheKey = $"{configName.ToLowerInvariant()}::{sequenceId.ToLowerInvariant()}";
        if (useCache && _cache.TryGetValue(cacheKey, out var cached) && cached != null)
            return Result<PhaseSequenceDefinition>.Success(cached);

        try
        {
            var resources = _resourceLoader.LoadResource(
                $"phase-sequences/{sequenceId}.json",
                _configManager.ResolveInheritanceChain(configName),
                strictMode: false);
            if (resources.Count == 0)
                return Result<PhaseSequenceDefinition>.Failure($"Phase sequence not found: {sequenceId}");

            var element = resources.TryGetValue(sequenceId, out var exact)
                ? exact
                : resources.Values.First();
            return ParseAndCache(element.GetRawText(), sequenceId, cacheKey);
        }
        catch (Exception exception)
        {
            return Result<PhaseSequenceDefinition>.Failure(
                $"Error loading phase sequence '{sequenceId}': {exception.Message}",
                exception);
        }
    }

    public Result<PhaseSequenceDefinition> LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result<PhaseSequenceDefinition>.Failure("JSON string cannot be empty");
        return ParseAndCache(json, null, null);
    }

    public void ClearCache() => _cache.Clear();

    public void Invalidate(string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            _cache.Clear();
        else
            _cache.Remove(key);
    }

    public CacheServiceStats GetStats()
    {
        var stats = _cache.GetStats();
        return new CacheServiceStats
        {
            CacheName = CacheName,
            Capacity = stats.Capacity,
            Count = stats.Count,
            Hits = stats.Hits,
            Misses = stats.Misses,
            Evictions = stats.Evictions,
            HitRate = stats.HitRate,
            LastInvalidation = stats.LastInvalidation
        };
    }

    public static Result ValidateSequence(PhaseSequenceDefinition sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (sequence.Phases.Count < 3)
            return Result.Failure("A phase sequence requires at least START, MIDDLE and END phases");
        if (sequence.Phases.Any(phase => string.IsNullOrWhiteSpace(phase.PhaseId)))
            return Result.Failure("Every phase requires phaseId");
        if (sequence.Phases.Select(phase => phase.PhaseId).Distinct(StringComparer.Ordinal).Count() !=
            sequence.Phases.Count)
            return Result.Failure("Phase ids must be unique");
        if (sequence.Phases.Any(phase => phase.Role == PhaseRole.Unspecified))
            return Result.Failure("Every phase requires a semantic role");
        foreach (var role in new[] { PhaseRole.Start, PhaseRole.Middle, PhaseRole.End })
        {
            if (!sequence.Phases.Any(phase => phase.Role == role))
                return Result.Failure($"Phase sequence requires at least one {role.ToString().ToUpperInvariant()} phase");
        }

        var ordered = sequence.Phases.OrderBy(phase => phase.Order).ToArray();
        if (!ordered.SequenceEqual(sequence.Phases) || ordered.Select(phase => phase.Order).Distinct().Count() != ordered.Length)
            return Result.Failure("Phase order values must be unique and ascending");
        var roleRanks = ordered.Select(phase => phase.Role switch
        {
            PhaseRole.Start => 0,
            PhaseRole.Middle => 1,
            PhaseRole.End => 2,
            _ => -1
        }).ToArray();
        if (!roleRanks.SequenceEqual(roleRanks.OrderBy(rank => rank)))
            return Result.Failure("Phase roles must follow START, MIDDLE, END order");

        var known = sequence.Phases.Select(phase => phase.PhaseId).ToHashSet(StringComparer.Ordinal);
        foreach (var phase in sequence.Phases)
        foreach (var next in phase.ValidNextPhaseIds)
        {
            if (!known.Contains(next))
                return Result.Failure($"Phase {phase.PhaseId} references invalid next phase: {next}");
        }

        return Result.Success();
    }

    /// <summary>
    /// Operational subset currently supported by the canonical activation
    /// planner. Richer phase graphs stay valid content for future modes, but a
    /// combat rule cannot select them until explicit phase commands exist.
    /// </summary>
    public static Result ValidateCanonicalActivationSequence(PhaseSequenceDefinition sequence)
    {
        var validation = ValidateSequence(sequence);
        if (validation.IsFailure)
            return validation;

        foreach (var role in new[] { PhaseRole.Start, PhaseRole.Middle, PhaseRole.End })
        {
            var count = sequence.Phases.Count(phase => phase.Role == role);
            if (count != 1)
            {
                return Result.Failure(
                    $"Canonical activation flow currently requires exactly one {role.ToString().ToUpperInvariant()} phase; found {count}");
            }
        }

        return Result.Success();
    }

    private Result<PhaseSequenceDefinition> ParseAndCache(
        string json,
        string? sequenceId,
        string? cacheKey)
    {
        try
        {
            var sequence = JsonSerializer.Deserialize<PhaseSequenceDefinition>(json, JsonOptions);
            if (sequence == null)
                return Result<PhaseSequenceDefinition>.Failure("Failed to deserialize phase sequence");
            if (!string.IsNullOrWhiteSpace(sequenceId))
                sequence = sequence with { SequenceId = sequenceId };
            var validation = ValidateSequence(sequence);
            if (validation.IsFailure)
                return Result<PhaseSequenceDefinition>.Failure($"Validation failed: {validation.Error}");
            if (cacheKey != null)
                _cache.Set(cacheKey, sequence);
            _logger.LogInformation($"Loaded phase sequence: {sequence.SequenceId ?? sequence.Name}");
            return Result<PhaseSequenceDefinition>.Success(sequence);
        }
        catch (JsonException exception)
        {
            return Result<PhaseSequenceDefinition>.Failure($"JSON parsing error: {exception.Message}");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
