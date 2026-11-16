using Core.Content;

namespace API.Contracts;

public static class AuthoringDefinitionKinds
{
    internal const string CardZones = "card-zones";
    internal const string CardResolutionFlows = "card-resolution-flows";
    internal const string CardPaymentFlows = "card-payment-flows";
    private static readonly HashSet<string> Editable = new(StringComparer.Ordinal)
    {
        "cards", "combat-rules", "flow-rules", "phase-sequences", "modes",
        "capability-policies", "content-binding-policies", "replay-policies", "timeline-policies",
        "actions", "modifiers", "status-effects", "effect-resolution-policies", "gambits", "powers", "keywords"
    };

    public static bool Supports(string kind) => Editable.Contains(kind);

    public static string? Identity(string kind) => Supports(kind)
        ? ContentKindRegistry.Canonical.Get(kind).Value.IdentityProperty : null;

    public static IReadOnlyDictionary<string, string> References(string kind) => kind switch
    {
        "cards" => CardReferences(),
        "combat-rules" => new Dictionary<string, string>
        {
            ["/defaultPhaseSequenceId"] = "phase-sequences", ["/flow/actionBudget/resourceId"] = "resources"
        },
        "phase-sequences" => new Dictionary<string, string>
        {
            ["/entryPhaseId"] = "local:phases/phaseId",
            ["/phases/*/edges/*/targetPhaseId"] = "local:phases/phaseId"
        },
        "actions" => new Dictionary<string, string>
        {
            ["/costs/costs/*/resourceId"] = "resources",
            ["/effects/*/targetResource"] = "resources",
            ["/effects/*/statusId"] = "status-effects"
        },
        "modifiers" => new Dictionary<string, string> { ["/tags/*"] = "tags" },
        "gambits" => new Dictionary<string, string>
        {
            ["/action/actionId"] = "actions", ["/action/cardDefinitionId"] = "cards",
            ["/action/targetSelector/resourceId"] = "resources"
        },
        "status-effects" => new Dictionary<string, string>
        {
            ["/tags/*"] = "tags",
            ["/triggers/*/effects/*/targetResource"] = "resources",
            ["/triggers/*/effects/*/statusId"] = "status-effects"
        },
        "modes" => new Dictionary<string, string>
        {
            ["/runDefinitionId"] = "runs", ["/flowRulesId"] = "flow-rules", ["/combatRulesId"] = "combat-rules",
            ["/replayPolicyId"] = "replay-policies", ["/timelinePolicyId"] = "timeline-policies",
            ["/contentBindingPolicyId"] = "content-binding-policies", ["/capabilityPolicyId"] = "capability-policies",
            ["/cardZoneSystemId"] = "card-zone-systems", ["/effectResolutionPolicyId"] = "effect-resolution-policies",
            ["/progressionPolicyId"] = "run-progression-policies", ["/actorResourceLifecyclePolicyId"] = "actor-resource-lifecycle-policies",
            ["/profileProgressPolicyId"] = "profile-progress-policies", ["/loadoutPolicyId"] = "loadout-policies",
            ["/deckCompartmentPolicyId"] = "deck-compartment-policies", ["/deckCompositionPolicyId"] = "deck-composition-policies",
            ["/supportLoadoutPolicyId"] = "support-loadout-policies", ["/calculationPipelineIds/*"] = "calculation-pipelines",
            ["/cardPoolIds/*"] = "card-pools", ["/enemyPoolIds/*"] = "enemy-pools"
        },
        _ => new Dictionary<string, string>()
    };

    private static Dictionary<string, string> CardReferences()
    {
        var references = new Dictionary<string, string>
        {
            ["/tags"] = "tags", ["/componentBundles/*/bundleId"] = "card-component-bundles",
            ["/components/*/costs/costs/*/resourceId"] = "resources",
            ["/components/*/costs/alternativeCosts/*/costs/*/resourceId"] = "resources",
            ["/components/*/selectionResourceId"] = "resources",
            ["/components/*/cardZoneResolutionFlowId"] = CardResolutionFlows,
            ["/components/*/zonePayments/*/sourceZoneId"] = CardZones,
            ["/components/*/zonePayments/*/targetZoneId"] = CardZones,
            ["/components/*/zonePayments/*/flowId"] = CardPaymentFlows,
            ["/components/*/zonePayments/*/costOptionId"] = "local:costs/alternativeCosts/optionId",
            ["/components/*/zonePayments/*/requiredTags/*"] = "tags",
            ["/components/*/zonePayments/*/excludedTags/*"] = "tags",
            ["/components/*/requiredTags/*"] = "tags",
            ["/components/*/excludedTags/*"] = "tags"
        };
        foreach (var path in new[] { "/components/*/effect", "/components/*/effects/*" })
        {
            references[$"{path}/statusId"] = "status-effects";
            references[$"{path}/targetResource"] = "resources";
            references[$"{path}/selectionResourceId"] = "resources";
            references[$"{path}/continuation/selectionResourceId"] = "resources";
            references[$"{path}/dispel/statusIds/*"] = "status-effects";
            references[$"{path}/dispel/requiredTags/*"] = "tags";
            references[$"{path}/modifierId"] = "modifiers";
            references[$"{path}/cardDefinitionId"] = "cards";
            references[$"{path}/calculationPipelineId"] = "calculation-pipelines";
            references[$"{path}/continuation/overflowPipelineId"] = "calculation-pipelines";
            references[$"{path}/parameters/*/pipelineId"] = "calculation-pipelines";
            references[$"{path}/condensationRecipeId"] = "condensation-recipes";
            foreach (var field in new[] { "tags", "requiredTags", "excludedTags" })
                references[$"{path}/{field}/*"] = "tags";
        }
        return references;
    }
}
