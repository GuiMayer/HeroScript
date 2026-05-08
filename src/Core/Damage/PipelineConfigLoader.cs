using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Core.Config;
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
        try
        {
            _logger.LogInfo("Loading damage pipeline configuration");

            // 1. Carregar JSON via ResourceLoader
            var rawData = _resourceLoader.LoadResource(
                "Pipelines/DamagePipeline.json",
                configChain,
                strictMode: false
            );

            // 2. Deserializar
            var config = DeserializePipeline(rawData);

            // 3. Validar
            var validation = config.Validate();
            if (!validation.IsSuccess)
            {
                _logger.LogError($"Invalid pipeline config: {validation.Error}");
                _logger.LogWarning("Falling back to hardcoded configuration");
                return GetFallbackConfiguration();
            }

            // 4. Ordenar buckets por Order
            config = config with 
            { 
                Buckets = config.Buckets.OrderBy(b => b.Order).ToList() 
            };

            _logger.LogInfo($"Loaded pipeline '{config.ConfigName}' with {config.Buckets.Count} buckets");
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to load pipeline: {ex.Message}");
            _logger.LogWarning("Falling back to hardcoded configuration");
            return GetFallbackConfiguration();
        }
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
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        return new PipelineConfiguration
        {
            ConfigName = "default",
            Buckets = buckets ?? new List<BucketDefinition>()
        };
    }

    /// <summary>
    /// Retorna configuração hardcoded mínima (fallback)
    /// </summary>
    private PipelineConfiguration GetFallbackConfiguration()
    {
        _logger.LogInfo("Using fallback pipeline configuration (base bucket only)");

        return new PipelineConfiguration
        {
            ConfigName = "fallback",
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "base",
                    Order = 1,
                    FilterConditions = new List<FilterCondition>(),
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation
                        {
                            Type = OperationType.ADD_FLAT,
                            Source = "modifier:base_damage",
                            Parameters = new Dictionary<string, object>()
                        }
                    },
                    EmitEvents = true
                }
            }
        };
    }
}
