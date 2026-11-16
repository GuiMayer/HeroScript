using Core.Content;

namespace API.Contracts;

public static class AuthoringDefinitionKinds
{
    private static readonly HashSet<string> Editable = new(StringComparer.Ordinal)
    {
        "cards", "combat-rules", "flow-rules", "phase-sequences", "modes",
        "capability-policies", "content-binding-policies", "replay-policies", "timeline-policies"
    };

    public static bool Supports(string kind) => Editable.Contains(kind);

    public static string? Identity(string kind) => Supports(kind)
        ? ContentKindRegistry.Canonical.Get(kind).Value.IdentityProperty : null;

    public static IReadOnlyDictionary<string, string> References(string kind) => kind switch
    {
        "cards" => new Dictionary<string, string>
        {
            ["/tags"] = "tags", ["/componentBundles/*/bundleId"] = "card-component-bundles"
        },
        "combat-rules" => new Dictionary<string, string>
        {
            ["/defaultPhaseSequenceId"] = "phase-sequences", ["/flow/actionBudget/resourceId"] = "resources"
        },
        "phase-sequences" => new Dictionary<string, string>
        {
            ["/entryPhaseId"] = "local:phases/phaseId",
            ["/phases/*/edges/*/targetPhaseId"] = "local:phases/phaseId"
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
}
