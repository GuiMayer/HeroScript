using Core.Resources;
using Xunit;

namespace Core.Tests.Resources;

public sealed class ResourceFormulaVariablesTests
{
    [Fact]
    public void AddOwner_ProjectsArbitraryResourcesWithCanonicalNamesInStableOrder()
    {
        var resources = new ResourceSet
        {
            OwnerId = "actor",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["mana"] = Pool("mana", 2, 0, 8),
                ["focus"] = Pool("focus", 5, 0, 10)
            }
        };
        var variables = new Dictionary<string, float>(StringComparer.Ordinal);

        ResourceFormulaVariables.AddOwner(variables, "source", resources);

        Assert.Equal(
            [
                "source.resources.focus.current",
                "source.resources.focus.minimum",
                "source.resources.focus.maximum",
                "source.resources.focus.percent",
                "source.resources.mana.current",
                "source.resources.mana.minimum",
                "source.resources.mana.maximum",
                "source.resources.mana.percent"
            ],
            variables.Keys);
        Assert.Equal(5, variables["source.resources.focus.current"]);
        Assert.Equal(50, variables["source.resources.focus.percent"]);
        Assert.DoesNotContain("source_focus_current", variables.Keys);
    }

    [Fact]
    public void AddUnscoped_OverwritesExternalValuesWithAuthoritativeSnapshot()
    {
        var variables = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["resources.focus.current"] = 999
        };

        ResourceFormulaVariables.AddUnscoped(
            variables,
            new Dictionary<string, ResourcePool>
            {
                ["focus"] = Pool("focus", 4, 0, 10)
            });

        Assert.Equal(4, variables["resources.focus.current"]);
        Assert.Equal(40, variables["resources.focus.percent"]);
    }

    private static ResourcePool Pool(string id, float current, float minimum, float maximum) => new()
    {
        ResourceId = id,
        Current = current,
        Minimum = minimum,
        Maximum = maximum,
        Definition = new ResourceDefinition
        {
            ResourceId = id,
            DisplayName = id,
            DefaultMin = minimum,
            DefaultMax = maximum,
            DefaultCurrent = current
        }
    };
}
