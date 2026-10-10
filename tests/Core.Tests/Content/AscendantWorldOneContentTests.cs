using Core.Config;
using Core.Content;
using Core.Run;
using Core.Run.Content;
using Mods;
using Xunit;

namespace Core.Tests.Content;

public sealed class AscendantWorldOneContentTests
{
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
        Assert.Equal(30, json.RootElement.EnumerateObject().Count());
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
}
