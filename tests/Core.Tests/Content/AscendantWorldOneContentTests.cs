using Core.Config;
using Core.Content;
using Core.Run;
using Core.Run.Content;
using System.Collections.Immutable;
using Mods;
using Xunit;

namespace Core.Tests.Content;

public sealed class AscendantWorldOneContentTests
{
    [Fact]
    public async Task WorldOneGraph_EveryRouteEndsAtBossWithNineFightsAndBuildOpportunities()
    {
        var runtime = await Compile();
        var run = runtime.GetDefinition<RunDefinition>("runs", "ascendant_world_one_run").Value;
        var nodes = run.MapNodes.ToDictionary(node => node.NodeId);
        var paths = new List<List<RunMapNodeDefinition>>();
        void Visit(string id, List<RunMapNodeDefinition> prefix)
        {
            Assert.DoesNotContain(prefix, node => node.NodeId == id);
            var path = new List<RunMapNodeDefinition>(prefix) { nodes[id] };
            if (nodes[id].NextNodeIds.Count == 0) paths.Add(path);
            else foreach (var next in nodes[id].NextNodeIds) Visit(next, path);
        }
        Visit(run.MapNodes[0].NodeId, []);
        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            Assert.Equal("world_one_boss", path[^1].NodeId);
            Assert.Equal(9, path.Count(node => node.Activity.Type == RunActivityType.Encounter));
            Assert.Equal(14, path.Count(node => node.Activity.Type is not (RunActivityType.CardSelection or RunActivityType.RelicReward)));
            Assert.True(path.Count(node => node.Activity.Type == RunActivityType.CardUpgrade) >= 2);
            Assert.Contains(path, node => node.Activity.Type == RunActivityType.Shop);
            Assert.Contains(path, node => node.NodeId.StartsWith("s12_", StringComparison.Ordinal));
        }
        Assert.Contains(paths, path => path.All(node => !node.NodeId.StartsWith("s09_", StringComparison.Ordinal) || node.NodeId == "s09_patrol"));
        Assert.Equal(nodes.Count, paths.SelectMany(path => path).Select(node => node.NodeId).Distinct().Count());
    }

    [Fact]
    public async Task EveryDeclaredTransformation_AppliesToAllItsEligibleCardsWithoutMutatingBase()
    {
        var runtime = await Compile();
        var root = ResourceProviderFactory.FindProjectRoot()!;
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "data/configs/ascendant/Resources/card-upgrades/world_one.json")));
        Assert.Equal(12, json.RootElement.EnumerateObject().Count());
        var compiler = new CardContentCompiler();
        foreach (var entry in json.RootElement.EnumerateObject())
        {
            var upgrade = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", entry.Name).Value;
            foreach (var id in upgrade.CardDefinitionIds)
            {
                var baseCard = compiler.Compile(id, runtime).Value;
                var before = Core.Determinism.CanonicalJson.ComputeHash(baseCard);
                var applied = CardInstanceUpgradeTransitions.Apply(new CardInstanceState
                {
                    CardInstanceId = Guid.Parse("10000000-0000-4000-8000-000000000001"), DefinitionId = id
                }, upgrade, runtime.Manifest.Revision);
                Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
                var effective = new EffectiveCardResolver().Resolve(baseCard, applied.Value);
                Assert.True(effective.IsSuccess, effective.IsFailure ? $"{entry.Name}/{id}: {effective.Error}" : null);
                Assert.Equal(before, Core.Determinism.CanonicalJson.ComputeHash(baseCard));
            }
        }
    }

    internal static async Task<ContentRuntime> Compile()
    {
        var root = ResourceProviderFactory.FindProjectRoot();
        Assert.NotNull(root);
        var compilation = await new SettingCompiler([
            new DirectoryPackageProvider("shipped", Path.Combine(root!, "data", "configs"))
        ]).CompileAsync("ascendant");
        Assert.True(compilation.IsSuccess, compilation.IsFailure ? compilation.Error : null);
        var validation = new ContentGraphValidator().Validate(compilation.Value.Bundle);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        var runtime = ContentRuntime.Create(compilation.Value.Bundle);
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        return runtime.Value;
    }

    [Fact]
    public async Task WorldOneCards_CompileAndOnlySpendEnergy()
    {
        var runtime = await Compile();
        var root = ResourceProviderFactory.FindProjectRoot()!;
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "data/configs/ascendant/Resources/cards/world_one.json")));
        var compiler = new CardContentCompiler();
        Assert.Equal(33, json.RootElement.EnumerateObject().Count());
        foreach (var entry in json.RootElement.EnumerateObject())
        {
            var card = compiler.Compile(entry.Name, runtime);
            Assert.True(card.IsSuccess, card.IsFailure ? $"{entry.Name}: {card.Error}" : null);
            foreach (var cost in entry.Value.GetProperty("components").EnumerateArray()
                .Where(component => component.GetProperty("type").GetString() == "cost")
                .SelectMany(component => component.GetProperty("costs").GetProperty("costs").EnumerateArray()))
                Assert.Equal("energy", cost.GetProperty("resourceId").GetString());
        }
    }

    [Fact]
    public async Task ExileCards_AreCanonicalComponentsAndStarterIsInTheOpeningDeck()
    {
        var runtime = await Compile();
        var run = runtime.GetDefinition<RunDefinition>("runs", "ascendant_world_one_run").Value;
        Assert.Equal(10, run.StartingCards.Count);
        Assert.Single(run.StartingCards, id => id == "w1_last_guard");
        var compiler = new CardContentCompiler();
        foreach (var id in new[] { "w1_last_guard", "w1_second_wind", "w1_solar_burst" })
        {
            var card = compiler.Compile(id, runtime);
            Assert.True(card.IsSuccess, card.IsFailure ? card.Error : null);
            Assert.Equal("card.played.to-exhaust", card.Value.All<CardDispositionComponentDefinition>().Single().CardZoneResolutionFlowId);
            Assert.NotEmpty(card.Value.All<CardEffectComponentDefinition>());
        }
    }
}
