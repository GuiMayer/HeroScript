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
        => ContentRuntime.Create(await CompileBundle()).Value;

    internal static async Task<ContentBundle> CompileBundle()
    {
        var compiler = new SettingCompiler([new DirectoryPackageProvider("shipped", Path.Combine(ResourceProviderFactory.FindProjectRoot()!, "data", "configs"))]);
        var compilation = await compiler.CompileAsync("compartment-lab");
        Assert.True(compilation.IsSuccess, compilation.IsFailure ? compilation.Error : null);
        var validation = new ContentGraphValidator().Validate(compilation.Value.Bundle);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        return compilation.Value.Bundle;
    }

    [Fact]
    public async Task IncompatibleCapacityReloadRejectsAtomicallyAndDoesNotPolluteProfileCache()
    {
        var original = await CompileBundle();
        var runtime = ContentRuntime.Create(original).Value;
        var instance = new CardInstanceState { CardInstanceId = CompartmentPlannerTests.First, DefinitionId = "core_strike" };
        var run = new RunState { ConfigName = "compartment-lab", PlayerEntityId = "hero",
            PlayerEntity = PersistentPlayerTransitions.Create("hero", "compartment_lab_hero", runtime).Value,
            ResolvedMode = new() { Definition = runtime.GetDefinition<GameModeDefinition>("modes", "compartment_lab").Value },
            Determinism = DeterministicContext.Create(1, runtime.Manifest.Revision),
            Deck = new() { Topology = new() { Instances = new Dictionary<Guid, CardInstanceState> { [instance.CardInstanceId] = instance } } } };
        var installed = DeckCompartmentPlanner.Plan(run, runtime, "hero", [new(instance.CardInstanceId, "ward")]);
        Assert.True(installed.IsSuccess, installed.IsFailure ? installed.Error : null);
        var before = CanonicalJson.ComputeHash(installed.Value.Candidate);
        var path = "deck-compartment-profiles/lab.json";
        var changed = System.Text.Json.Nodes.JsonNode.Parse(original.Artifacts[path].GetRawText())!;
        changed["compartment_lab_balanced"]!["compartments"]![0]!["capacity"] = 0;
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(changed);
        var candidateBundle = original with { Artifacts = original.Artifacts.SetItem(path, payload),
            Manifest = original.Manifest with { Revision = new string('b', 64), Artifacts = original.Manifest.Artifacts.Select(a => a.Path == path ? a with { Hash = CanonicalJson.ComputeHash(payload) } : a).ToArray() } };
        var candidate = ContentRuntime.Create(candidateBundle).Value;
        var valid = RunContentCompatibilityValidator.ValidateForActivation(installed.Value.Candidate, candidate);
        Assert.True(valid.IsFailure);
        Assert.Contains("capacity", valid.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, CanonicalJson.ComputeHash(installed.Value.Candidate));
        Assert.Equal(3, DeckCompartmentAccess.Profile(installed.Value.Candidate, runtime, "hero").Value.Compartments[0].Capacity);
        Assert.True(RunContentCompatibilityValidator.ValidateForActivation(installed.Value.Candidate with { ActiveEncounterId = instance.CardInstanceId }, runtime).IsFailure);
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
