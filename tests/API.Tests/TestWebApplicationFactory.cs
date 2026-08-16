using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Core.Combat;
using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Entity.Controllers;
using Core.Resources;
using Core.StatusEffects;
using Moq;

namespace API.Tests;

/// <summary>
/// Factory para criar instâncias de teste da API
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var projectRoot = FindProjectRoot();

        // Test hosts must not write to the Windows Event Log, which is unavailable
        // in ordinary developer and CI environments.
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IActionManager>();
            services.RemoveAll<IResourceManager>();
            services.RemoveAll<IScriptModifierManager>();
            services.RemoveAll<IGambitEngine>();

            services.AddSingleton(CreateActionManager());
            services.AddSingleton(CreateResourceManager());
            services.AddSingleton(CreateScriptModifierManager());
            services.AddSingleton(CreateGambitEngine());
        });

        // Usar ambiente de teste
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Enabled", "true");
        builder.UseSetting("Admin:ApiKey", "dev-admin-key");
         
        // Configurar content root para encontrar arquivos de configuração
        if (projectRoot != null)
        {
            builder.UseContentRoot(projectRoot);
        }
    }

    private static string? FindProjectRoot()
    {
        // Allow CI/test environment to specify root explicitly
        var envRoot = Environment.GetEnvironmentVariable("HERESCRIPT_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return envRoot;
        }
        
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        
        // Limit depth to prevent scanning to filesystem root
        for (int depth = 0; depth < 12 && dir != null; depth++)
        {
            var apiPath = Path.Combine(dir.FullName, "src", "API");
            if (Directory.Exists(apiPath))
            {
                return dir.FullName;
            }
            
            dir = dir.Parent;
        }
        
        return null;
    }

    private static IActionManager CreateActionManager()
    {
        var actions = new List<ActionDefinition>
        {
            new()
            {
                ActionId = "basic_attack",
                DisplayName = "Basic Attack",
                Description = "Deterministic test action",
                ActionType = ActionType.BASIC_ATTACK,
                RequiresTarget = true,
                Tags = new List<string> { "attack", "starter" },
                Costs = new ActionCosts
                {
                    Costs = new List<ResourceCost>
                    {
                        new() { ResourceId = "energy", Amount = 1 }
                    }
                }
            },
            new()
            {
                ActionId = "heal",
                DisplayName = "Heal",
                Description = "Deterministic test heal",
                ActionType = ActionType.POWER,
                RequiresTarget = false,
                Tags = new List<string> { "heal", "utility" },
                Costs = new ActionCosts()
            }
        };

        var mock = new Mock<IActionManager>();
        mock.Setup(m => m.LoadActionDefinitions(It.IsAny<string>()));
        mock.Setup(m => m.GetAllDefinitions()).Returns(actions);
        mock.Setup(m => m.GetDefinition(It.IsAny<string>()))
            .Returns((string id) =>
            {
                var action = actions.FirstOrDefault(a => string.Equals(a.ActionId, id, StringComparison.OrdinalIgnoreCase));
                return action is null
                    ? Result<ActionDefinition>.Failure($"Action not found: {id}")
                    : Result<ActionDefinition>.Success(action);
            });
        mock.Setup(m => m.GetDefinitionsByType(It.IsAny<ActionType>()))
            .Returns((ActionType type) => actions.Where(a => a.ActionType == type).ToList());
        mock.Setup(m => m.GetDefinitionsByTag(It.IsAny<string>()))
            .Returns((string tag) => actions.Where(a => a.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList());
        mock.Setup(m => m.ValidateActionDefinition(It.IsAny<ActionDefinition>())).Returns(Result.Success());

        return mock.Object;
    }

    private static IResourceManager CreateResourceManager()
    {
        var resources = new List<ResourceDefinition>
        {
            new()
            {
                ResourceId = "health",
                DisplayName = "Health",
                ShortName = "HP",
                Category = ResourceCategory.VITAL,
                DefaultCurrent = 30,
                DefaultMax = 30,
                DefaultMin = 0,
                Tags = new List<string> { "vital" }
            },
            new()
            {
                ResourceId = "energy",
                DisplayName = "Energy",
                ShortName = "EN",
                Category = ResourceCategory.TACTICAL,
                DefaultCurrent = 3,
                DefaultMax = 3,
                DefaultMin = 0,
                Tags = new List<string> { "combat" }
            }
        };

        var mock = new Mock<IResourceManager>();
        mock.Setup(m => m.LoadResourceDefinitions(It.IsAny<string>()));
        mock.Setup(m => m.GetAllDefinitions()).Returns(resources);
        mock.Setup(m => m.GetDefinition(It.IsAny<string>()))
            .Returns((string id) =>
            {
                var resource = resources.FirstOrDefault(r => string.Equals(r.ResourceId, id, StringComparison.OrdinalIgnoreCase));
                return resource is null
                    ? Result<ResourceDefinition>.Failure($"Resource not found: {id}")
                    : Result<ResourceDefinition>.Success(resource);
            });
        mock.Setup(m => m.GetDefinitionsByCategory(It.IsAny<ResourceCategory>()))
            .Returns((ResourceCategory category) => resources.Where(r => r.Category == category).ToList());
        mock.Setup(m => m.GetDefinitionsByTag(It.IsAny<string>()))
            .Returns((string tag) => resources.Where(r => r.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList());
        mock.Setup(m => m.CreatePool(It.IsAny<string>(), It.IsAny<float?>()))
            .Returns((string id, float? current) =>
            {
                var definition = resources.First(resource =>
                    string.Equals(resource.ResourceId, id, StringComparison.OrdinalIgnoreCase));
                return new ResourcePool
                {
                    ResourceId = id,
                    Current = current ?? definition.DefaultCurrent,
                    Maximum = Math.Max(current ?? definition.DefaultMax, definition.DefaultMax),
                    Minimum = definition.DefaultMin,
                    Definition = definition
                };
            });
        mock.Setup(m => m.CreatePoolFromDefinition(It.IsAny<ResourceDefinition>(), It.IsAny<float?>()))
            .Returns((ResourceDefinition definition, float? current) => new ResourcePool
            {
                ResourceId = definition.ResourceId,
                Current = current ?? definition.DefaultCurrent,
                Maximum = definition.DefaultMax,
                Minimum = definition.DefaultMin,
                Definition = definition
            });
        mock.Setup(m => m.CreateDefaultPools()).Returns(new Dictionary<string, ResourcePool>());
        mock.Setup(m => m.ValidateResourceExists(It.IsAny<string>()))
            .Returns((string id) => resources.Any(r => string.Equals(r.ResourceId, id, StringComparison.OrdinalIgnoreCase)));
        mock.Setup(m => m.ValidateCost(It.IsAny<ResourcePool>(), It.IsAny<float>()))
            .Returns((ResourcePool pool, float cost) => pool.Current >= cost ? Result.Success() : Result.Failure("Insufficient resource"));
        mock.Setup(m => m.ValidateResourceDefinition(It.IsAny<ResourceDefinition>())).Returns(Result.Success());
        mock.Setup(m => m.ProcessRegeneration(It.IsAny<EntityResourceState>(), It.IsAny<RegenerationTiming>(), It.IsAny<Dictionary<string, float>?>()))
            .Returns((EntityResourceState state, RegenerationTiming _, Dictionary<string, float>? _) => Result<EntityResourceState>.Success(state));
        mock.Setup(m => m.ReloadResource(It.IsAny<string>())).Returns(Result.Success());

        return mock.Object;
    }

    private static IScriptModifierManager CreateScriptModifierManager()
    {
        var mock = new Mock<IScriptModifierManager>();
        mock.Setup(m => m.LoadDefinitions(It.IsAny<string>())).Returns(Result.Success());
        mock.Setup(m => m.GetAllDefinitions()).Returns(new List<ScriptModifierDefinition>());
        mock.Setup(m => m.GetPipelineModifiers(It.IsAny<string>(), It.IsAny<IEnumerable<string>? >()))
            .Returns(new Dictionary<string, float>());
        return mock.Object;
    }

    private static IGambitEngine CreateGambitEngine()
    {
        var mock = new Mock<IGambitEngine>();
        mock.Setup(m => m.LoadDefinitions(It.IsAny<string>())).Returns(Result.Success());
        mock.Setup(m => m.GetAllDefinitions()).Returns(new List<GambitDefinition>());
        mock.Setup(m => m.DecideAction(It.IsAny<Core.Entity.Entity>(), It.IsAny<CombatState>(), It.IsAny<IEnumerable<string>? >()))
            .Returns(Result<EntityAction>.Failure("No gambit available in deterministic API tests"));
        return mock.Object;
    }
}
