using Core.Effects;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.Content;
using Xunit;

namespace Core.Tests.Content;

public sealed partial class ContentGraphValidatorTests
{
    [Fact]
    public void GrammarPublication_ConditionalPrerequisitesDoNotRequireEveryBaseCardToMatch()
    {
        var result = new ContentGraphValidator().Validate(GrammarBundle());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Theory]
    [InlineData("missing_bundle")]
    [InlineData("missing_resource")]
    [InlineData("unsupported_scope")]
    [InlineData("malformed_member")]
    public void GrammarPublication_ValidatesDormantRulesDespiteUnsatisfiedRequirements(string problem)
    {
        var result = new ContentGraphValidator().Validate(GrammarBundle(problem));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(problem == "missing_bundle" ? "missing" : problem == "missing_resource" ? "unknown" :
            problem == "unsupported_scope" ? "scope" : "execution limit", StringComparison.OrdinalIgnoreCase));
    }

    private static ContentBundle GrammarBundle(string? problem = null)
    {
        var rule = new CardCompositionRuleDefinition
        {
            RuleId = "arbitrary", Namespace = "arbitrary", BundleId = problem == "missing_bundle" ? "missing" : "pulse",
            Scope = problem == "unsupported_scope" ? (CardCompositionScope)999 : CardCompositionScope.AfterImpact
        };
        var upgrade = new CardUpgradeDefinition { UpgradeId = "conditional", Requirements = new() { RequiredCapabilities = ["not-yet-installed"] }, CompositionRules = [rule] };
        var effect = new CardEffectComponentDefinition { ComponentId = "amount", Effect = new()
        {
            Type = EffectType.DAMAGE, FlatValue = 1, TargetResource = problem == "missing_resource" ? "unknown" : "health",
            Repeat = problem == "malformed_member" ? 1000 : 1
        } };
        return Bundle(
            ("cards", "cards/test.json", new() { ["strike"] = new CardContentDefinition { CardId = "strike", Components = [effect with { Effect = effect.Effect with { Repeat = 1, TargetResource = "health" } }] } }),
            ("card-upgrades", "card-upgrades/test.json", new() { ["conditional"] = upgrade }),
            ("card-component-bundles", "card-component-bundles/test.json", new() { ["pulse"] = new CardComponentBundleDefinition { BundleId = "pulse", Components = [effect] } }),
            ("resources", "resources/test.json", new() { ["health"] = new ResourceDefinition { ResourceId = "health", DisplayName = "Health" } }));
    }
}
