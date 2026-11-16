using Core.Content;

namespace API.Contracts;

public static class AuthoringDefinitionKinds
{
    internal const string CardZones = "card-zones";
    internal const string CardResolutionFlows = "card-resolution-flows";
    internal const string CardPaymentFlows = "card-payment-flows";
    internal const string CardRarities = "card-rarities";
    internal const string CharacterAttributes = "character-attributes";
    private static readonly HashSet<string> Editable = new(StringComparer.Ordinal)
    {
        "cards", "combat-rules", "flow-rules", "phase-sequences", "modes",
        "capability-policies", "content-binding-policies", "replay-policies", "timeline-policies",
        "actions", "modifiers", "status-effects", "effect-resolution-policies", "gambits", "powers", "keywords",
        "resources", "actor-resource-lifecycle-policies", "calculation-pipelines", "formulas",
        "entities", "enemies", "enemy-pools", "companions", "races", "supports", "support-routines",
        "decks", "card-pools", "deck-composition-policies", "deck-modules", "deck-compartment-profiles",
        "deck-compartment-policies", "loadout-policies", "support-loadout-policies", "inscriptions",
        "runs", "run-progression-policies", "profile-progress-policies", "daily-challenges", "dialogues",
        "run-events", "preparations", "shops", "relics", "boards", "zones"
    };

    public static bool Supports(string kind) => Editable.Contains(kind);

    public static string? Identity(string kind) => Supports(kind)
        ? ContentKindRegistry.Canonical.Get(kind).Value.IdentityProperty : null;

    public static IReadOnlyDictionary<string, string> References(string kind) => kind switch
    {
        "cards" => CardReferences(),
        "runs" => WithEffectReferences(new()
        {
            ["/playerDefinitionId"] = "entities", ["/startingResources/@key"] = "resources",
            ["/initialCardZoneId"] = CardZones, ["/startingCards/*"] = "cards",
            ["/mapNodes/*/nextNodeIds/*"] = "local:mapNodes/nodeId"
        }, "/mapNodes/*/entryEffects/*", "/mapNodes/*/exitEffects/*"),
        "profile-progress-policies" => new Dictionary<string, string>
        {
            ["/contributingModeIds/*"] = "modes", ["/unlocks/*/condition/runDefinitionId"] = "runs",
            ["/unlocks/*/condition/unlockId"] = "local:unlocks/unlockId"
        },
        "daily-challenges" => new Dictionary<string, string>
        {
            ["/runDefinitionId"] = "runs", ["/modeId"] = "modes", ["/scoreResourceWeights/@key"] = "resources"
        },
        "dialogues" => WithEffectReferences(new()
        {
            ["/startNodeId"] = "local:nodes/nodeId", ["/nodes/*/choices/*/nextNodeId"] = "local:nodes/nodeId",
            ["/nodes/*/choices/*/costs/*/resourceId"] = "resources"
        }, "/nodes/*/entryEffects/*", "/nodes/*/choices/*/effects/*"),
        "shops" => new Dictionary<string, string>
        {
            ["/cardPoolId"] = "card-pools", ["/items/*/cardId"] = "cards",
            ["/items/*/modifierItemDefinitionId"] = "card-modifier-items", ["/items/*/costs/*/resourceId"] = "resources",
            ["/pricing/rarityMultipliers/@key"] = CardRarities, ["/pricing/tagMultipliers/@key"] = "tags",
            ["/reroll/baseCosts/*/resourceId"] = "resources", ["/reroll/costsPerReroll/*/resourceId"] = "resources"
        },
        "preparations" => WithEffectReferences(new()
        {
            ["/options/*/costs/*/resourceId"] = "resources", ["/options/*/grantedCardIds/*"] = "cards",
            ["/options/*/applyModifiers/*/modifierId"] = "modifiers"
        }, "/options/*/effects/*"),
        "relics" => WithEffectReferences(new()
        {
            ["/reactions/*/resourceId"] = "resources", ["/resourceRestrictions/*/resourceId"] = "resources",
            ["/influences/*/requiredTags/*"] = "tags", ["/influences/*/excludedTags/*"] = "tags"
        }, "/triggers/*/effects/*"),
        "card-pools" => new Dictionary<string, string>
        {
            ["/explicitCardIds/*"] = "cards", ["/includeTags/*"] = "tags", ["/excludeTags/*"] = "tags",
            ["/rarityWeights/@key"] = CardRarities
        },
        "deck-composition-policies" => new Dictionary<string, string>
        {
            ["/groups/*/cardIds/*"] = "cards", ["/groups/*/startingCardIds/*"] = "cards",
            ["/groups/*/moduleIds/*"] = "deck-modules", ["/groups/*/startingModuleId"] = "deck-modules"
        },
        "deck-modules" => new Dictionary<string, string>
        {
            ["/cardIds/*"] = "cards", ["/startingCardIds/*"] = "local:cardIds"
        },
        "deck-compartment-profiles" => new Dictionary<string, string>
        {
            ["/compartments/*/grants/*/bundleId"] = "card-component-bundles",
            ["/compartments/*/grantedTags/*"] = "tags", ["/compartments/*/requiredTags/*"] = "tags",
            ["/compartments/*/excludedTags/*"] = "tags"
        },
        "loadout-policies" => new Dictionary<string, string>
        {
            ["/inscriptionIds/*"] = "inscriptions", ["/startingInscriptionIds/*"] = "local:inscriptionIds",
            ["/slots/*/requiredTags/*"] = "tags", ["/stations/*/pool/@key"] = "local:inscriptionIds",
            ["/stations/*/costs/*/resourceId"] = "resources"
        },
        "support-loadout-policies" => new Dictionary<string, string>
        {
            ["/supportIds/*"] = "supports", ["/startingOwnedSupportIds/*"] = "local:supportIds",
            ["/startingSupportId"] = "local:startingOwnedSupportIds"
        },
        "inscriptions" => new Dictionary<string, string>
        {
            ["/tags/*"] = "tags", ["/allowedActorDefinitionIds/*"] = "entities",
            ["/attributeBonuses/*/attributeId"] = CharacterAttributes
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
        "actions" => new Dictionary<string, string>
        {
            ["/costs/costs/*/resourceId"] = "resources",
            ["/effects/*/targetResource"] = "resources",
            ["/effects/*/statusId"] = "status-effects"
        },
        "modifiers" => new Dictionary<string, string> { ["/tags/*"] = "tags" },
        "resources" => new Dictionary<string, string>
        {
            ["/tags/*"] = "tags", ["/regeneration/calculationPipelineId"] = "calculation-pipelines"
        },
        "actor-resource-lifecycle-policies" => new Dictionary<string, string> { ["/rules/*/resourceId"] = "resources" },
        "entities" => new Dictionary<string, string>
        {
            ["/components/*/pools/@key"] = "resources",
            ["/components/*/abilityIds/*"] = "actions",
            ["/components/*/profileId"] = "deck-compartment-profiles"
        },
        "enemy-pools" => new Dictionary<string, string> { ["/entityDefinitionIds/*"] = "entities" },
        "supports" => new Dictionary<string, string>
        {
            ["/entityDefinitionId"] = "entities", ["/routineIds/*"] = "support-routines", ["/defaultRoutineId"] = "local:routineIds"
        },
        "support-routines" => new Dictionary<string, string> { ["/gambitIds/*"] = "gambits" },
        "calculation-pipelines" => new Dictionary<string, string>
        {
            ["/buckets/*/stageId"] = "local:stages/stageId",
            ["/resourceInfluenceBindings/*/resourceId"] = "resources",
            ["/resourceInfluenceBindings/*/bucket"] = "local:buckets/bucketId",
            ["/statInfluenceBindings/*/bucket"] = "local:buckets/bucketId",
            ["/resourceInfluenceBindings/*/requiredTags/*"] = "tags",
            ["/resourceInfluenceBindings/*/excludedTags/*"] = "tags",
            ["/statInfluenceBindings/*/requiredTags/*"] = "tags",
            ["/statInfluenceBindings/*/excludedTags/*"] = "tags"
        },
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

    private static Dictionary<string, string> CardReferences() =>
        WithEffectReferences(new()
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
        }, "/components/*/effect", "/components/*/effects/*");

    private static Dictionary<string, string> WithEffectReferences(Dictionary<string, string> references, params string[] paths)
    {
        foreach (var path in paths)
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
