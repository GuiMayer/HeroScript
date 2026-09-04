using Core.Resources;
using Xunit;

namespace Core.Tests.Resources;

public sealed class ResourceSetTests
{
    [Fact]
    public void RebindDefinitions_PreservesStateAndAppliesNewConstraintsImmutably()
    {
        var oldDefinition = Definition("Old Health", canExceedMax: true);
        var newDefinition = Definition("New Health", canExceedMax: false);
        var original = new ResourceSet
        {
            OwnerId = "hero",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new()
                {
                    ResourceId = "health",
                    Current = 150,
                    Minimum = 0,
                    Maximum = 100,
                    Definition = oldDefinition
                }
            }
        };

        var result = original.RebindDefinitions(new Dictionary<string, ResourceDefinition>
        {
            ["health"] = newDefinition
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(150, original.Current("health"));
        Assert.Same(oldDefinition, original.Get("health")!.Definition);
        Assert.Equal(100, result.Value.Current("health"));
        Assert.Equal(100, result.Value.Get("health")!.Maximum);
        Assert.Same(newDefinition, result.Value.Get("health")!.Definition);
    }

    [Fact]
    public void RebindDefinitions_RejectsMissingDefinitionWithoutPartialResult()
    {
        var state = new ResourceSet
        {
            OwnerId = "hero",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new()
                {
                    ResourceId = "health",
                    Current = 10,
                    Maximum = 10,
                    Definition = Definition("Health", canExceedMax: false)
                }
            }
        };

        var result = state.RebindDefinitions(
            new Dictionary<string, ResourceDefinition>());

        Assert.True(result.IsFailure);
        Assert.Contains("health", result.Error);
        Assert.Equal(10, state.Current("health"));
    }

    private static ResourceDefinition Definition(string displayName, bool canExceedMax) => new()
    {
        ResourceId = "health",
        DisplayName = displayName,
        DefaultMin = 0,
        DefaultMax = 100,
        DefaultCurrent = 100,
        CanExceedMax = canExceedMax
    };
}
