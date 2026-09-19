using API.Models;
using API.Logging;
using API.Services;
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
using Core.Combat.LegalActions;
using Core.Combat.Reactions;
using Core.Combat.TurnPhase;
using Core.Combat.TurnOrder;
using Core.Resources;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Core.Run.Replay;
using Core.Run.Events;
using Core.Run.Projections;
using Core.Run.Runtime;
using Core.Run.Sandbox;
using Core.CardZones;
using Core.StatusEffects;
using Core.Entity.Definitions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using API.Contracts;
using Mods;

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
var toolAccessSettings = builder.Configuration.GetSection("ToolAccess").Get<ToolAccessSettings>() ?? new();
builder.Services.AddSingleton(toolAccessSettings);
builder.Services.AddSingleton<IToolAccessPolicy, ToolAccessPolicy>();

// Register OperationalEventBus first (singleton) - must be registered before other services that depend on it
builder.Services.AddSingleton<IGameEventContextAccessor, GameEventContextAccessor>();
builder.Services.AddSingleton<IOperationalEventBus, OperationalEventBus>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("OperationalEventBus"));
    return new OperationalEventBus(
        logger,
        sp.GetService<IOperationalEventStore>(),
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
    var eventBus = sp.GetRequiredService<IOperationalEventBus>();
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
    return new MathEngine(configManager, formulaLoader, logger);
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

// Register ResourceManager
builder.Services.AddSingleton<IResourceManager, ResourceManager>(sp =>
{
    var configManager = sp.GetRequiredService<IConfigManager>();
    var resourceLoader = sp.GetRequiredService<IResourceLoader>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ResourceManager"));
    return new ResourceManager(
        configManager,
        resourceLoader,
        logger,
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

builder.Services.AddSingleton<IPinnedContentCatalog<StatusEffectDefinition>>(sp =>
    new PinnedContentCatalog<StatusEffectDefinition>(
        sp.GetRequiredService<IContentRuntimeResolver>(),
        "status-effects",
        (_, definition) => definition));
builder.Services.AddSingleton<IPinnedContentCatalog<ScriptModifierDefinition>>(sp =>
    new PinnedContentCatalog<ScriptModifierDefinition>(
        sp.GetRequiredService<IContentRuntimeResolver>(),
        "modifiers",
        (_, definition) => definition));

builder.Services.AddSingleton<ICombatStatusLifecycle>(sp => new CombatStatusLifecycle(
    sp.GetRequiredService<IEffectTriggerExecutor>()));
builder.Services.AddSingleton<ICombatRelicLifecycle>(sp => new CombatRelicLifecycle(
    sp.GetRequiredService<IEffectTriggerExecutor>()));

// Register persistence services
var telemetryStorePath = builder.Configuration.GetValue<string>("Persistence:OperationalTelemetryPath") ?? "data/telemetry";
var runStatePath = builder.Configuration.GetValue<string>("Persistence:RunStatePath") ?? "data/runs";
var contentStorePath = builder.Configuration.GetValue<string>("Persistence:ContentStorePath") ?? "data/content";
var configuredPackageRoots = builder.Configuration
    .GetSection("Mods:PackageRoots")
    .Get<string[]>() ?? [];
var packageRoots = configuredPackageRoots.Length > 0
    ? configuredPackageRoots
    : [Path.Combine(AppContext.BaseDirectory, "Resources")];
var startupSettingId = builder.Configuration.GetValue<string>("Content:StartupSetting") ?? "default";

builder.Services.AddSingleton<IOperationalEventStore>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("JsonFileOperationalEventStore"));
    return new JsonFileOperationalEventStore(telemetryStorePath, logger);
});

builder.Services.AddSingleton<IRunCommitStore>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("FileRunCommitStore"));
    return new FileRunCommitStore(runStatePath, logger);
});
builder.Services.AddSingleton<IRunCommitReader>(sp => sp.GetRequiredService<IRunCommitStore>());
builder.Services.AddSingleton<IRunCommitProjectionReader, RunCommitProjectionReader>();
builder.Services.AddSingleton<ICombatResolutionReader, CombatResolutionReader>();

// Register Run content and manager
builder.Services.AddSingleton<IContentKindRegistry>(_ => ContentKindRegistry.Default);
builder.Services.AddSingleton<IContentManifestProvider, ContentManifestProvider>();
builder.Services.AddSingleton<IContentGraphValidator, ContentGraphValidator>();
builder.Services.AddSingleton<IContentPublicationService>(sp => new ContentPublicationService(
    contentStorePath,
    sp.GetRequiredService<IContentManifestProvider>(),
    sp.GetRequiredService<IContentGraphValidator>()));
builder.Services.AddSingleton<IContentRuntimeResolver, ContentRuntimeResolver>();
builder.Services.AddSingleton<IContentReloadService, ContentReloadService>();
for (var packageRootIndex = 0; packageRootIndex < packageRoots.Length; packageRootIndex++)
{
    builder.Services.AddSingleton<IPackageProvider>(new DirectoryPackageProvider(
        $"configured:{packageRootIndex:D3}",
        packageRoots[packageRootIndex]));
}
builder.Services.AddSingleton<SettingCompiler>();
builder.Services.AddSingleton<ISettingCompiler>(sp => sp.GetRequiredService<SettingCompiler>());
builder.Services.AddSingleton<ISettingBundleCompiler>(sp => sp.GetRequiredService<SettingCompiler>());
builder.Services.AddSingleton<ICardContentCatalog, CardContentCatalog>();
builder.Services.AddSingleton<ICardPoolResolver, CardPoolResolver>();
builder.Services.AddSingleton<ICardContentCompiler, CardContentCompiler>();
builder.Services.AddSingleton<IEffectiveCardResolver, EffectiveCardResolver>();
builder.Services.AddSingleton<ICardZoneCardMetadataResolver, RevisionedCardZoneCardMetadataResolver>();
builder.Services.AddSingleton<ICardZoneRuleEvaluator, CardZoneRuntimeRuleEvaluator>();
builder.Services.AddSingleton<ICardZoneFlowExecutor, CardZoneFlowExecutor>();
builder.Services.AddSingleton<IRunCardResolver, RunCardResolver>();
builder.Services.AddSingleton<ICalculationEngine>(sp =>
    new CalculationEngine(sp.GetRequiredService<IRuntimeFormulaEvaluator>()));
builder.Services.AddSingleton<IImmutableEffectProcessor, ImmutableEffectProcessor>();
builder.Services.AddSingleton<ICombatResourceLifecycle, CombatResourceLifecycle>();
builder.Services.AddSingleton<IEffectTriggerExecutor>(sp => new EffectTriggerExecutor(
    sp.GetRequiredService<IRuntimeFormulaEvaluator>(),
    sp.GetRequiredService<IImmutableEffectProcessor>(),
    sp.GetRequiredService<IContentRuntimeResolver>(),
    sp.GetRequiredService<ICalculationEngine>(),
    sp.GetRequiredService<ICalculationInfluenceProvider>(),
    cardZoneFlows: sp.GetRequiredService<ICardZoneFlowExecutor>()));
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
builder.Services.AddSingleton<IResourceCatalog<Core.CardZones.CardZoneSystemDefinition>>(sp =>
    new ResourceCatalog<Core.CardZones.CardZoneSystemDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "card-zone-systems",
        definition => definition.CardZoneSystemId));
builder.Services.AddSingleton<IResourceCatalog<FlowRulesDefinition>>(sp =>
    new ResourceCatalog<FlowRulesDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "flow-rules",
        definition => definition.FlowRulesId));
builder.Services.AddSingleton<IResourceCatalog<RunProgressionPolicyDefinition>>(sp =>
    new ResourceCatalog<RunProgressionPolicyDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "run-progression-policies",
        definition => definition.ProgressionPolicyId));
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
builder.Services.AddSingleton<GameModeResolver>(sp => new GameModeResolver(
    sp.GetRequiredService<IResourceCatalog<GameModeDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<FlowRulesDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<CombatRulesDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<ReplayPolicyDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<TimelinePolicyDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<ContentBindingPolicyDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<CapabilityPolicyDefinition>>(),
    sp.GetRequiredService<IResourceCatalog<RunProgressionPolicyDefinition>>(),
    sp.GetRequiredService<ICardPoolResolver>(),
    sp.GetRequiredService<IResourceCatalog<EnemyPoolDefinition>>(),
    sp.GetRequiredService<IContentRuntimeResolver>(),
    new CoreLoggerAdapter(sp.GetRequiredService<ILoggerFactory>().CreateLogger("GameModeResolver")),
    sp.GetRequiredService<IResourceCatalog<Core.CardZones.CardZoneSystemDefinition>>()));
builder.Services.AddSingleton<IGameModeResolver>(sp => sp.GetRequiredService<GameModeResolver>());
builder.Services.AddSingleton<IRevisionedGameModeResolver>(sp => sp.GetRequiredService<GameModeResolver>());
builder.Services.AddSingleton<IResourceCatalog<DailyChallengeDefinition>>(sp =>
    new ResourceCatalog<DailyChallengeDefinition>(
        sp.GetRequiredService<IConfigManager>(),
        sp.GetRequiredService<IResourceLoader>(),
        "daily-challenges",
        definition => definition.ChallengeId));
builder.Services.AddSingleton<IGameplayCommandCodec>(_ => GameplayCommandCodec.CreateDefault());
builder.Services.AddSingleton<IRunReplayService, RunSemanticReplayService>();
builder.Services.AddSingleton<IRunEventProjectionReader, RunEventProjectionReader>();
builder.Services.AddSingleton<Core.Meta.IPlayerProfileProjectionReader, Core.Meta.PlayerProfileProjectionReader>();
builder.Services.AddSingleton<Core.Run.Branching.IRunLineageIndex>(sp =>
    new Core.Run.Branching.RunLineageIndex(sp.GetRequiredService<IRunCommitReader>()));
builder.Services.AddSingleton<Core.Run.Branching.IRunBranchService>(sp =>
    new Core.Run.Branching.RunBranchService(
        (Core.Abstractions.Persistence.IRunCommitStore)sp.GetRequiredService<IRunCommitStore>(),
        sp.GetRequiredService<Core.Run.Branching.IRunLineageIndex>()));
builder.Services.AddSingleton<Core.Run.Branching.IRunSimulationService, Core.Run.Branching.RunSimulationService>();

builder.Services.AddSingleton<ITurnOrderResolver>(sp => new TurnOrderResolver(
    sp.GetRequiredService<IRuntimeFormulaEvaluator>()));
builder.Services.AddSingleton<IPhaseGraphReducer>(sp => new PhaseGraphReducer(
    sp.GetRequiredService<IRuntimeFormulaEvaluator>(),
    sp.GetRequiredService<IEffectTriggerExecutor>()));

builder.Services.AddSingleton<ICombatFactory>(sp => new CombatFactory(
    sp.GetRequiredService<IResourceManager>(),
    sp.GetRequiredService<EntityDefinitionLoader>()));

// Focused canonical combat-flow composition
builder.Services.AddSingleton<ICombatOutcomeResolver, CombatOutcomeResolver>();
builder.Services.AddSingleton<ICombatBoundaryExecutor>(sp => new CombatBoundaryExecutor(
    sp.GetRequiredService<ITurnOrderResolver>(),
    sp.GetRequiredService<ICombatStatusLifecycle>(),
    sp.GetRequiredService<ICombatRelicLifecycle>(),
    sp.GetRequiredService<ICombatResourceLifecycle>(),
    sp.GetRequiredService<IPhaseGraphReducer>(),
    sp.GetRequiredService<ICombatOutcomeResolver>(),
    sp.GetRequiredService<ICardZoneFlowExecutor>()));
builder.Services.AddSingleton<ICombatFlowPlanner>(sp => new CombatFlowPlanner(
    sp.GetRequiredService<IContentRuntimeResolver>(),
    sp.GetRequiredService<ICombatBoundaryExecutor>(),
    sp.GetRequiredService<IIntentResolver>()));
builder.Services.AddSingleton<ICombatScenarioCompiler>(sp => new CombatScenarioCompiler(
    sp.GetRequiredService<IRevisionedGameModeResolver>(),
    sp.GetRequiredService<EntityDefinitionLoader>(),
    sp.GetRequiredService<IResourceManager>(),
    sp.GetRequiredService<IContentManifestProvider>(),
    sp.GetRequiredService<IPinnedContentCatalog<StatusEffectDefinition>>(),
    sp.GetRequiredService<IContentRuntimeResolver>()));
builder.Services.AddSingleton<ICombatSandboxService>(sp => new CombatSandboxService(
    sp.GetRequiredService<ICombatScenarioCompiler>(),
    sp.GetRequiredService<IRunManager>(),
    sp.GetRequiredService<IGameplayCommandGateway>(),
    sp.GetRequiredService<IRunCommitReader>()));
builder.Services.AddSingleton<ICombatSandboxSnapshotService>(sp => new CombatSandboxSnapshotService(
    sp.GetRequiredService<IRunManager>()));
builder.Services.AddSingleton<ICombatTimelineProjectionService>(sp => new CombatTimelineProjectionService(
    sp.GetRequiredService<IRunManager>(),
    sp.GetRequiredService<IRunCommitReader>(),
    sp.GetRequiredService<IRunCommitProjectionReader>()));

builder.Services.AddSingleton<API.Services.DailyChallengeService>();

builder.Services.AddSingleton<IActionCostEvaluator, ActionCostEvaluator>();
builder.Services.AddSingleton<ICardPlayEvaluator, CardPlayEvaluator>();
builder.Services.AddSingleton<CardComponentInfluenceProvider>(sp =>
    new CardComponentInfluenceProvider(sp.GetRequiredService<IRuntimeFormulaEvaluator>()));
builder.Services.AddSingleton<EntityResourceInfluenceProvider>();
builder.Services.AddSingleton<EntityStatInfluenceProvider>();
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
        sp.GetRequiredService<EntityStatInfluenceProvider>(),
        sp.GetRequiredService<RunModifierInfluenceProvider>(),
        sp.GetRequiredService<StatusCalculationInfluenceProvider>(),
        sp.GetRequiredService<RelicCalculationInfluenceProvider>(),
        sp.GetRequiredService<GameModeCalculationInfluenceProvider>(),
        sp.GetRequiredService<EncounterCalculationInfluenceProvider>()
    ]));
builder.Services.AddSingleton<ICardPlayExecutor, CardPlayExecutor>();
builder.Services.AddSingleton<ICardInspectionService, CardInspectionService>();
builder.Services.AddSingleton<IAbilityExecutor, AbilityExecutor>();
builder.Services.AddSingleton<ILegalActionResolver, LegalActionResolver>();
builder.Services.AddSingleton<IReactionFlowReducer, ReactionFlowReducer>();
builder.Services.AddSingleton<ILegalActionQueryService, LegalActionQueryService>();
builder.Services.AddSingleton<IDecisionPolicy, GambitDecisionPolicy>();
builder.Services.AddSingleton<IDecisionPolicyRegistry, DecisionPolicyRegistry>();
builder.Services.AddSingleton<IIntentResolver, IntentResolver>();
builder.Services.AddSingleton<ICombatActionStateReducer, CombatActionStateReducer>();
builder.Services.AddSingleton<ICombatCommandHandler, CombatCommandHandler>();
builder.Services.AddSingleton<IAutomaticFlowDriver, AutomaticFlowDriver>();
builder.Services.AddSingleton<IGameplayRuntimeFactory, GameplayRuntimeFactory>();
builder.Services.AddSingleton<GameplayRuntime>(sp =>
    sp.GetRequiredService<IGameplayRuntimeFactory>().Create(new GameplayRuntimeOptions(
        GameplayPersistenceMode.Authoritative,
        sp.GetRequiredService<IRunCommitStore>(),
        sp.GetRequiredService<IOperationalEventBus>())));
builder.Services.AddSingleton<RunManager>(sp => sp.GetRequiredService<GameplayRuntime>().Runs);
builder.Services.AddSingleton<IRunManager>(sp => sp.GetRequiredService<GameplayRuntime>().Runs);
builder.Services.AddSingleton<IRunQueryService>(sp => sp.GetRequiredService<GameplayRuntime>().Runs);
builder.Services.AddSingleton<IRunCreationService>(sp => sp.GetRequiredService<GameplayRuntime>().Runs);
builder.Services.AddSingleton<IRunEncounterRuntime>(sp => sp.GetRequiredService<GameplayRuntime>().Runs);
builder.Services.AddSingleton<IContentRevisionActivationPreviewService>(
    sp => sp.GetRequiredService<GameplayRuntime>().Runs);
builder.Services.AddSingleton<RunSessionCoordinator>(sp => sp.GetRequiredService<GameplayRuntime>().RunCommands);
builder.Services.AddSingleton<IRunCommandGateway>(sp => sp.GetRequiredService<GameplayRuntime>().RunCommands);
builder.Services.AddSingleton<IRunCommandProcessor>(sp => sp.GetRequiredService<GameplayRuntime>().RunCommands);
builder.Services.AddSingleton<CombatRunCoordinator>(sp => sp.GetRequiredService<GameplayRuntime>().Combats);
builder.Services.AddSingleton<ICombatRunCoordinator>(sp => sp.GetRequiredService<GameplayRuntime>().Combats);
builder.Services.AddSingleton<GameplayCommandGateway>(sp => sp.GetRequiredService<GameplayRuntime>().Gateway);
builder.Services.AddSingleton<IGameplayCommandGateway>(sp => sp.GetRequiredService<GameplayRuntime>().Gateway);

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

// Shipped content crosses the same compile/validate/publish boundary as mods.
// Startup never exposes loose files directly to gameplay.
var startupCompilation = app.Services.GetRequiredService<ISettingCompiler>()
    .CompileAsync(startupSettingId)
    .GetAwaiter()
    .GetResult();
if (startupCompilation.IsFailure)
    throw new InvalidOperationException($"Startup setting compilation failed: {startupCompilation.Error}");
var startupPublication = app.Services.GetRequiredService<IContentPublicationService>()
    .PublishBundleAsync(startupCompilation.Value.Bundle)
    .GetAwaiter()
    .GetResult();
if (startupPublication.IsFailure)
    throw new InvalidOperationException($"Startup setting publication failed: {startupPublication.Error}");

// Cache membership is explicit and complete. The coordinator orders broad
// invalidations by dependency and preserves revision-addressed runtimes unless
// an administrator explicitly requests their removal.
var cacheCoordinator = app.Services.GetRequiredService<ICacheCoordinator>();
var applicationCaches = new ICacheService[]
{
    (ICacheService)app.Services.GetRequiredService<IResourceLoader>(),
    app.Services.GetRequiredService<EntityDefinitionLoader>(),
    (ICacheService)app.Services.GetRequiredService<IMathEngine>(),
    (ICacheService)app.Services.GetRequiredService<IContentRuntimeResolver>(),
    (ICacheService)app.Services.GetRequiredService<ICardContentCatalog>(),
    (ICacheService)app.Services.GetRequiredService<ICardPoolResolver>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<RelicDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<CardUpgradeDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<CalculationPipelineDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<GameModeDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<FlowRulesDefinition>>(),
    (ICacheService)app.Services.GetRequiredService<IResourceCatalog<RunProgressionPolicyDefinition>>(),
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
