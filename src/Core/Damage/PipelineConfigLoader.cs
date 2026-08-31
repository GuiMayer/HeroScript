using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Config;
using Core.Content;
using Core.Logging;

namespace Core.Damage;

/// <summary>
/// Carrega configuração do pipeline de dano de JSON usando ResourceLoader
/// </summary>
public class PipelineConfigLoader
{
    private readonly IResourceLoader _resourceLoader;
    private readonly ILogger _logger;

    public PipelineConfigLoader(IResourceLoader resourceLoader, ILogger logger)
    {
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Carrega pipeline da cadeia de configuração
    /// </summary>
    public PipelineConfiguration LoadPipeline(IEnumerable<string> configChain)
    {
        _logger.LogInformation("Loading damage pipeline configuration");

        var rawData = _resourceLoader.LoadResource(
            "Pipelines/DamagePipeline.json",
            configChain,
            strictMode: false
        );

        var config = DeserializePipeline(rawData);
        config.Validate();

        config = config with
        {
            Buckets = config.Buckets.OrderBy(b => b.Order).ToList()
        };

        _logger.LogInformation($"Loaded pipeline '{config.ConfigName}' with {config.Buckets.Count} buckets");
        return config;
    }

    public PipelineConfiguration LoadPipeline(ContentRuntime runtime, string? pipelineId = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var rawData = runtime.GetDefinitions("pipelines")
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var config = DeserializePipeline(rawData) with
        {
            ConfigName = string.IsNullOrWhiteSpace(pipelineId) ? "default" : pipelineId
        };
        config.Validate();
        return config with { Buckets = config.Buckets.OrderBy(bucket => bucket.Order).ToArray() };
    }

    private PipelineConfiguration DeserializePipeline(Dictionary<string, JsonElement> rawData)
    {
        // Assumindo que o JSON tem estrutura: { "buckets": [...] }
        if (!rawData.TryGetValue("buckets", out var bucketsElement))
        {
            throw new InvalidOperationException("Pipeline JSON must have 'buckets' array");
        }

        var buckets = JsonSerializer.Deserialize<List<BucketDefinition>>(
            bucketsElement.GetRawText(),
            CreateJsonOptions()
        );

        return new PipelineConfiguration
        {
            ConfigName = "default",
            Buckets = buckets ?? new List<BucketDefinition>()
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

}
