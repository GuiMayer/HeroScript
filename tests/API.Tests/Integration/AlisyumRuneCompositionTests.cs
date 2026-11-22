using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Content;
using Core.Run;
using Core.Run.Content;
using Core.Effects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class AlisyumRuneCompositionTests
{
    [Fact]
    public async Task PublishedRunesComposeIndividualCardsWithoutReplacingTheirBudget()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var revisions = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/revisions?configName=alisyum");
        var revision = revisions.GetProperty("currentRevision").GetString()!;
        var runtime = factory.Services.GetRequiredService<IContentRuntimeResolver>().Resolve(revision, "alisyum").Value;
        var compiler = new CardContentCompiler();
        var resolver = new EffectiveCardResolver();
        CardInstanceState Apply(CardInstanceState card, string suffix)
        {
            var item = runtime.GetDefinition<CardModifierItemDefinition>("card-modifier-items", "obor_overwrite_" + suffix + "_item").Value;
            Assert.True(item.Validate(runtime).IsSuccess);
            var upgrade = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", item.UpgradeId).Value;
            var closed = CardBundleCompiler.Seal(upgrade, runtime).Value;
            return CardInstanceUpgradeTransitions.Apply(card, closed, revision).Value;
        }
        var strike = new CardInstanceState { CardInstanceId = Guid.NewGuid(), DefinitionId = "alisyum_basic_strike" };
        var compiled = compiler.Compile(strike.DefinitionId, runtime).Value;
        var original = resolver.Resolve(compiled, strike).Value;
        var modified = resolver.Resolve(compiled, Apply(Apply(strike, "ember"), "fragment")).Value;
        var originalMain = original.All<CardEffectComponentDefinition>().Single(x => x.ComponentId == "main").Effect;
        var main = modified.All<CardEffectComponentDefinition>().Single(x => x.ComponentId == "main").Effect;
        Assert.Equal(1, originalMain.Repeat);
        Assert.Equal(3, main.Repeat);
        Assert.Equal(6f, main.Parameters.Single(x => x.Parameter == EffectNumericParameter.Amount).FlatValue);
        Assert.NotNull(main.Parameters.Single().Distribution);
        Assert.Equal("alisyum_burn", modified.All<CardEffectComponentDefinition>().Single(x => x.ComponentId == "rune.affinity").Effect.StatusId);
        Assert.Empty(strike.Upgrades);
        var magma = new CardInstanceState { CardInstanceId = Guid.NewGuid(), DefinitionId = "pyro_magma_slash" };
        var rewritten = resolver.Resolve(compiler.Compile(magma.DefinitionId, runtime).Value, Apply(magma, "frost_magma")).Value;
        Assert.DoesNotContain(rewritten.All<CardEffectComponentDefinition>(), x => x.Effect.StatusId == "alisyum_burn");
        Assert.Contains(rewritten.All<CardEffectComponentDefinition>(), x => x.Effect.StatusId == "alisyum_frostbite");
    }
}
