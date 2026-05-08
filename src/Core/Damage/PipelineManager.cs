using System;
using System.Collections.Generic;
using System.Linq;
using Core.Events;
using Core.Logging;
using Core.Math;

namespace Core.Damage;

/// <summary>
/// Gerenciador do pipeline de dano com cache de processadores
/// </summary>
public class PipelineManager : IPipelineManager
{
    private readonly PipelineConfigLoader _loader;
    private readonly IMathEngine _mathEngine;
    private readonly IEventBus _eventBus;
    private readonly ILogger _logger;
    
    // Cache de configuração e processadores
    private PipelineConfiguration? _cachedConfig;
    private List<GenericBucketProcessor>? _cachedProcessors;
    private readonly object _cacheLock = new();

    public PipelineManager(
        PipelineConfigLoader loader,
        IMathEngine mathEngine,
        IEventBus eventBus,
        ILogger logger)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _mathEngine = mathEngine ?? throw new ArgumentNullException(nameof(mathEngine));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executa o pipeline completo de dano
    /// </summary>
    public DamageContext ExecutePipeline(DamageContext initialContext)
    {
        var processors = GetProcessors();
        var context = initialContext;
        
        _logger.LogDebug($"Pipeline start: {context.CurrentDamage:F2} damage, {context.Tags.Count} tags, {context.Modifiers.Count} modifiers");
        
        foreach (var processor in processors)
        {
            context = processor.Process(context);
        }
        
        _logger.LogDebug($"Pipeline end: {context.CurrentDamage:F2} damage (delta: {context.CurrentDamage - initialContext.CurrentDamage:+F2;-F2;0})");
        
        return context;
    }

    /// <summary>
    /// Recarrega configuração do pipeline
    /// </summary>
    public void ReloadConfiguration(IEnumerable<string> configChain)
    {
        lock (_cacheLock)
        {
            _logger.LogInfo("Reloading pipeline configuration");
            
            try
            {
                _cachedConfig = _loader.LoadPipeline(configChain);
                _cachedProcessors = InstantiateProcessors(_cachedConfig);
                
                // Emitir evento de sucesso
                _eventBus.Publish(new Events.PipelineReloadedEvent
                {
                    BucketCount = _cachedConfig.Buckets.Count,
                    BucketIds = _cachedConfig.Buckets.Select(b => b.BucketId).ToList(),
                    Reason = "Manual reload",
                    Success = true
                });
                
                _logger.LogInfo($"Pipeline reloaded: {_cachedConfig.Buckets.Count} buckets");
            }
            catch (Exception ex)
            {
                // Emitir evento de falha
                _eventBus.Publish(new Events.PipelineReloadedEvent
                {
                    BucketCount = 0,
                    BucketIds = new List<string>(),
                    Reason = "Manual reload",
                    Success = false,
                    ErrorMessage = ex.Message
                });
                
                _logger.LogError($"Pipeline reload failed: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Obtém configuração atual do pipeline
    /// </summary>
    public PipelineConfiguration GetCurrentConfiguration()
    {
        lock (_cacheLock)
        {
            if (_cachedConfig == null)
            {
                ReloadConfiguration(GetDefaultConfigChain());
            }
            return _cachedConfig!;
        }
    }

    private List<GenericBucketProcessor> GetProcessors()
    {
        lock (_cacheLock)
        {
            if (_cachedProcessors == null)
            {
                // Lazy load na primeira execução
                ReloadConfiguration(GetDefaultConfigChain());
            }
            return _cachedProcessors!;
        }
    }

    private List<GenericBucketProcessor> InstantiateProcessors(PipelineConfiguration config)
    {
        _logger.LogDebug($"Instantiating {config.Buckets.Count} bucket processors");
        
        return config.Buckets
            .Select(b => new GenericBucketProcessor(b, _mathEngine, _eventBus, _logger))
            .ToList();
    }

    private IEnumerable<string> GetDefaultConfigChain()
    {
        // TODO: Obter de ConfigManager quando integrado
        // Por enquanto, usa apenas "default"
        return new[] { "default" };
    }
}
