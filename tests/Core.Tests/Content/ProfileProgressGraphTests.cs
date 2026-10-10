using Core.Meta;
using Core.Tests.Run;
using Xunit;

namespace Core.Tests.Content;

public sealed partial class ContentGraphValidatorTests
{
    [Fact]
    public void ProfileProgressPoliciesAreCanonicalContent_AndUnknownReferencesFailPublication()
    {
        var policy = ProfileProgressTests.Policy();
        var result = new Core.Content.ContentGraphValidator().Validate(Bundle(
            ("profile-progress-policies", "profile-progress-policies/options.json", new() { ["options"] = policy })));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Unknown unlock target", StringComparison.Ordinal));
    }

    [Fact]
    public void ProfileProgressPolicyCyclesFailGraphValidationBeforePublication()
    {
        var policy = ProfileProgressTests.Policy();
        policy = policy with { Unlocks = [policy.Unlocks[0] with { Condition = new() {
            Kind = UnlockConditionKind.UnlockGranted, UnlockId = "new-card" } }] };
        var result = new Core.Content.ContentGraphValidator().Validate(Bundle(
            ("profile-progress-policies", "profile-progress-policies/options.json", new() { ["options"] = policy }),
            ("cards", "cards/catalog.json", new() { ["card-b"] = new Core.Run.Content.CardContentDefinition { CardId = "card-b" } })));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void ProfileConditionsCannotTargetAnUnrelatedOrUnknownNode()
    {
        var policy = ProfileProgressTests.Policy();
        policy = policy with { Unlocks = [policy.Unlocks[0] with { Condition = new() {
            Kind = UnlockConditionKind.EncounterCompleted, RunDefinitionId = "missing-run", NodeId = "boss" } }] };
        var result = new Core.Content.ContentGraphValidator().Validate(Bundle(
            ("profile-progress-policies", "profile-progress-policies/options.json", new() { ["options"] = policy })));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Unknown run/node", StringComparison.Ordinal));
    }
}
