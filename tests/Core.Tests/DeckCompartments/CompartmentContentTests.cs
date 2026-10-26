using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Content;
using Core.Determinism;
using Core.Entity.Definitions;
using Core.Entity.Integration;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckCompartments;
using Moq;
using Xunit;

namespace Core.Tests.DeckCompartments;

public sealed class CompartmentContentTests
{
    [Fact]
    public void ProfilesUseGenericBundlesWithoutAnyAffinity()
    {
        Assert.True(CompartmentFixture.Profile.Validate(CompartmentFixture.Runtime).IsSuccess);
        Assert.True(CardBundleCompiler.Expand(CompartmentFixture.GrantBundle, "source").IsSuccess);
    }

    [Theory]
    [InlineData(-1)] [InlineData(257)]
    public void InvalidCapacityRejected(int capacity)
    {
        var profile = CompartmentFixture.Profile with { Compartments = [CompartmentFixture.Profile.Compartments[0] with { Capacity = capacity }] };
        Assert.True(profile.Validate(CompartmentFixture.Runtime).IsFailure);
    }

    [Fact]
    public void MissingBundleAndDuplicateCompartmentsRejected()
    {
        var compartment = CompartmentFixture.Profile.Compartments[0];
        Assert.True((CompartmentFixture.Profile with { Compartments = [compartment, compartment] }).Validate(CompartmentFixture.Runtime).IsFailure);
        Assert.True((CompartmentFixture.Profile with { Compartments = [compartment with { Grants = [new() { GrantId = "a", BundleId = "missing" }] }] }).Validate(CompartmentFixture.Runtime).IsFailure);
    }

    [Fact]
    public void CharacterBindingMaterializesAndRoundTripsWithoutSpecialRole()
    {
        var definition = new EntityDefinition { DefinitionId = "actor", DisplayName = "Actor",
            Components = [new DeckCompartmentProfileComponentDefinition { ComponentId = "build", ProfileId = "layout" }] };
        Assert.True(EntityDefinitionValidator.Validate(definition).IsSuccess);
        var actor = new EntityMaterializer(Mock.Of<IResourceManager>()).Materialize(definition, "actor", "revision", "side", new());
        Assert.True(actor.IsSuccess, actor.IsFailure ? actor.Error : null);
        var json = JsonSerializer.Serialize(actor.Value);
        var restored = JsonSerializer.Deserialize<CombatActorState>(json)!;
        Assert.Equal("layout", restored.Component<DeckCompartmentProfileComponentState>()!.ProfileId);
    }

    [Fact]
    public void DisabledModeHasNoNewSerializedMember()
    {
        Assert.DoesNotContain("deckCompartment", CanonicalJson.Serialize(new GameModeDefinition()));
    }

    [Fact]
    public void PublicationRejectsDanglingCharacterProfile()
    {
        var bundle = CompartmentFixture.Bundle(("entities", "actor", new EntityDefinition { DefinitionId = "actor", DisplayName = "Actor",
            Components = [new DeckCompartmentProfileComponentDefinition { ComponentId = "build", ProfileId = "missing" }] }));
        var validation = new ContentGraphValidator().Validate(bundle);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("missing"));
    }
}
