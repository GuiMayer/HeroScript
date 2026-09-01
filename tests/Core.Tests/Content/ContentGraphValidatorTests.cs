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
