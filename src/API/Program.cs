using API.Models;
using API.Logging;
using Core;
using Core.Abstractions.Persistence;
using Core.Caching;
using Core.Config;
using Core.Content;
using Core.Infrastructure.Persistence;
using Core.Math;
using Core.Events;
using Core.Combat;
using Core.Combat.Activation;
using Core.Combat.Intents;
using Core.Combat.Modifiers;
using Core.Combat.Gambits;
using Core.Combat.TurnPhase;
using Core.Combat.TurnOrder;
using Core.Resources;
using Core.Damage;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Core.Run.Replay;
using Core.Run.Events;
using Core.StatusEffects;
using Core.Entity.Definitions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using API.Contracts;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The supplied value is invalid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal);

        var result = new BadRequestObjectResult(
            ApiProblemDetailsFactory.CreateValidation(context.HttpContext, errors));
        result.ContentTypes.Add("application/problem+json");
        return result;
    };
});

// Configure config reload settings (security flag)
var allowConfigReload = builder.Configuration.GetValue<bool>("AllowConfigReload", false);
builder.Services.AddSingleton(new ConfigReloadSettings { Enabled = allowConfigReload });

// Register EventBus first (singleton) - must be registered before other services that depend on it
builder.Services.AddSingleton<IEventBus, EventBus>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("EventBus"));
    // IEventStore is registered later but resolved lazily; use a factory lambda to avoid ordering issues
    return new EventBus(logger, sp.GetService<IEventStore>());
});

// Register Core services with DI
builder.Services.AddSingleton<Core.Logging.ILogger>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new CoreLoggerAdapter(loggerFactory.CreateLogger("Core"));
});

builder.Services.AddSingleton<IConfigManager, ConfigManager>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ConfigManager"));
    var eventBus = sp.GetRequiredService<IEventBus>();
    var manager = new ConfigManager(logger, eventBus: eventBus);
    manager.SetValidatorFactory(() => sp.GetRequiredService<ConfigValidator>());
    return manager;
});
// ConfigValidator depends on IConfigManager, so it must be resolved lazily by ConfigManager.
builder.Services.AddSingleton<ConfigValidator>();

builder.Services.AddSingleton<ResourceProviderFactory>();
builder.Services.AddSingleton<IResourceLoader, ResourceLoader>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ResourceLoader"));
    var providerFactory = sp.GetRequiredService<ResourceProviderFactory>();
    return new ResourceLoader(logger, providerFactory);
});

// Register DefinitionPersister for CRUD operations on resource definitions
builder.Services.AddSingleton<IDefinitionPersister, DefinitionPersister>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("DefinitionPersister"));
    return new DefinitionPersister(configManager, logger);
});

builder.Services.AddSingleton<FormulaLoader>(sp =>
{
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("FormulaLoader"));
    return new FormulaLoader(resourceLoader, logger);
});
builder.Services.AddSingleton<IMathEngine, MathEngine>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var formulaLoader = sp.GetRequiredService<FormulaLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("MathEngine"));
    var eventBus = sp.GetRequiredService<IEventBus>();
    return new MathEngine(configManager, formulaLoader, logger, eventBus);
});
builder.Services.AddSingleton<IRuntimeFormulaEvaluator, RuntimeFormulaEvaluator>(sp =>
{
    var mathEngine = sp.GetRequiredService<IMathEngine>();
    var expressionEvaluator = sp.GetRequiredService<IExpressionEvaluator>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("RuntimeFormulaEvaluator"));
    return new RuntimeFormulaEvaluator(mathEngine, expressionEvaluator, logger);
});

// Register ResourceRegenerationProcessor
builder.Services.AddSingleton<IResourceRegenerationProcessor, ResourceRegenerationProcessor>(sp =>
{
    var mathEngine = sp.GetRequiredService<IMathEngine>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ResourceRegenerationProcessor"));
    var eventBus = sp.GetRequiredService<IEventBus>();
    return new ResourceRegenerationProcessor(mathEngine, logger, eventBus);
});

// Register ResourceManager
builder.Services.AddSingleton<IResourceManager, ResourceManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ResourceManager"));
    var regenerationProcessor = sp.GetRequiredService<IResourceRegenerationProcessor>();
    return new ResourceManager(configManager, resourceLoader, logger, regenerationProcessor);
});

// Register CacheRegistry (singleton for centralized cache management)
builder.Services.AddSingleton(CacheRegistry.Instance);

// Register EntityDefinitionLoader
builder.Services.AddSingleton<EntityDefinitionLoader>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var persister = sp.GetRequiredService<IDefinitionPersister>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("EntityDefinitionLoader"));
    var loader = new EntityDefinitionLoader(configManager, resourceLoader, logger, persister: persister);
    
    // Register with CacheRegistry
    var registry = sp.GetRequiredService<CacheRegistry>();
    registry.Register(loader);
    
    return loader;
});

// Register PhaseSequenceLoader
builder.Services.AddSingleton<PhaseSequenceLoader>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("PhaseSequenceLoader"));
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var loader = new PhaseSequenceLoader(logger, configManager, resourceLoader);
    
    // Register with CacheRegistry
    var registry = sp.GetRequiredService<CacheRegistry>();
    registry.Register(loader);
    
    return loader;
});

// Register StatusEffectManager
builder.Services.AddSingleton<IStatusEffectManager, StatusEffectManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var resourceManager = sp.GetRequiredService<IResourceManager>();
    var formulaEvaluator = sp.GetRequiredService<IRuntimeFormulaEvaluator>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    var persister = sp.GetRequiredService<IDefinitionPersister>();
    return new StatusEffectManager(configManager, resourceLoader, resourceManager, formulaEvaluator, eventBus, persister);
});

// Register ActionManager
builder.Services.AddSingleton<IActionManager, ActionManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var persister = sp.GetRequiredService<IDefinitionPersister>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ActionManager"));
    return new ActionManager(configManager, resourceLoader, logger, persister);
});

// Register ScriptModifierManager
builder.Services.AddSingleton<IScriptModifierManager>(sp => new ScriptModifierManager(
    sp.GetRequiredService<IConfigManager>(),
    sp.GetRequiredService<IResourceLoader>(),
    sp.GetRequiredService<IRuntimeFormulaEvaluator>(),
    sp.GetRequiredService<IEventBus>()));

// Register GambitEngine
builder.Services.AddSingleton<IGambitEngine>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    var persister = sp.GetRequiredService<IDefinitionPersister>();
    return new GambitEngine(configManager, resourceLoader, eventBus, persister);
});
builder.Services.AddSingleton<IIntentResolver, IntentResolver>();

// Register persistence services
var eventStorePath = builder.Configuration.GetValue<string>("Persistence:EventStorePath") ?? "data/events";
var runStatePath = builder.Configuration.GetValue<string>("Persistence:RunStatePath") ?? "data/runs";
var contentStorePath = builder.Configuration.GetValue<string>("Persistence:ContentStorePath") ?? "data/content";
var maxRetainedSnapshots = builder.Configuration.GetValue<int>("Persistence:Snapshots:MaxRetainedSnapshots", 100);

builder.Services.AddSingleton<IEventStore>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("JsonFileEventStore"));
    return new JsonFileEventStore(eventStorePath, logger);
});

builder.Services.AddSingleton<IRunStateRepository>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("VersionedRunStateRepository"));
    return new VersionedRunStateRepository(runStatePath, logger, maxRetainedSnapshots);
});

// Register Run content and manager
builder.Services.AddSingleton<IContentManifestProvider, ContentManifestProvider>();
builder.Services.AddSingleton<IContentPublicationService>(sp => new ContentPublicationService(
    contentStorePath,
    sp.GetRequiredService<IContentManifestProvider>(),
    sp.GetRequiredService<IResourceLoader>()));
builder.Services.AddSingleton<ICardContentCatalog, CardContentCatalog>();
builder.Services.AddSingleton<ICardPoolResolver, CardPoolResolver>();
builder.Services.AddSingleton<IResourceCatalog<RelicDefinition>>(sp =>
    new ResourceCatalog<RelicDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "relics",
        definition => definition.RelicId));
builder.Services.AddSingleton<IResourceCatalog<CardUpgradeDefinition>>(sp =>
    new ResourceCatalog<CardUpgradeDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "card-upgrades",
        definition => definition.UpgradeId));
builder.Services.AddSingleton<IResourceCatalog<GameModeDefinition>>(sp =>
    new ResourceCatalog<GameModeDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "modes",
        definition => definition.ModeId));
builder.Services.AddSingleton<IResourceCatalog<DailyChallengeDefinition>>(sp =>
    new ResourceCatalog<DailyChallengeDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "daily-challenges",
        definition => definition.ChallengeId));
builder.Services.AddSingleton<RunManager>(sp => new RunManager(
    sp.GetRequiredService<IConfigManager>(),
    sp.GetRequiredService<IResourceLoader>(),
    sp.GetRequiredService<ICardPoolResolver>(),
    sp.GetRequiredService<ICardContentCatalog>(),
    sp.GetRequiredService<IScriptModifierManager>(),
    sp.GetRequiredService<IEventBus>(),
    sp.GetRequiredService<IRunStateRepository>(),
    sp.GetRequiredService<IContentManifestProvider>(),
    sp.GetRequiredService<IResourceCatalog<RelicDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<CardUpgradeDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<GameModeDefinition>>()));
builder.Services.AddSingleton<IRunManager>(sp => sp.GetRequiredService<RunManager>());
builder.Services.AddSingleton<IRunCommandProcessor>(sp => sp.GetRequiredService<RunManager>());
builder.Services.AddSingleton<IRunReplayService, RunSemanticReplayService>();
builder.Services.AddSingleton<IRunEventProjectionReader, RunEventProjectionReader>();
builder.Services.AddSingleton<Core.Meta.IPlayerProfileProjectionReader, Core.Meta.PlayerProfileProjectionReader>();
builder.Services.AddSingleton<Core.Run.Branching.IRunBranchService>(sp =>
    new Core.Run.Branching.RunBranchService(
        (Core.Abstractions.Persistence.IRunCheckpointRepository)sp.GetRequiredService<IRunStateRepository>()));
builder.Services.AddSingleton<Core.Run.Branching.IRunSimulationService, Core.Run.Branching.RunSimulationService>();

// Register damage pipeline
builder.Services.AddSingleton<PipelineConfigLoader>(sp =>
{
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("PipelineConfigLoader"));
    return new PipelineConfigLoader(resourceLoader, logger);
});

builder.Services.AddSingleton<IPipelineManager, PipelineManager>(sp =>
{
    var loader = sp.GetRequiredService<PipelineConfigLoader>();
    var configManager = sp.GetRequiredService<IConfigManager>();
    var mathEngine = sp.GetRequiredService<IMathEngine>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("PipelineManager"));
    return new PipelineManager(loader, configManager, mathEngine, eventBus, logger);
});

// Register DamageCalculator
builder.Services.AddSingleton<IDamageCalculator, DamageCalculator>(sp =>
{
    var pipelineManager = sp.GetRequiredService<IPipelineManager>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    var statusEffectManager = sp.GetRequiredService<IStatusEffectManager>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("DamageCalculator"));
    return new DamageCalculator(pipelineManager, eventBus, logger, statusEffectManager);
});

// Register EffectResolver
builder.Services.AddSingleton<IEffectResolver, EffectResolver>(sp =>
{
    var damageCalculator = sp.GetRequiredService<IDamageCalculator>();
    var resourceManager = sp.GetRequiredService<IResourceManager>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("EffectResolver"));
    var formulaEvaluator = sp.GetRequiredService<IRuntimeFormulaEvaluator>();
    var statusEffectManager = sp.GetRequiredService<IStatusEffectManager>();
    var runManager = sp.GetRequiredService<IRunManager>();
    return new EffectResolver(damageCalculator, resourceManager, eventBus, logger, formulaEvaluator, randomProvider: null, statusEffectManager: statusEffectManager, runManager: runManager);
});

// Register CombatOptions
builder.Services.Configure<CombatOptions>(builder.Configuration.GetSection("Combat"));

// Register TurnOrderCalculator
builder.Services.AddSingleton<ITurnOrderCalculator>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("TurnOrderCalculator"));
    var combatOptions = sp.GetRequiredService<IOptions<CombatOptions>>();
    var turnOrderStrategy = combatOptions.Value.TurnOrderStrategy;
    
    return turnOrderStrategy.ToLowerInvariant() switch
    {
        "speed_based" => new SpeedBasedTurnOrderCalculator(logger),
        "initiative" => new InitiativeTurnOrderCalculator(logger),
        "atb" => new ATBTurnOrderCalculator(10f, logger),
        "conditional" => ConditionalTurnOrderCalculator.CreateHybridCalculator(logger),
        "fixed" => new FixedTurnOrderCalculator(logger),
        _ => new FixedTurnOrderCalculator(logger)
    };
});

// Register CombatSystem
builder.Services.AddSingleton<ICombatSystem, CombatSystem>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("CombatSystem"));
    var resourceManager = sp.GetRequiredService<IResourceManager>();
    var turnOrderCalculator = sp.GetRequiredService<ITurnOrderCalculator>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    var damageCalculator = sp.GetRequiredService<IDamageCalculator>();
    var statusEffectManager = sp.GetRequiredService<IStatusEffectManager>();
    var actionManager = sp.GetRequiredService<IActionManager>();
    var entityDefinitionLoader = sp.GetRequiredService<EntityDefinitionLoader>();
    var effectResolver = sp.GetRequiredService<IEffectResolver>();
    var actionCostEvaluator = sp.GetRequiredService<IActionCostEvaluator>();
    return new CombatSystem(logger, resourceManager, turnOrderCalculator, eventBus, damageCalculator, statusEffectManager, regenerationProcessor: null, actionManager: actionManager, entityDefinitionLoader: entityDefinitionLoader, effectResolver: effectResolver, actionCostEvaluator: actionCostEvaluator);
});

// Register CombatRunCoordinator
builder.Services.AddSingleton<ICombatRunCoordinator>(sp => new CombatRunCoordinator(
    sp.GetRequiredService<ICombatSystem>(),
    sp.GetRequiredService<IRunManager>(),
    sp.GetRequiredService<IActionManager>(),
    sp.GetRequiredService<IScriptModifierManager>()));

// Register CombatActivation services
builder.Services.AddSingleton<ICombatActivationRulesLoader, CombatActivationRulesLoader>();
builder.Services.AddSingleton<ICombatActivationCoordinator, CombatActivationCoordinator>();

// Register EntityFactory
builder.Services.AddSingleton<IEntityFactory, Core.Combat.EntityFactory>();

// Register ActionAffordabilityService
builder.Services.AddSingleton<IActionCostEvaluator, ActionCostEvaluator>();
builder.Services.AddSingleton<IActionAffordabilityService, ActionAffordabilityService>();

// Register ExpressionEvaluator
builder.Services.AddSingleton<IExpressionEvaluator, ExpressionEvaluator>();

// Register OperationMetadataProvider
builder.Services.AddSingleton<IOperationMetadataProvider, OperationMetadataProvider>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Development: Allow any origin for easier testing
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Production: Restrict to configured origins
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
            else
            {
                // Fallback: Secure by default, no wildcard CORS in production
                policy.WithOrigins("https://localhost")
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            }
        }
    });
});

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure Core library logging
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var coreLogger = new CoreLoggerAdapter(loggerFactory.CreateLogger("Core"));
Core.Logging.LoggerFactory.SetFactory(categoryName => 
    new CoreLoggerAdapter(loggerFactory.CreateLogger(categoryName)));

// Load status effect definitions
var statusEffectManager = app.Services.GetRequiredService<IStatusEffectManager>();
var loadResult = statusEffectManager.LoadStatusDefinitions("default");
var logger = loggerFactory.CreateLogger("Startup");
if (loadResult.IsSuccess)
{
    logger.LogInformation("Successfully loaded status effect definitions from config 'default'");
}
else
{
    logger.LogError("Failed to load status effect definitions: {Error}", loadResult.Error);
}

var scriptModifierManager = app.Services.GetRequiredService<IScriptModifierManager>();
var modifierLoadResult = scriptModifierManager.LoadDefinitions("default");
if (modifierLoadResult.IsSuccess)
{
    logger.LogInformation("Successfully loaded script modifier definitions from config 'default'");
}
else
{
    logger.LogWarning("Script modifier definitions were not loaded: {Error}", modifierLoadResult.Error);
}

var gambitEngine = app.Services.GetRequiredService<IGambitEngine>();
var gambitLoadResult = gambitEngine.LoadDefinitions("default");
if (gambitLoadResult.IsSuccess)
{
    logger.LogInformation("Successfully loaded gambit definitions from config 'default'");
}
else
{
    logger.LogWarning("Gambit definitions were not loaded: {Error}", gambitLoadResult.Error);
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "HeroScript API v1");
        options.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

// Enable HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors();
app.UseMiddleware<API.Middleware.CorrelationIdMiddleware>();
app.UseMiddleware<API.Middleware.ApiExceptionMiddleware>();
app.UseMiddleware<API.Middleware.AdminKeyMiddleware>();
app.MapControllers();

app.Run();
