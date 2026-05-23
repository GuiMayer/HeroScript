using API.Models;
using API.Logging;
using Core;
using Core.Config;
using Core.Math;
using Core.Events;
using Core.Combat;
using Core.Combat.Modifiers;
using Core.Combat.Gambits;
using Core.Resources;
using Core.Damage;
using Core.Effects;
using Core.Run;
using Core.StatusEffects;
using Core.Entity.Definitions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Configure config reload settings (security flag)
var allowConfigReload = builder.Configuration.GetValue<bool>("AllowConfigReload", false);
builder.Services.AddSingleton(new ConfigReloadSettings { Enabled = allowConfigReload });

// Register EventBus first (singleton) - must be registered before other services that depend on it
builder.Services.AddSingleton<IEventBus, EventBus>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("EventBus"));
    return new EventBus(logger);
});

// Register Core services with DI
builder.Services.AddSingleton<Core.Logging.ILogger>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new CoreLoggerAdapter(loggerFactory.CreateLogger("Core"));
});

builder.Services.AddSingleton<ConfigValidator>();
builder.Services.AddSingleton<IConfigManager, ConfigManager>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ConfigManager"));
    var validator = sp.GetRequiredService<ConfigValidator>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    return new ConfigManager(logger, validator, eventBus);
});

builder.Services.AddSingleton<ResourceProviderFactory>();
builder.Services.AddSingleton<IResourceLoader, ResourceLoader>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ResourceLoader"));
    var providerFactory = sp.GetRequiredService<ResourceProviderFactory>();
    return new ResourceLoader(logger, providerFactory);
});

builder.Services.AddSingleton<FormulaLoader>();
builder.Services.AddSingleton<IMathEngine, MathEngine>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var formulaLoader = sp.GetRequiredService<FormulaLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("MathEngine"));
    var eventBus = sp.GetRequiredService<IEventBus>();
    return new MathEngine(configManager, formulaLoader, logger, eventBus);
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

// Register EntityDefinitionLoader
builder.Services.AddSingleton<EntityDefinitionLoader>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("EntityDefinitionLoader"));
    var basePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "configs", "default", "Entities");
    return new EntityDefinitionLoader(basePath, logger);
});

// Register StatusEffectManager
builder.Services.AddSingleton<IStatusEffectManager, StatusEffectManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceManager = sp.GetRequiredService<IResourceManager>();
    var mathEngine = sp.GetRequiredService<IMathEngine>();
    return new StatusEffectManager(configManager, resourceManager, mathEngine);
});

// Register ActionManager
builder.Services.AddSingleton<IActionManager, ActionManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ActionManager"));
    return new ActionManager(configManager, resourceLoader, logger);
});

// Register ScriptModifierManager
builder.Services.AddSingleton<IScriptModifierManager, ScriptModifierManager>();

// Register GambitEngine
builder.Services.AddSingleton<IGambitEngine>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    return new GambitEngine(configManager, resourceLoader);
});

// Register RunManager
builder.Services.AddSingleton<IRunManager, RunManager>();

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
    var statusEffectManager = sp.GetRequiredService<IStatusEffectManager>();
    var runManager = sp.GetRequiredService<IRunManager>();
    return new EffectResolver(damageCalculator, resourceManager, eventBus, logger, randomProvider: null, statusEffectManager: statusEffectManager, runManager: runManager);
});

// Register CombatSystem
builder.Services.AddSingleton<ICombatSystem, CombatSystem>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("CombatSystem"));
    var resourceManager = sp.GetRequiredService<IResourceManager>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    var damageCalculator = sp.GetRequiredService<IDamageCalculator>();
    var statusEffectManager = sp.GetRequiredService<IStatusEffectManager>();
    var actionManager = sp.GetRequiredService<IActionManager>();
    var entityDefinitionLoader = sp.GetRequiredService<EntityDefinitionLoader>();
    var effectResolver = sp.GetRequiredService<IEffectResolver>();
    return new CombatSystem(logger, resourceManager, eventBus, damageCalculator, statusEffectManager, actionManager: actionManager, entityDefinitionLoader: entityDefinitionLoader, effectResolver: effectResolver);
});

// Register EntityFactory
builder.Services.AddSingleton<IEntityFactory, Core.Combat.EntityFactory>();

// Register ActionAffordabilityService
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
                // Fallback: No CORS if no origins configured
                policy.AllowAnyOrigin()
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
app.MapControllers();

app.Run();
