using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Run.Content;
using Core.Run.DeckCompartments;

namespace Core.Tests.DeckCompartments;

internal static class CompartmentFixture
{
    internal static ContentBundle Bundle(params (string Kind, string Id, object Value)[] definitions)
    {
        var artifacts = definitions.GroupBy(d => d.Kind).ToDictionary(g => g.Key + "/test.json",
            g => JsonSerializer.SerializeToElement(g.ToDictionary(d => d.Id, d => d.Value)));
        return new() { Manifest = new() { ConfigName = "test", Revision = new string('a', 64),
            Artifacts = artifacts.Select(a => new ContentArtifactManifest { Kind = a.Key.Split('/')[0],
                Path = a.Key, DefinitionCount = a.Value.EnumerateObject().Count() }).ToImmutableArray() },
            Artifacts = artifacts.ToImmutableDictionary() };
    }
    internal static CardComponentBundleDefinition GrantBundle => new() { BundleId = "utility", Components = [
        new CardConditionComponentDefinition { ComponentId = "ready", Expression = "1" },
        new CardTargetingComponentDefinition { ComponentId = "target", MinimumTargets = 0, MaximumTargets = 0 }
    ] };
    internal static DeckCompartmentProfileDefinition Profile => new() { ProfileId = "layout", Compartments = [
        new() { CompartmentId = "utility", Capacity = 2, Grants = [new() { GrantId = "grant", BundleId = "utility" }] },
        new() { CompartmentId = "other", Capacity = 1, Grants = [new() { GrantId = "grant", BundleId = "utility" }] }
    ] };
    internal static ContentRuntime Runtime => ContentRuntime.Create(Bundle(
        ("card-component-bundles", "utility", GrantBundle),
        ("deck-compartment-profiles", "layout", Profile),
        ("deck-compartment-policies", "outside", new DeckCompartmentPolicyDefinition { PolicyId = "outside" })
    )).Value;
}
