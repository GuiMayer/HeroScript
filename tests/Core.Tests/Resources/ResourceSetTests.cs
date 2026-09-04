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

    [Fact]
    public void ResourceCostTransitions_RejectsAggregateOverspendAtomically()
    {
        var state = Energy(5);

        var result = ResourceCostTransitions.Spend(
            state,
            [
                new ResolvedResourceCost { ResourceId = "energy", Amount = 3 },
                new ResolvedResourceCost { ResourceId = "energy", Amount = 3 }
            ],
            "action");

        Assert.True(result.IsFailure);
        Assert.Contains("Insufficient", result.Error);
        Assert.Equal(5, state.Current("energy"));
    }

    [Fact]
    public void ResourceCostTransitions_AppliesEveryCostInDeclaredOrder()
    {
        var state = Energy(5);

        var result = ResourceCostTransitions.Spend(
            state,
            [
                new ResolvedResourceCost { ResourceId = "energy", Amount = 2 },
                new ResolvedResourceCost { ResourceId = "energy", Amount = 1 }
            ],
            "action");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.State.Current("energy"));
        Assert.Equal(2, result.Value.Records.Count);
        Assert.Equal(5, result.Value.Records[0].PreviousValue);
        Assert.Equal(3, result.Value.Records[0].CurrentValue);
        Assert.Equal(3, result.Value.Records[1].PreviousValue);
        Assert.Equal(2, result.Value.Records[1].CurrentValue);
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

    private static ResourceSet Energy(float current)
    {
        var definition = new ResourceDefinition
        {
            ResourceId = "energy",
            DisplayName = "Energy",
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = current
        };
        return new ResourceSet
        {
            OwnerId = "hero",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["energy"] = new()
                {
                    ResourceId = "energy",
                    Current = current,
                    Minimum = 0,
                    Maximum = 10,
                    Definition = definition
                }
            }
        };
    }
}
