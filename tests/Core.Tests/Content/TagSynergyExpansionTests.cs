using System.Text.Json;
using Core.Config;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyExpansionTests
{
    internal static string Root => ResourceProviderFactory.FindProjectRoot()!;
    [Fact]
    public void OriginalCardComponents_RemainIdenticalToPreExpansionBaseline()
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,
            "tests/Core.Tests/Content/Fixtures/world_one_card_components.json")));
        using var current = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,
            "data/configs/ascendant/Resources/cards/world_one.json")));
        Assert.Equal(33, baseline.RootElement.EnumerateObject().Count());
        foreach (var entry in baseline.RootElement.EnumerateObject())
            Assert.True(JsonElement.DeepEquals(entry.Value,
                current.RootElement.GetProperty(entry.Name).GetProperty("components")), entry.Name);
    }

    [Fact]
    public async Task ResidualAndDefenseProducers_AreDiscoverableByTags()
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var compiler = new CardContentCompiler();
        foreach (var id in new[] { "w1_scorch", "w1_poison", "w1_solar_burst", "w1_inferno", "w1_reserve" })
            Assert.Contains("residual", compiler.Compile(id, runtime).Value.Tags);
        foreach (var id in new[] { "w1_guard", "w1_bulwark", "w1_mend", "w1_shield_bash" })
            Assert.Contains("defense", compiler.Compile(id, runtime).Value.Tags);
        Assert.DoesNotContain("residual", compiler.Compile("w1_release", runtime).Value.Tags);
    }
}
