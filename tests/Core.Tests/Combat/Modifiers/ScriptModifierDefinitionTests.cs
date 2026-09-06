using Core.Calculations;
using Core.Combat.Modifiers;
using Xunit;

namespace Core.Tests.Combat.Modifiers;

[Trait("Category", "Unit")]
public sealed class ScriptModifierDefinitionTests
{
    [Fact]
    public void DefinitionUsesInfluenceComponentsInsteadOfParallelNumericFields()
    {
        var definition = new ScriptModifierDefinition
        {
            ModifierId = "glass-cannon",
            Influences =
            [
                new ContextualInfluenceDefinition
                {
                    InfluenceId = "attack-increased",
                    Scope = CalculationEntityScope.Actor,
                    Channel = "effect_amount",
                    Bucket = "increased",
                    Formula = "stacks * 0.25",
                    RequiredTags = ["attack"]
                }
            ]
        };

        Assert.Single(definition.Influences);
        var propertyNames = typeof(ScriptModifierDefinition).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var forbidden in new[]
                 {
                     "ModifierKey", "FormulaValue", "BaseValue", "RequiredTags", "ExcludedTags"
                 })
            Assert.DoesNotContain(forbidden, propertyNames);
    }
}
