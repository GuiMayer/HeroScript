using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Determinism;
using Core.GameplayEvents.Responses;
using Core.Run;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResponseRuntimePresenceTests
{
    [Fact]
    public void PresenceIsDerivedPerImmutableRuntimeAndCannotCrossContaminateSettingsOrRevisions()
    {
        ContentBundle Bundle(string config, bool responses)
        {
            var artifacts = ImmutableDictionary<string, JsonElement>.Empty.Add("relics/test.json", JsonSerializer.SerializeToElement(
                new Dictionary<string, RelicDefinition> { ["test"] = new() { RelicId = "test",
                    Responses = responses ? [GameplayResponseCompilationTests.Definition()] : null } }));
            return new() { Manifest = new() { Revision = "same-test-revision", ConfigName = config,
                Artifacts = [new() { Kind = "relics", Path = "relics/test.json", DefinitionCount = 1 }] }, Artifacts = artifacts };
        }
        var registry = new ContentKindRegistry(ContentKindRegistry.Default.Kinds, allowShadowResponseDeclarations: true);
        var first = Bundle("one", true); var original = CanonicalJson.ComputeHash(first);
        var enabled = ContentRuntime.Create(first, registry);
        var disabled = ContentRuntime.Create(Bundle("two", false), registry);
        Assert.True(enabled.IsSuccess, enabled.IsFailure ? enabled.Error : null);
        Assert.True(disabled.IsSuccess, disabled.IsFailure ? disabled.Error : null);
        Assert.True(enabled.Value.ContainsGameplayResponses); Assert.False(disabled.Value.ContainsGameplayResponses);
        Assert.Equal(original, CanonicalJson.ComputeHash(first));
        Assert.True(ContentRuntime.Create(first).IsFailure);
    }
}
