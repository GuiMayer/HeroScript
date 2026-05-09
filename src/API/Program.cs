using API.Models;
using API.Logging;
using Core;
using Core.Config;
using Core.Math;
using Core.Events;
using Core.Combat;
using Core.Resources;

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
    var eventBus = sp.GetRequiredService<IEventBus>();
    return new MathEngine(configManager, formulaLoader, eventBus);
});

// Register ResourceManager
builder.Services.AddSingleton<IResourceManager, ResourceManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ResourceManager"));
    return new ResourceManager(configManager, resourceLoader, logger);
});

// Register ActionManager
builder.Services.AddSingleton<ActionManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ActionManager"));
    return new ActionManager(configManager, resourceLoader, logger);
});

// Register CombatSystem
builder.Services.AddSingleton<ICombatSystem, CombatSystem>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("CombatSystem"));
    var resourceManager = sp.GetRequiredService<IResourceManager>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    return new CombatSystem(logger, resourceManager, eventBus);
});

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
