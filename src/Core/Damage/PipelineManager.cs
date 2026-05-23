using System;
using System.Collections.Generic;
using System.Linq;
using Core.Config;
using Core.Events;
using Core.Logging;
using Core.Math;

namespace Core.Damage;

/// <summary>
/// Gerenciador do pipeline de dano com cache de processadores
/// </summary>
public class PipelineManager : IPipelineManager
{
    private readonly PipelineConfigLoader? _loader;
    private readonly IConfigManager? _configManager;
    private readonly IMathEngine _mathEngine;
    private readonly IEventBus _eventBus;
    private readonly ILogger _logger;
    private readonly IRandomProvider _randomProvider;
    
    // Cache de configuração e processadores
    private PipelineConfiguration? _cachedConfig;
    private List<GenericBucketProcessor>? _cachedProcessors;
    private readonly object _cacheLock = new();

    public PipelineManager(
        PipelineConfigLoader loader,
        IConfigManager configManager,
        IMathEngine mathEngine,
        IEventBus eventBus,
        ILogger logger,
        IRandomProvider? randomProvider = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _mathEngine = mathEngine ?? throw new ArgumentNullException(nameof(mathEngine));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _randomProvider = randomProvider ?? new DefaultRandomProvider();
    }

    // Construtor privado para factory method
    private PipelineManager(
        PipelineConfiguration config,
        IMathEngine mathEngine,
        IEventBus eventBus,
        ILogger logger,
        IRandomProvider randomProvider,
        bool isFactoryCall)
    {
        _loader = null;
        _configManager = null;
        _mathEngine = mathEngine;
        _eventBus = eventBus;
        _logger = logger;
        _randomProvider = randomProvider;
        _cachedConfig = config;
        _cachedProcessors = InstantiateProcessors(config);
    }

    /// <summary>
    /// Factory method para criar PipelineManager com configuração direta (útil para testes)
    /// </summary>
    public static PipelineManager CreateWithConfig(
        PipelineConfiguration config,
        IMathEngine mathEngine,
        IEventBus eventBus,
        ILogger logger,
        IRandomProvider? randomProvider = null)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (mathEngine == null) throw new ArgumentNullException(nameof(mathEngine));
        if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        
        return new PipelineManager(
            config,
            mathEngine,
            eventBus,
            logger,
            randomProvider ?? new DefaultRandomProvider(),
            isFactoryCall: true);
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
            _logger.LogInformation("Reloading pipeline configuration");
            
            try
            {
                if (_loader == null)
                    throw new InvalidOperationException("Pipeline manager was created with a fixed configuration and cannot reload from configs.");

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
                
                _logger.LogInformation($"Pipeline reloaded: {_cachedConfig.Buckets.Count} buckets");
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
                ReloadConfiguration(GetConfiguredChain());
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
                ReloadConfiguration(GetConfiguredChain());
            }
            return _cachedProcessors!;
        }
    }

    private List<GenericBucketProcessor> InstantiateProcessors(PipelineConfiguration config)
    {
        _logger.LogDebug($"Instantiating {config.Buckets.Count} bucket processors");
        
        return config.Buckets
            .Select(b => new GenericBucketProcessor(b, _mathEngine, _eventBus, _logger, _randomProvider))
            .ToList();
    }

    private IEnumerable<string> GetConfiguredChain()
    {
        if (_configManager == null)
            throw new InvalidOperationException("Pipeline manager was created with a fixed configuration and cannot lazy-load from configs.");

        return _configManager.ResolveInheritanceChain(_configManager.DefaultConfig);
    }
}
