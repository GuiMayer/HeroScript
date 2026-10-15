using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Content;
using Core.Effects;
using Core.Run.Loadouts;

namespace Core.Tests.Run;

internal static class LoadoutTestContent
{
    internal static LoadoutPolicyDefinition Policy(InscriptionInventoryScope scope = InscriptionInventoryScope.Actor) => new()
    {
        LoadoutPolicyId = "loadout", InventoryScope = scope, Capacity = 6, AllowDuplicateDefinitions = false,
        InscriptionIds = ["focus"],
        Slots = [new() { SlotId = "one" }, new() { SlotId = "two" }],
        EquipmentWindows = [LoadoutEquipmentWindow.OutsideCombat]
    };

    internal static InscriptionDefinition Inscription(string id = "focus", float cost = 3) => new()
    {
        InscriptionId = id, EquipmentCost = cost, Tags = ["critical"], AttributeBonuses =
        [new() { AttributeId = "critical", Influence = new() { InfluenceId = "bonus", Channel = "critical", Bucket = "flat", Value = .25f } }]
    };

    internal static ContentBundle Bundle(InscriptionDefinition? item = null, LoadoutPolicyDefinition? policy = null)
    {
        var definitions = new Dictionary<string, Dictionary<string, object>>
        {
            ["inscriptions"] = new() { ["focus"] = item ?? Inscription() },
            ["loadout-policies"] = new() { ["loadout"] = policy ?? Policy() },
            ["calculation-pipelines"] = new() { ["critical"] = new CalculationPipelineDefinition
            { PipelineId = "critical", Channel = "critical", Buckets = [new() { BucketId = "flat", Operation = CalculationBucketOperation.Add }] } },
            ["effect-resolution-policies"] = new() { ["attributes"] = new EffectResolutionPolicyDefinition
            { PolicyId = "attributes", Rules = [new() { RuleId = "test", AmountPipelineId = "critical" }],
                Attributes = [new() { AttributeId = "critical", DisplayName = "Critical chance", PipelineId = "critical", DisplayScale = 100, DisplayUnit = "%" }] } }
        };
        return new()
        {
            Manifest = new() { ConfigName = "default", Revision = new string('a', 64), Artifacts = definitions.Select(pair =>
                new ContentArtifactManifest { Kind = pair.Key, Path = pair.Key + "/catalog.json", DefinitionCount = pair.Value.Count }).ToImmutableArray() },
            Artifacts = definitions.ToImmutableDictionary(pair => pair.Key + "/catalog.json", pair => JsonSerializer.SerializeToElement(pair.Value))
        };
    }

    internal static ContentRuntime Runtime() => ContentRuntime.Create(Bundle()).Value;
}
