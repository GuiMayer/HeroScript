using System.Text.Json;
using Core.Config;
using Core.Content;
using Core.Run.Content;
using Mods;
using Xunit;

namespace Core.Tests.Content;

public sealed class UnoClashParityTests
{
    private static JsonDocument Load(string kind) => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        ResourceProviderFactory.FindProjectRoot()!, "data", "configs", "uno-clash", "Resources", kind, "uno.json")));

    private static JsonElement[] Effects(JsonElement card) => card.GetProperty("components").EnumerateArray()
        .Where(component => component.GetProperty("type").GetString() == "effect")
        .Select(component => component.GetProperty("effect")).ToArray();

    private static int ExtraActions(IEnumerable<JsonElement> effects) => effects
        .Where(effect => effect.TryGetProperty("targetResource", out var resource) && resource.GetString() == "uno_energy")
        .Sum(effect =>
        {
            Assert.Equal("SELF", effect.GetProperty("target").GetString());
            Assert.Equal("ADD", effect.GetProperty("operation").GetString());
            return effect.GetProperty("flatValue").GetInt32();
        });

    [Theory]
    [InlineData("red")]
    [InlineData("blue")]
    [InlineData("green")]
    [InlineData("yellow")]
    public void ReverseAndDrawTwo_HaveSymmetricTurnAndPenaltyEffects(string color)
    {
        using var cards = Load("cards");
        using var actions = Load("actions");
        using var rules = Load("combat-rules");
        // Refunding the action is ineffective if the controller forcibly ends its turn.
        Assert.False(rules.RootElement.GetProperty("uno_combat").GetProperty("flow")
            .GetProperty("ai").GetProperty("autoEndAfterAction").GetBoolean());
        foreach (var value in new[] { "reverse", "draw2" })
        {
            var player = Effects(cards.RootElement.GetProperty($"uno_{color}_{value}"));
            var bot = actions.RootElement.GetProperty(value == "draw2"
                ? $"uno_ai_{color}_draw2" : $"uno_ai_play_{color}_reverse");
            var botEffects = bot.GetProperty("effects").EnumerateArray().ToArray();
            Assert.Equal(value == "reverse" ? 1 : 0, ExtraActions(player));
            Assert.Equal(ExtraActions(player), ExtraActions(botEffects));
            Assert.Equal(1, bot.GetProperty("costs").GetProperty("costs").EnumerateArray()
                .Single(cost => cost.GetProperty("resourceId").GetString() == "uno_energy")
                .GetProperty("amount").GetInt32());
            foreach (var table in player.Where(effect => effect.GetProperty("tags").EnumerateArray()
                         .Any(tag => tag.GetString() == "table-state")))
            {
                var matching = Assert.Single(botEffects, effect =>
                    effect.TryGetProperty("targetResource", out var resource) &&
                    resource.GetString() == table.GetProperty("targetResource").GetString());
                Assert.Equal(table.GetProperty("flatValue").GetInt32(), matching.GetProperty("flatValue").GetInt32());
            }
            if (value == "draw2")
            {
                Assert.DoesNotContain(botEffects, effect => effect.GetProperty("type").GetString() == "CARD_ZONE_FLOW");
                foreach (var effects in new[] { player, botEffects })
                {
                    var stack = Assert.Single(effects, effect => effect.TryGetProperty("targetResource", out var resource) &&
                        resource.GetString() == "uno_draw2_pending" && effect.GetProperty("target").GetString() == "TARGET");
                    Assert.Equal("source.resources.uno_draw2_pending.current + 2", stack.GetProperty("formulaValue").GetString());
                    Assert.Equal("ADD", stack.GetProperty("operation").GetString());
                    Assert.Equal(0, Assert.Single(effects, effect => effect.TryGetProperty("targetResource", out var resource) &&
                        resource.GetString() == "uno_draw2_pending" && effect.GetProperty("target").GetString() == "SELF")
                        .GetProperty("flatValue").GetInt32());
                }
            }
        }
    }

    [Fact]
    public void OtherSpecialCards_PreserveTheirTurnAndDrawSemantics()
    {
        using var cards = Load("cards");
        using var actions = Load("actions");
        foreach (var color in new[] { "red", "blue", "green", "yellow" })
        {
            Assert.Equal(1, ExtraActions(Effects(cards.RootElement.GetProperty($"uno_{color}_skip"))));
            Assert.Equal(0, ExtraActions(actions.RootElement.GetProperty($"uno_ai_wild_{color}")
                .GetProperty("effects").EnumerateArray()));
        }
        Assert.Equal(0, ExtraActions(Effects(cards.RootElement.GetProperty("uno_wild"))));
        var drawFour = Effects(cards.RootElement.GetProperty("uno_wild_draw4"));
        Assert.Equal(1, ExtraActions(drawFour));
        Assert.Equal(4, Assert.Single(drawFour, effect =>
                effect.GetProperty("targetResource").GetString() == "ai_hand_count")
            .GetProperty("flatValue").GetInt32());
    }

    [Fact]
    public void AccumulatedDrawTwo_RequiresAcceptanceAndNeverMixesWithWildFour()
    {
        using var cards = Load("cards");
        using var actions = Load("actions");
        foreach (var card in cards.RootElement.EnumerateObject())
        {
            var guards = card.Value.GetProperty("components").EnumerateArray().Where(component =>
                component.GetProperty("componentId").GetString() == "condition.no_draw2_debt").ToArray();
            if (card.Name.EndsWith("_draw2")) Assert.Empty(guards);
            else Assert.Equal("1 - source.resources.uno_draw2_pending.current",
                Assert.Single(guards).GetProperty("expression").GetString());
        }
        foreach (var id in new[] { "uno_accept_draw2", "uno_ai_accept_draw2" })
        {
            var action = actions.RootElement.GetProperty(id);
            Assert.Equal(0, ExtraActions(action.GetProperty("effects").EnumerateArray()));
            Assert.Contains(action.GetProperty("costs").GetProperty("costs").EnumerateArray(), cost =>
                cost.GetProperty("resourceId").GetString() == "uno_draw2_due" && cost.GetProperty("amount").GetInt32() == 1);
        }
        var draw = actions.RootElement.GetProperty("uno_accept_draw2").GetProperty("effects")[0];
        Assert.Equal("source.resources.uno_draw2_pending.current",
            draw.GetProperty("parameters")[0].GetProperty("formulaValue").GetString());
    }

    [Fact]
    public void BuyingOnce_PreservesThePlayAndResetsAtNextActivationForBothSides()
    {
        using var actions = Load("actions");
        using var resources = Load("resources");
        using var entities = Load("entities");
        foreach (var entry in actions.RootElement.EnumerateObject().Where(entry =>
                     entry.Name == "uno_draw_card" || entry.Name.StartsWith("uno_ai_draw_")))
        {
            var costs = entry.Value.GetProperty("costs").GetProperty("costs").EnumerateArray().ToArray();
            var playCost = Assert.Single(costs, cost => cost.GetProperty("resourceId").GetString() == "uno_energy")
                .GetProperty("amount").GetInt32();
            Assert.Equal(playCost, ExtraActions(entry.Value.GetProperty("effects").EnumerateArray()));
            Assert.Equal(1, Assert.Single(costs, cost => cost.GetProperty("resourceId").GetString() == "uno_draw_available")
                .GetProperty("amount").GetInt32());
        }
        var resource = resources.RootElement.GetProperty("uno_draw_available");
        Assert.False(resource.GetProperty("canExceedMax").GetBoolean());
        Assert.Equal(1, resource.GetProperty("defaultMax").GetInt32());
        Assert.True(resource.GetProperty("regeneration").GetProperty("enabled").GetBoolean());
        Assert.Equal("START_TURN", resource.GetProperty("regeneration").GetProperty("timing").GetString());
        Assert.Equal(1, resource.GetProperty("regeneration").GetProperty("amountPerTurn").GetInt32());
        foreach (var entity in entities.RootElement.EnumerateObject())
            Assert.Equal(1, entity.Value.GetProperty("components").EnumerateArray()
                .Single(component => component.GetProperty("type").GetString() == "resources")
                .GetProperty("pools").GetProperty("uno_draw_available").GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task UpdatedPackage_CompilesAndPassesContentValidation()
    {
        var root = ResourceProviderFactory.FindProjectRoot()!;
        var compilation = await new SettingCompiler([
            new DirectoryPackageProvider("shipped", Path.Combine(root, "data", "configs"))
        ]).CompileAsync("uno-clash");
        Assert.True(compilation.IsSuccess, compilation.IsFailure ? compilation.Error : null);
        var validation = new ContentGraphValidator().Validate(compilation.Value.Bundle);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        var runtime = ContentRuntime.Create(compilation.Value.Bundle);
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        using var cards = Load("cards");
        foreach (var card in cards.RootElement.EnumerateObject())
        {
            var compiled = new CardContentCompiler().Compile(card.Name, runtime.Value);
            Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        }
    }
}
