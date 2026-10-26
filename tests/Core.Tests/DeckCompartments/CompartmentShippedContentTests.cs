using Core.Combat.Models;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckCompartments;
using Mods;
using Xunit;

namespace Core.Tests.DeckCompartments;

public sealed class CompartmentShippedContentTests
{
    internal static async Task<ContentRuntime> Compile()
    {
        var compiler = new SettingCompiler([new DirectoryPackageProvider("shipped", Path.Combine(ResourceProviderFactory.FindProjectRoot()!, "data", "configs"))]);
        var compilation = await compiler.CompileAsync("compartment-lab");
        Assert.True(compilation.IsSuccess, compilation.IsFailure ? compilation.Error : null);
        var validation = new ContentGraphValidator().Validate(compilation.Value.Bundle);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        return ContentRuntime.Create(compilation.Value.Bundle).Value;
    }

    [Fact]
    public async Task TwoCharactersHaveDifferentProfilesAndPersistentBindingSurvivesRebind()
    {
        var runtime = await Compile();
        var hero = PersistentPlayerTransitions.Create("hero", "compartment_lab_hero", runtime);
        var sentinel = PersistentPlayerTransitions.Create("sentinel", "compartment_lab_sentinel", runtime);
        Assert.True(hero.IsSuccess);
        Assert.True(sentinel.IsSuccess);
        Assert.NotEqual(hero.Value.Component<DeckCompartmentProfileComponentState>()!.ProfileId,
            sentinel.Value.Component<DeckCompartmentProfileComponentState>()!.ProfileId);
        var rebound = PersistentPlayerTransitions.Rebind(hero.Value, runtime);
        Assert.True(rebound.IsSuccess, rebound.IsFailure ? rebound.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(hero.Value), CanonicalJson.ComputeHash(rebound.Value));
    }
}
