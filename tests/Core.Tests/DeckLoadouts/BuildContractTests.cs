using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckLoadouts;
using Core.Run.Loadouts;
using Core.Run.Supports;
using Core.Tests.DeckCompartments;
using Xunit;

namespace Core.Tests.DeckLoadouts;

public sealed class BuildContractTests
{
    internal static DeckModuleDefinition Module => new() { ModuleId = "alternate", CardIds = ["card"], StartingCardIds = ["card"] };
    internal static DeckCompositionPolicyDefinition Policy => new()
    {
        PolicyId = "build", EditWindows = [LoadoutEquipmentWindow.OutsideCombat], Groups = [
            new() { GroupId = "primary", Minimum = 2, Maximum = 2, AllowDuplicateDefinitions = true, CardIds = ["card"], StartingCardIds = ["card", "card"] },
            new() { GroupId = "secondary", Minimum = 1, Maximum = 1, AllowDuplicateDefinitions = false, ModuleIds = ["alternate"], StartingModuleId = "alternate" }
        ]
    };
    internal static ContentBundle Bundle(params (string Kind, string Id, object Value)[] extra) => CompartmentFixture.Bundle(
        new[] { ("cards", "card", (object)new CardContentDefinition { CardId = "card", Components = [new CardConditionComponentDefinition { ComponentId = "valid", Expression = "1" }] }),
            ("deck-modules", "alternate", (object)Module) }.Concat(extra).ToArray());
    internal static ContentRuntime Runtime => ContentRuntime.Create(Bundle()).Value;

    [Fact] public void ConfiguredCountsAreNotTiedToOneGame()
    {
        Assert.True(Policy.Validate(Runtime).IsSuccess);
        Assert.Equal(3, Policy.Groups.Sum(g => g.Minimum));
        Assert.True(Policy.Validate(Runtime).IsSuccess);
    }
    [Fact] public void MissingReferencesAndAmbiguousEligibilityAreRejected()
    {
        var group = Policy.Groups[0];
        Assert.True((Policy with { Groups = [group with { CardIds = ["missing"] }] }).Validate(Runtime).IsFailure);
        Assert.True((Policy with { Groups = [group with { ModuleIds = ["alternate"] }] }).Validate(Runtime).IsFailure);
        Assert.True((Policy with { Groups = [group with { AllowDuplicateDefinitions = false }] }).Validate(Runtime).IsFailure);
        Assert.True((Policy with { Groups = [group with { Maximum = 1 }] }).Validate(Runtime).IsFailure);
        Assert.True((Policy with { Groups = [group, group] }).Validate(Runtime).IsFailure);
    }
    [Fact] public void UnimplementedEditWindowsCannotBePublished()
    {
        Assert.True((Policy with { EditWindows = [LoadoutEquipmentWindow.Combat] }).Validate(Runtime).IsFailure);
        Assert.True((Policy with { EditWindows = [] }).Validate(Runtime).IsFailure);
    }
    [Fact] public void PublicationIncludesSemanticBuildValidation()
    {
        var bad = Policy with { Groups = [Policy.Groups[0] with { Minimum = 4 }] };
        var result = new ContentGraphValidator().Validate(Bundle(("deck-composition-policies", "build", bad)));
        Assert.Contains(result.Errors, value => value.StartsWith("deck-composition-policies/build:"));
    }
    [Theory]
    [InlineData("{\"policyId\":\"build\",\"groups\":[{\"groupId\":\"one\"}]}")]
    [InlineData("{\"policyId\":\"build\",\"heroCount\":15}")]
    public void SchemaRejectsMissingExplicitBoundsAndUnknownFields(string json)
    {
        Assert.True(ContentKindRegistry.Default.Validate("deck-composition-policies", "build", JsonDocument.Parse(json).RootElement).IsFailure);
    }
    [Fact] public void OldOpaqueKindsAreNotReinterpreted()
    {
        using var json = JsonDocument.Parse("{\"anything\":42}");
        Assert.True(ContentKindRegistry.Default.Validate("decks", "old", json.RootElement).IsSuccess);
        Assert.True(ContentKindRegistry.Default.Validate("companions", "old", json.RootElement).IsSuccess);
    }
    [Fact] public void PaymentContractHasNoResourceOrZonePurposeHardcoded()
    {
        var payment = new CardZonePaymentDefinition { PaymentId = "fuel", SourceZoneId = "custom_a", TargetZoneId = "custom_b", Count = 2 };
        Assert.True(payment.Validate().IsSuccess);
        Assert.True((payment with { TargetZoneId = "custom_a" }).Validate().IsFailure);
        Assert.True((payment with { Count = 0 }).Validate().IsFailure);
        Assert.True((payment with { RequiredTags = ["x"], ExcludedTags = ["x"] }).Validate().IsFailure);
    }
    [Fact] public void SupportsRequirePublishedEntityAndRoutineReferences()
    {
        Assert.True(new SupportDefinition { SupportId = "support" }.Validate(Runtime).IsFailure);
        Assert.True(new SupportRoutineDefinition { RoutineId = "routine", GambitIds = ["missing"] }.Validate(Runtime).IsFailure);
        Assert.True(new SupportLoadoutPolicyDefinition { PolicyId = "support", SupportIds = ["missing"], EditWindows = [LoadoutEquipmentWindow.OutsideCombat] }.Validate(Runtime).IsFailure);
    }
    [Fact] public void ContractValidationDoesNotMutateSourceOrExistingRunShape()
    {
        var before = JsonSerializer.Serialize(Policy);
        _ = Policy.Validate(Runtime);
        Assert.Equal(before, JsonSerializer.Serialize(Policy));
        Assert.DoesNotContain("DeckLoadout", JsonSerializer.Serialize(new RunState()));
        Assert.DoesNotContain("SupportLoadout", JsonSerializer.Serialize(new RunState()));
    }
}
