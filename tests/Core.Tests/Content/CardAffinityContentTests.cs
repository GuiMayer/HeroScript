using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Mods;
using Xunit;

namespace Core.Tests.Content;

public sealed class CardAffinityContentTests
{
    [Theory]
    [InlineData("basic_attack", "burning", EffectTarget.TARGET)]
    [InlineData("heal", "regeneration", EffectTarget.SELF)]
    public async Task DefaultAffinity_IsOneAuthoredTransformationWithActionSpecificConsequences(string cardId, string statusId, EffectTarget target)
    {
        var root = ResourceProviderFactory.FindProjectRoot()!;
        var compilation = await new SettingCompiler([new DirectoryPackageProvider("base-game", Path.Combine(root, "data", "configs"))]).CompileAsync("default");
        Assert.True(compilation.IsSuccess, compilation.IsFailure ? compilation.Error : null);
        var graph = new ContentGraphValidator().Validate(compilation.Value.Bundle);
        Assert.True(graph.IsValid, string.Join("; ", graph.Errors));
        var runtime = ContentRuntime.Create(compilation.Value.Bundle).Value;
        var definition = new CardContentCompiler().Compile(cardId, runtime).Value;
        var authored = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", "core.ember.affinity").Value;
        var closed = CardBundleCompiler.Seal(authored, runtime);
        Assert.True(closed.IsSuccess, closed.IsFailure ? closed.Error : null);
        var card = CardInstanceUpgradeTransitions.Apply(new() { CardInstanceId = Guid.Parse("00000000-0000-8000-8000-000000000007"), DefinitionId = cardId },
            closed.Value, runtime.Manifest.Revision).Value;
        var resolved = new EffectiveCardResolver().Resolve(definition, card);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : null);
        Assert.Contains("ember_affinity", resolved.Value.Tags);
        var residual = Assert.Single(Assert.Single(resolved.Value.All<CardEffectComponentDefinition>()).Effect.ChainedEffects!);
        Assert.Equal(EffectType.APPLY_STATUS, residual.Type); Assert.Equal(statusId, residual.StatusId); Assert.Equal(target, residual.Target);
        Assert.True(GameplayContentValidator.ValidateCardContainer(runtime, "affinity-test", resolved.Value.Components).IsSuccess);
        Assert.Equal(CanonicalJson.ComputeHash<CardComponentDefinition>(definition.SingleOrDefault<CardCostComponentDefinition>()!),
            CanonicalJson.ComputeHash<CardComponentDefinition>(resolved.Value.SingleOrDefault<CardCostComponentDefinition>()!));
        Assert.Single(resolved.Value.CompositionTrace);
        Assert.Equal("affinity", Assert.Single(resolved.Value.AppliedUpgrades).SlotId);
    }
}
