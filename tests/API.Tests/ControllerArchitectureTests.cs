using API.Controllers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace API.Tests;

[Trait("Category", "Architecture")]
public sealed class ControllerArchitectureTests
{
    [Fact]
    public void PublicGameplayControllers_DependOnlyOnApplicationPorts()
    {
        Type[] controllerTypes =
        [
            typeof(CombatController),
            typeof(CombatCommandController),
            typeof(RunCommandController),
            typeof(CombatTimelineController),
            typeof(CombatTimelineBranchController),
            typeof(CombatSandboxController)
        ];

        foreach (var controller in controllerTypes)
        {
            var constructor = Assert.Single(controller.GetConstructors());
            foreach (var dependency in constructor.GetParameters().Select(parameter => parameter.ParameterType))
            {
                if (dependency.IsGenericType && dependency.GetGenericTypeDefinition() == typeof(ILogger<>))
                    continue;

                Assert.True(
                    dependency.IsInterface,
                    $"{controller.Name} depends on concrete gameplay service {dependency.FullName}");
                Assert.StartsWith("I", dependency.Name, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void EntityCatalog_HasOneGenericContentAuthority()
    {
        var root = FindProjectRoot();
        Assert.False(File.Exists(Path.Combine(root, "src", "API", "Controllers", "EntityController.cs")));
        Assert.Empty(Directory.Exists(Path.Combine(root, "src", "API", "Models", "Entities"))
            ? Directory.GetFiles(Path.Combine(root, "src", "API", "Models", "Entities"), "*.cs")
            : []);

        var contentController = File.ReadAllText(
            Path.Combine(root, "src", "API", "Controllers", "ContentController.cs"));
        Assert.Contains("api/v1/content", contentController, StringComparison.Ordinal);
        Assert.DoesNotContain("EntityDefinitionLoader", contentController, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var depth = 0; depth < 12 && directory != null; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HeroScript.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("HeroScript repository root was not found.");
    }
}
