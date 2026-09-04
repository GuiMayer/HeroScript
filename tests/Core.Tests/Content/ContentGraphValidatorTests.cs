using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Xunit;

namespace Core.Tests.Content;

public sealed class ContentGraphValidatorTests
{
    [Fact]
    public void Validate_RejectsMissingCrossModuleReference()
    {
        var bundle = Bundle(
            ("cards", "cards/catalog.json", new Dictionary<string, object>
            {
                ["strike"] = new { cardId = "strike", actionId = "missing_action" }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains(
            "cards/strike references missing actions/missing_action",
            result.Errors);
    }

    [Fact]
    public void Validate_AcceptsResolvedCrossModuleReference()
    {
        var bundle = Bundle(
            ("cards", "cards/catalog.json", new Dictionary<string, object>
            {
                ["strike"] = new { cardId = "strike", actionId = "strike" }
            }),
            ("actions", "actions/strike.json", new Dictionary<string, object>
            {
                ["strike"] = new { actionId = "strike", effects = Array.Empty<object>() }
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
                ["same"] = new { cardId = "same", actionId = "strike" }
            }),
            ("cards", "cards/b.json", new Dictionary<string, object>
            {
                ["same"] = new { cardId = "same", actionId = "strike" }
            }));

        var result = new ContentGraphValidator().Validate(bundle);

        Assert.False(result.IsValid);
        Assert.Contains("Duplicate definition 'same'", result.Errors.Single());
    }

    [Fact]
    public void Validate_RejectsFiniteStatusWithoutExplicitDurationBoundary()
    {
        var bundle = Bundle(
            ("status-effects", "StatusEffects/status.json", new Dictionary<string, object>
            {
                ["burning"] = new
                {
                    statusId = "burning",
                    behavior = "DAMAGE_OVER_TIME",
                    timing = "END_OF_TURN",
                    defaultDuration = 3,
                    triggerBoundary = "EndActivation"
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
                    type = "PLAYER",
                    displayName = "Mage",
                    resources = new
                    {
                        resources = new Dictionary<string, object>
                        {
                            ["arcane_charge"] = new { current = 2, max = 3 }
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
