using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Content;
using Core.Effects;
using Xunit;

namespace Core.Tests.Content;

public sealed class ContentGraphValidatorTests
{
    [Fact]
    public void Validate_RejectsMissingComponentBundleReference()
    {
        var bundle = Bundle(
            ("cards", "cards/catalog.json", new Dictionary<string, object>
            {
                ["strike"] = new { cardId = "strike", componentBundleIds = new[] { "missing_bundle" } }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("references missing component bundle missing_bundle", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsCanonicalCardComponents()
    {
        var bundle = Bundle(
            ("cards", "cards/catalog.json", new Dictionary<string, object>
            {
                ["strike"] = new
                {
                    cardId = "strike",
                    components = new[]
                    {
                        new { type = "disposition", componentId = "destination", order = 10, destination = "Discard" }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void Validate_RejectsDuplicateIdsAcrossArtifactsOfSameKind()
    {
        var bundle = Bundle(
            ("cards", "cards/a.json", new Dictionary<string, object>
            {
                ["same"] = new { cardId = "same" }
            }),
            ("cards", "cards/b.json", new Dictionary<string, object>
            {
                ["same"] = new { cardId = "same" }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains("Duplicate definition 'same'", result.Errors.Single());
    }

    [Fact]
    public void Validate_RejectsFiniteStatusWithoutExplicitDurationBoundary()
    {
        var bundle = Bundle(
            ("status-effects", "status-effects/status.json", new Dictionary<string, object>
            {
                ["burning"] = new
                {
                    statusId = "burning",
                    defaultDuration = 3
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(
            "status-effects/burning requires durationTickBoundary for finite duration",
            result.Errors);
    }

    [Fact]
    public void Validate_RejectsResourceChangingEffectWithoutExplicitResource()
    {
        var bundle = Bundle(
            ("actions", "actions/strike.json", new Dictionary<string, object>
            {
                ["strike"] = new
                {
                    actionId = "strike",
                    effects = new[] { new { effectId = "strike.damage", type = "DAMAGE", flatValue = 5 } }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(
            "actions/strike effect strike.damage (DAMAGE) requires targetResource",
            result.Errors);
    }

    [Fact]
    public void Validate_RejectsInvalidResourceThresholdPolicy()
    {
        var bundle = Bundle(
            ("resources", "resources/focus.json", new Dictionary<string, object>
            {
                ["focus"] = new
                {
                    resourceId = "focus",
                    displayName = "Focus",
                    defaultMin = 0,
                    defaultMax = 10,
                    defaultCurrent = 10,
                    costMultiplier = 1,
                    thresholdPolicies = new[]
                    {
                        new { policyId = "lose_focus", consequence = "DefeatOwner" }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(
            "resources/focus: Resource threshold comparison is required: lose_focus",
            result.Errors);
    }

    [Fact]
    public void Validate_RejectsEntityResourceOutsidePublishedGraph()
    {
        var bundle = Bundle(
            ("entities", "entities/mage.json", new Dictionary<string, object>
            {
                ["mage"] = new
                {
                    definitionId = "mage",
                    displayName = "Mage",
                    components = new object[]
                    {
                        new
                        {
                            type = "resources",
                            componentId = "resources",
                            pools = new Dictionary<string, object>
                            {
                                ["arcane_charge"] = new { current = 2, max = 3 }
                            }
                        }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(
            "entities/mage references missing resources/arcane_charge",
            result.Errors);
    }

    [Fact]
    public void Validate_RejectsRunResourceOutsidePublishedGraph()
    {
        var bundle = Bundle(
            ("runs", "runs/default.json", new Dictionary<string, object>
            {
                ["default"] = new
                {
                    runId = "default",
                    startingResources = new Dictionary<string, float> { ["credits"] = 10 }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains("runs/default references missing resources/credits", result.Errors);
    }

    [Fact]
    public void Validate_RejectsShopCostResourceOutsidePublishedGraph()
    {
        var bundle = Bundle(
            ("shops", "shops/test.json", new Dictionary<string, object>
            {
                ["test"] = new
                {
                    shopId = "test",
                    items = new[]
                    {
                        new
                        {
                            itemId = "card",
                            costs = new[] { new { resourceId = "credits", amount = 2 } }
                        }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains("shops/test references missing resources/credits", result.Errors);
    }

    [Fact]
    public void Validate_RejectsCalculationBindingResourceOutsidePublishedGraph()
    {
        var bundle = Bundle(
            ("calculation-pipelines", "calculation-pipelines/power.json", new Dictionary<string, object>
            {
                ["power"] = new
                {
                    pipelineId = "power",
                    channel = "effect_amount",
                    buckets = new[]
                    {
                        new { bucketId = "flat", order = 10, operation = "Add" }
                    },
                    resourceInfluenceBindings = new[]
                    {
                        new
                        {
                            bindingId = "actor.power",
                            scope = "Actor",
                            resourceId = "power",
                            field = "Current",
                            channel = "effect_amount",
                            bucket = "flat"
                        }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(
            "calculation-pipelines/power references missing resources/power",
            result.Errors);
    }

    [Fact]
    public void Validate_RejectsCalculationBindingForUnknownBucket()
    {
        var bundle = Bundle(
            ("resources", "resources/power.json", new Dictionary<string, object>
            {
                ["power"] = new
                {
                    resourceId = "power",
                    displayName = "Power",
                    defaultMin = 0,
                    defaultMax = 100,
                    defaultCurrent = 0,
                    costMultiplier = 1
                }
            }),
            ("calculation-pipelines", "calculation-pipelines/power.json", new Dictionary<string, object>
            {
                ["power"] = new
                {
                    pipelineId = "power",
                    channel = "effect_amount",
                    buckets = new[]
                    {
                        new { bucketId = "flat", order = 10, operation = "Add" }
                    },
                    resourceInfluenceBindings = new[]
                    {
                        new
                        {
                            bindingId = "actor.power",
                            scope = "Actor",
                            resourceId = "power",
                            channel = "effect_amount",
                            bucket = "missing"
                        }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unknown bucket", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsGambitWithMissingActionReference()
    {
        var bundle = Bundle(
            ("gambits", "gambits/rules.json", new Dictionary<string, object>
            {
                ["attack"] = new
                {
                    gambitId = "attack",
                    predicates = new[] { new { expression = "1" } },
                    action = new
                    {
                        actionType = "POWER",
                        actionId = "missing",
                        targetSelector = new { strategy = "FirstOrdinal", relationship = "Enemy" }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains("gambits/attack references missing actions/missing", result.Errors);
    }

    [Fact]
    public void Validate_RejectsGambitPredicateOutsideSharedFormulaVariables()
    {
        var bundle = Bundle(
            ("gambits", "gambits/rules.json", new Dictionary<string, object>
            {
                ["wait"] = new
                {
                    gambitId = "wait",
                    predicates = new[] { new { expression = "hardcoded_health" } },
                    action = new
                    {
                        actionType = "END_TURN",
                        targetSelector = new { strategy = "None" }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Unknown formula, variable", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsTurnOrderResourceOutsidePublishedGraph()
    {
        var bundle = Bundle(
            ("combat-rules", "combat-rules/test.json", new Dictionary<string, object>
            {
                ["test"] = new
                {
                    combatRulesId = "test",
                    defaultPhaseSequenceId = "phases",
                    turnOrder = new
                    {
                        strategy = "Resource",
                        recalculateAt = "RoundStart",
                        tieBreak = new { strategy = "StableActorId" },
                        resource = new
                        {
                            resourceId = "missing_speed",
                            direction = "Descending"
                        }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(
            "combat-rules/test references missing resources/missing_speed",
            result.Errors);
    }

    [Fact]
    public void Validate_RejectsConditionalTurnOrderOutsideSharedFormulaVariables()
    {
        var bundle = Bundle(
            ("combat-rules", "combat-rules/test.json", new Dictionary<string, object>
            {
                ["test"] = new
                {
                    combatRulesId = "test",
                    defaultPhaseSequenceId = "phases",
                    turnOrder = new
                    {
                        strategy = "Conditional",
                        recalculateAt = "ActivationEnd",
                        tieBreak = new { strategy = "StableActorId" },
                        conditional = new
                        {
                            scoreExpression = "ambient_clock",
                            direction = "Descending"
                        }
                    }
                }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Contains("conditional turnOrder", StringComparison.Ordinal) &&
            error.Contains("Unknown formula, variable", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsPhaseEffectsWithMissingReferences()
    {
        var sequence = PhaseSequence() with
        {
            Phases = PhaseSequence().Phases.Select(phase => phase.PhaseId == "start"
                ? phase with
                {
                    EntryEffects = [new EffectDefinition
                    {
                        Type = EffectType.APPLY_STATUS,
                        Target = EffectTarget.SELF,
                        StatusId = "missing_status"
                    }]
                }
                : phase).ToArray()
        };
        var bundle = Bundle(("phase-sequences", "phase-sequences/test.json",
            new Dictionary<string, object> { ["test"] = sequence }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(
            "references missing status-effects/missing_status", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsPhaseConditionOutsideSharedFormulaVariables()
    {
        var sequence = PhaseSequence() with
        {
            Phases = PhaseSequence().Phases.Select(phase => phase.PhaseId == "start"
                ? phase with
                {
                    Edges = [phase.Edges[0] with { Condition = "ambient_clock" }]
                }
                : phase).ToArray()
        };
        var bundle = Bundle(("phase-sequences", "phase-sequences/test.json",
            new Dictionary<string, object> { ["test"] = sequence }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Unknown formula, variable", StringComparison.Ordinal));
    }

    private static PhaseSequenceDefinition PhaseSequence() => new()
    {
        SequenceId = "test",
        EntryPhaseId = "start",
        Phases =
        [
            new PhaseDefinition
            {
                PhaseId = "start", Role = PhaseRole.Start, Order = 10,
                Edges = [new()
                {
                    EdgeId = "start-main", TargetPhaseId = "main",
                    Trigger = PhaseEdgeTrigger.Automatic
                }]
            },
            new PhaseDefinition
            {
                PhaseId = "main", Role = PhaseRole.Middle, Order = 20,
                AllowedActions = [ActionType.END_TURN],
                Edges = [new()
                {
                    EdgeId = "main-end", TargetPhaseId = "end",
                    Trigger = PhaseEdgeTrigger.ActivationExit
                }]
            },
            new PhaseDefinition { PhaseId = "end", Role = PhaseRole.End, Order = 30 }
        ]
    };

    private static ContentBundle Bundle(
        params (string Kind, string Path, Dictionary<string, object> Definitions)[] artifacts)
    {
        var payloads = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
            payloads[artifact.Path] = JsonSerializer.SerializeToElement(artifact.Definitions);
        return new ContentBundle
        {
            Manifest = new ContentManifest
            {
                ConfigName = "default",
                Revision = new string('a', 64),
                Artifacts = artifacts.Select(artifact => new ContentArtifactManifest
                {
                    Kind = artifact.Kind,
                    Path = artifact.Path,
                    DefinitionCount = artifact.Definitions.Count
                }).ToImmutableArray()
            },
            Artifacts = payloads.ToImmutable()
        };
    }
}
