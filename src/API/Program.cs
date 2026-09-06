using API.Models;
using API.Logging;
using Core;
using Core.Abstractions.Persistence;
using Core.Caching;
using Core.Calculations;
using Core.Config;
using Core.Content;
using Core.Infrastructure.Persistence;
using Core.Math;
using Core.Events;
using Core.Combat;
using Core.Combat.Flow;
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
using Core.Run.Sandbox;
using Core.StatusEffects;
using Core.Entity.Definitions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using API.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

// Add services to the container
builder.Services.AddControllers(options =>
    options.Filters.Add<ApiProblemDetailsResultFilter>());
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
builder.Services.AddSingleton<IGameEventContextAccessor, GameEventContextAccessor>();
builder.Services.AddSingleton<IEventBus, EventBus>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("EventBus"));
    return new EventBus(
        logger,
        sp.GetService<IEventStore>(),
        sp.GetRequiredService<IGameEventContextAccessor>());
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
builder.Services.AddSingleton<CacheRegistry>();
builder.Services.AddSingleton<ICacheCoordinator>(sp => sp.GetRequiredService<CacheRegistry>());
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
    return new RuntimeFormulaEvaluator(
        mathEngine,
        expressionEvaluator,
        logger,
        sp.GetRequiredService<IContentRuntimeResolver>());
});

// Register ResourceRegenerationProcessor
builder.Services.AddSingleton<IResourceRegenerationProcessor, ResourceRegenerationProcessor>(sp =>
{
    return new ResourceRegenerationProcessor(sp.GetRequiredService<IRuntimeFormulaEvaluator>());
});

// Register ResourceManager
builder.Services.AddSingleton<IResourceManager, ResourceManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ResourceManager"));
    var regenerationProcessor = sp.GetRequiredService<IResourceRegenerationProcessor>();
    return new ResourceManager(
        configManager,
        resourceLoader,
        logger,
        regenerationProcessor,
        sp.GetRequiredService<IContentRuntimeResolver>());
});

// Register EntityDefinitionLoader
builder.Services.AddSingleton<EntityDefinitionLoader>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var persister = sp.GetRequiredService<IDefinitionPersister>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("EntityDefinitionLoader"));
    var loader = new EntityDefinitionLoader(
        configManager,
        resourceLoader,
        logger,
        persister: persister,
        contentRuntimes: sp.GetRequiredService<IContentRuntimeResolver>());
    
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
    return new StatusEffectManager(
        configManager,
        resourceLoader,
        resourceManager,
        formulaEvaluator,
        eventBus,
        persister,
        sp.GetRequiredService<IContentRuntimeResolver>());
});

// Register ActionManager
builder.Services.AddSingleton<IActionManager, ActionManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var persister = sp.GetRequiredService<IDefinitionPersister>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ActionManager"));
    return new ActionManager(
        configManager,
        resourceLoader,
        logger,
        persister,
        sp.GetRequiredService<IContentRuntimeResolver>());
});

// Register ScriptModifierManager
builder.Services.AddSingleton<IScriptModifierManager>(sp => new ScriptModifierManager(
    sp.GetRequiredService<IConfigManager>(),
    sp.GetRequiredService<IResourceLoader>(),
    sp.GetRequiredService<IRuntimeFormulaEvaluator>(),
    sp.GetRequiredService<IEventBus>(),
    sp.GetRequiredService<IContentRuntimeResolver>()));
builder.Services.AddSingleton<IPinnedContentCatalog<StatusEffectDefinition>>(sp =>
    new PinnedContentCatalog<StatusEffectDefinition>(
        sp.GetRequiredService<IContentRuntimeResolver>(),
        "status-effects",
        (id, definition) => definition with
        {
            StatusId = string.IsNullOrWhiteSpace(definition.StatusId) ? id : definition.StatusId
        }));
builder.Services.AddSingleton<IPinnedContentCatalog<ScriptModifierDefinition>>(sp =>
    new PinnedContentCatalog<ScriptModifierDefinition>(
        sp.GetRequiredService<IContentRuntimeResolver>(),
        "modifiers",
        (id, definition) => definition with
        {
            ModifierId = string.IsNullOrWhiteSpace(definition.ModifierId) ? id : definition.ModifierId
        }));

// Register GambitEngine
builder.Services.AddSingleton<IGambitEngine>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    var persister = sp.GetRequiredService<IDefinitionPersister>();
    return new GambitEngine(
        configManager,
        resourceLoader,
        eventBus,
        persister,
        sp.GetRequiredService<IContentRuntimeResolver>(),
        sp.GetRequiredService<IRuntimeFormulaEvaluator>());
});
builder.Services.AddSingleton<IIntentResolver, IntentResolver>();
builder.Services.AddSingleton<ICombatStatusLifecycle>(sp => new CombatStatusLifecycle(
    sp.GetRequiredService<IEffectTriggerExecutor>()));
builder.Services.AddSingleton<ICombatRelicLifecycle>(sp => new CombatRelicLifecycle(
    sp.GetRequiredService<IEffectTriggerExecutor>()));

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
builder.Services.AddSingleton<IContentGraphValidator, ContentGraphValidator>();
builder.Services.AddSingleton<IContentPublicationService>(sp => new ContentPublicationService(
    contentStorePath,
    sp.GetRequiredService<IContentManifestProvider>(),
    sp.GetRequiredService<IResourceLoader>(),
    sp.GetRequiredService<IContentGraphValidator>()));
builder.Services.AddSingleton<IContentRuntimeResolver, ContentRuntimeResolver>();
builder.Services.AddSingleton<IContentReloadService, ContentReloadService>();
builder.Services.AddSingleton<ICardContentCatalog, CardContentCatalog>();
builder.Services.AddSingleton<ICardPoolResolver, CardPoolResolver>();
builder.Services.AddSingleton<ICardContentCompiler, CardContentCompiler>();
builder.Services.AddSingleton<IEffectiveCardResolver, EffectiveCardResolver>();
builder.Services.AddSingleton<ICalculationEngine, CalculationEngine>();
builder.Services.AddSingleton<IImmutableEffectProcessor, ImmutableEffectProcessor>();
builder.Services.AddSingleton<ICombatResourceLifecycle, CombatResourceLifecycle>();
builder.Services.AddSingleton<IEffectTriggerExecutor>(sp => new EffectTriggerExecutor(
    sp.GetRequiredService<IRuntimeFormulaEvaluator>(),
    sp.GetRequiredService<IImmutableEffectProcessor>(),
    sp.GetRequiredService<IContentRuntimeResolver>(),
    sp.GetRequiredService<ICalculationEngine>(),
    sp.GetRequiredService<ICalculationInfluenceProvider>()));
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
builder.Services.AddSingleton<IResourceCatalog<CalculationPipelineDefinition>>(sp =>
    new ResourceCatalog<CalculationPipelineDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "calculation-pipelines",
        definition => definition.PipelineId));
builder.Services.AddSingleton<IResourceCatalog<GameModeDefinition>>(sp =>
    new ResourceCatalog<GameModeDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "modes",
        definition => definition.ModeId));
builder.Services.AddSingleton<IResourceCatalog<FlowRulesDefinition>>(sp =>
    new ResourceCatalog<FlowRulesDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "flow-rules",
        definition => definition.FlowRulesId));
builder.Services.AddSingleton<IResourceCatalog<CombatRulesDefinition>>(sp =>
    new ResourceCatalog<CombatRulesDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "combat-rules",
        definition => definition.CombatRulesId));
builder.Services.AddSingleton<IResourceCatalog<ReplayPolicyDefinition>>(sp =>
    new ResourceCatalog<ReplayPolicyDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "replay-policies",
        definition => definition.ReplayPolicyId));
builder.Services.AddSingleton<IResourceCatalog<TimelinePolicyDefinition>>(sp =>
    new ResourceCatalog<TimelinePolicyDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "timeline-policies",
        definition => definition.TimelinePolicyId));
builder.Services.AddSingleton<IResourceCatalog<ContentBindingPolicyDefinition>>(sp =>
    new ResourceCatalog<ContentBindingPolicyDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "content-binding-policies",
        definition => definition.ContentBindingPolicyId));
builder.Services.AddSingleton<IResourceCatalog<CapabilityPolicyDefinition>>(sp =>
    new ResourceCatalog<CapabilityPolicyDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "capability-policies",
        definition => definition.CapabilityPolicyId));
builder.Services.AddSingleton<IResourceCatalog<EnemyPoolDefinition>>(sp =>
    new ResourceCatalog<EnemyPoolDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "enemy-pools",
        definition => definition.EnemyPoolId));
builder.Services.AddSingleton<IGameModeResolver>(sp => new GameModeResolver(
    sp.GetRequiredService<IResourceCatalog<GameModeDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<FlowRulesDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<CombatRulesDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<ReplayPolicyDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<TimelinePolicyDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<ContentBindingPolicyDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<CapabilityPolicyDefinition>>(),
    sp.GetRequiredService<ICardPoolResolver>(),
    sp.GetRequiredService<IResourceCatalog<EnemyPoolDefinition>>(),
    sp.GetRequiredService<IContentRuntimeResolver>(),
    new CoreLoggerAdapter(sp.GetRequiredService<ILoggerFactory>().CreateLogger("GameModeResolver"))));
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
    sp.GetRequiredService<IPinnedContentCatalog<ScriptModifierDefinition>>(),
    sp.GetRequiredService<IEventBus>(),
    sp.GetRequiredService<IRunStateRepository>(),
    sp.GetRequiredService<IContentManifestProvider>(),
    sp.GetRequiredService<IResourceCatalog<RelicDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<CardUpgradeDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<GameModeDefinition>>(),
    sp.GetRequiredService<IGameModeResolver>(),
    sp.GetRequiredService<IContentPublicationService>(),
    sp.GetRequiredService<IContentRuntimeResolver>(),
    sp.GetRequiredService<IResourceManager>()));
builder.Services.AddSingleton<IRunManager>(sp => sp.GetRequiredService<RunManager>());
builder.Services.AddSingleton<IRunCommandProcessor>(sp => sp.GetRequiredService<RunManager>());
builder.Services.AddSingleton<IGameplayCommandGateway, GameplayCommandGateway>();
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
    return new PipelineManager(
        loader,
        configManager,
        mathEngine,
        eventBus,
        logger,
        contentRuntimes: sp.GetRequiredService<IContentRuntimeResolver>());
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
    var calculator = new TurnOrderCalculatorFactory(logger)
        .CreateCalculator(combatOptions.Value.ToTurnOrderConfiguration());
    return calculator.IsSuccess
        ? calculator.Value
        : throw new InvalidOperationException(calculator.Error);
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
builder.Services.AddSingleton<ICombatFlowPlanner>(sp => new CombatFlowPlanner(
    sp.GetRequiredService<IContentRuntimeResolver>(),
    sp.GetRequiredService<IActionManager>(),
    sp.GetRequiredService<IIntentResolver>(),
    sp.GetRequiredService<ICombatStatusLifecycle>(),
    sp.GetRequiredService<ICombatRelicLifecycle>(),
    sp.GetRequiredService<ICombatResourceLifecycle>()));
builder.Services.AddSingleton<ICombatRunCoordinator>(sp => new CombatRunCoordinator(
    sp.GetRequiredService<ICombatSystem>(),
    sp.GetRequiredService<IRunManager>(),
    sp.GetRequiredService<ICardPlayExecutor>(),
    sp.GetRequiredService<ICombatFlowPlanner>(),
    sp.GetRequiredService<IGambitEngine>(),
    sp.GetRequiredService<IEventBus>(),
    sp.GetRequiredService<IAbilityExecutor>()));
builder.Services.AddSingleton<ICombatScenarioCompiler>(sp => new CombatScenarioCompiler(
    sp.GetRequiredService<IGameModeResolver>(),
    sp.GetRequiredService<ICardContentCatalog>(),
    sp.GetRequiredService<ICardPoolResolver>(),
    sp.GetRequiredService<IResourceCatalog<EnemyPoolDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<CardUpgradeDefinition>>(),
    sp.GetRequiredService<EntityDefinitionLoader>(),
    sp.GetRequiredService<IResourceManager>(),
    sp.GetRequiredService<IContentManifestProvider>(),
    sp.GetRequiredService<IPinnedContentCatalog<StatusEffectDefinition>>(),
    sp.GetRequiredService<IContentRuntimeResolver>()));
builder.Services.AddSingleton<ICombatSandboxService>(sp => new CombatSandboxService(
    sp.GetRequiredService<ICombatScenarioCompiler>(),
    sp.GetRequiredService<IRunManager>(),
    sp.GetRequiredService<ICombatRunCoordinator>(),
    sp.GetRequiredService<IRunStateRepository>()));
builder.Services.AddSingleton<ICombatSandboxSnapshotService>(sp => new CombatSandboxSnapshotService(
    sp.GetRequiredService<IRunManager>()));
builder.Services.AddSingleton<ICombatTimelineProjectionService>(sp => new CombatTimelineProjectionService(
    sp.GetRequiredService<IRunManager>(),
    (IRunCheckpointRepository)sp.GetRequiredService<IRunStateRepository>()));

builder.Services.AddSingleton<Core.Entity.Definitions.EntityFactory>(sp => new Core.Entity.Definitions.EntityFactory(
    sp.GetRequiredService<EntityDefinitionLoader>(),
    sp.GetRequiredService<IResourceManager>(),
    new CoreLoggerAdapter(sp.GetRequiredService<ILoggerFactory>().CreateLogger("EntityDefinitionFactory"))));
builder.Services.AddSingleton<API.Services.DailyChallengeService>();

builder.Services.AddSingleton<IActionCostEvaluator, ActionCostEvaluator>();
builder.Services.AddSingleton<ICardPlayEvaluator, CardPlayEvaluator>();
builder.Services.AddSingleton<CardComponentInfluenceProvider>(sp =>
    new CardComponentInfluenceProvider(sp.GetRequiredService<IRuntimeFormulaEvaluator>()));
builder.Services.AddSingleton<EntityResourceInfluenceProvider>();
builder.Services.AddSingleton<RunModifierInfluenceProvider>(sp =>
    new RunModifierInfluenceProvider(sp.GetRequiredService<IRuntimeFormulaEvaluator>()));
builder.Services.AddSingleton<StatusCalculationInfluenceProvider>(sp =>
    new StatusCalculationInfluenceProvider(sp.GetRequiredService<IRuntimeFormulaEvaluator>()));
builder.Services.AddSingleton<RelicCalculationInfluenceProvider>(sp =>
    new RelicCalculationInfluenceProvider(sp.GetRequiredService<IRuntimeFormulaEvaluator>()));
builder.Services.AddSingleton<GameModeCalculationInfluenceProvider>(sp =>
    new GameModeCalculationInfluenceProvider(sp.GetRequiredService<IRuntimeFormulaEvaluator>()));
builder.Services.AddSingleton<EncounterCalculationInfluenceProvider>(sp =>
    new EncounterCalculationInfluenceProvider(sp.GetRequiredService<IRuntimeFormulaEvaluator>()));
builder.Services.AddSingleton<ICalculationInfluenceProvider>(sp =>
    new CompositeCalculationInfluenceProvider(
    [
        sp.GetRequiredService<CardComponentInfluenceProvider>(),
        sp.GetRequiredService<EntityResourceInfluenceProvider>(),
        sp.GetRequiredService<RunModifierInfluenceProvider>(),
        sp.GetRequiredService<StatusCalculationInfluenceProvider>(),
        sp.GetRequiredService<RelicCalculationInfluenceProvider>(),
        sp.GetRequiredService<GameModeCalculationInfluenceProvider>(),
        sp.GetRequiredService<EncounterCalculationInfluenceProvider>()
    ]));
builder.Services.AddSingleton<ICardPlayExecutor, CardPlayExecutor>();
builder.Services.AddSingleton<ICardInspectionService, CardInspectionService>();
builder.Services.AddSingleton<IAbilityExecutor, AbilityExecutor>();

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
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HeroScript API",
        Version = "v1",
        Description = "Headless deterministic game-engine API. See /docs/api/governance for contract rules."
    });
});

var app = builder.Build();

// Cache membership is explicit and complete. The coordinator orders broad
// invalidations by dependency and preserves revision-addressed runtimes unless
// an administrator explicitly requests their removal.
var cacheCoordinator = app.Services.GetRequiredService<ICacheCoordinator>();
var applicationCaches = new ICacheService[]
{
    (ICacheService)app.Services.GetRequiredService<IResourceLoader>(),
    app.Services.GetRequiredService<EntityDefinitionLoader>(),
    app.Services.GetRequiredService<PhaseSequenceLoader>(),
    (ICacheService)app.Services.GetRequiredService<IMathEngine>(),
    (ICacheService)app.Services.GetRequiredService<IContentManifestProvider>(),
    (ICacheService)app.Services.GetRequiredService<IContentRuntimeResolver>(),
    (ICacheService)app.Services.GetRequiredService<ICardContentCatalog>(),
    (ICacheService)app.Services.GetRequiredService<ICardPoolResolver>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<RelicDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<CardUpgradeDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<CalculationPipelineDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<GameModeDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<FlowRulesDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<CombatRulesDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<ReplayPolicyDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<TimelinePolicyDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<ContentBindingPolicyDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<CapabilityPolicyDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<EnemyPoolDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<DailyChallengeDefinition>>()
};
foreach (var cache in applicationCaches)
    cacheCoordinator.Register(cache);

// Resolve logging through DI; Core services never depend on global factories.
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var coreLogger = new CoreLoggerAdapter(loggerFactory.CreateLogger("Core"));

// Load status effect definitions
var statusEffectManager = app.Services.GetRequiredService<IStatusEffectManager>();
var resourceManager = app.Services.GetRequiredService<IResourceManager>();
resourceManager.LoadResourceDefinitions("default");
var actionManager = app.Services.GetRequiredService<IActionManager>();
actionManager.LoadActionDefinitions("default");
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

// The machine-readable contract is published in every environment. The
// interactive UI remains a development aid and never becomes the app root.
app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "HeroScript API v1");
        options.RoutePrefix = "docs/api";
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
app.UseMiddleware<API.Middleware.ApiStatusCodeProblemDetailsMiddleware>();
app.UseMiddleware<API.Middleware.AdminKeyMiddleware>();
app.MapControllers();

app.Run();
