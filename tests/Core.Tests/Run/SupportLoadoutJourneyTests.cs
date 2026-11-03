using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat;
using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Content;
using Core.Determinism;
using Core.Entity.Definitions;
using Core.Run;
using Core.Run.Content;
using Core.Run.Loadouts;
using Core.Run.Supports;
using Xunit;

namespace Core.Tests.Run;

public sealed partial class ActorResourceJourneyTests
{
    private static SupportLoadoutPolicyDefinition SupportPolicy(SupportInventoryScope scope = SupportInventoryScope.Actor) => new()
    {
        PolicyId = "companions", InventoryScope = scope, EditWindows = [LoadoutEquipmentWindow.OutsideCombat],
        SupportIds = ["unit"], StartingOwnedSupportIds = ["unit"], StartingSupportId = "unit"
    };
    private static ContentRuntime SupportContent(SupportInventoryScope scope = SupportInventoryScope.Actor)
    {
        var baseline = ActorResourceLifecycleTests.Runtime();
        var original = new ContentBundle { Manifest = baseline.Manifest, Artifacts = baseline.Manifest.Artifacts.ToImmutableDictionary(artifact => artifact.Path,
            artifact => JsonSerializer.SerializeToElement(baseline.GetDefinitions(artifact.Kind))) };
        var extra = new Dictionary<string, JsonElement>
        {
            ["entities/support.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, EntityDefinition>
                { ["unit"] = new() { DefinitionId = "unit" } }),
            ["supports/test.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, SupportDefinition>
                { ["unit"] = new() { SupportId = "unit", EntityDefinitionId = "unit", OwnerActivationInterval = 3, CountsForOutcome = false,
                    RoutineIds = ["attack", "guard"], DefaultRoutineId = "attack" } }),
            ["support-routines/test.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, SupportRoutineDefinition>
                { ["attack"] = new() { RoutineId = "attack", GambitIds = ["hit"] }, ["guard"] = new() { RoutineId = "guard", GambitIds = ["defend"] } }),
            ["gambits/test.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, GambitDefinition>
                { ["hit"] = new() { GambitId = "hit" }, ["defend"] = new() { GambitId = "defend" } }),
            ["support-loadout-policies/test.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, SupportLoadoutPolicyDefinition>
                { ["companions"] = SupportPolicy(scope) })
        };
        return ContentRuntime.Create(original with { Artifacts = original.Artifacts.AddRange(extra), Manifest = original.Manifest with
            { Artifacts = original.Manifest.Artifacts.Concat(extra.Select(pair => new ContentArtifactManifest
                { Kind = pair.Key.Split('/')[0], Path = pair.Key, DefinitionCount = pair.Value.EnumerateObject().Count() })).ToArray() } }).Value;
    }
    private static RunState WithSupport(ContentRuntime runtime, string actor = "hero")
    {
        var run = Prepared();
        run = run with { PlayerEntityId = actor, ResolvedMode = run.ResolvedMode! with
            { Definition = run.ResolvedMode.Definition with { SupportLoadoutPolicyId = "companions" } } };
        return SupportLoadoutPlanner.Initialize(run, runtime).Value;
    }

    [Fact]
    public async Task RoutineAutosaveUsesOrdinaryTransactionalReceiptsRestartAndTenReplays()
    {
        var content = SupportContent();
        var parent = WithSupport(content);
        await Durable(parent, run => Envelope(run, RunCommandTypes.EquipSupport, new EquipSupportCommand("hero", "unit", "guard")), state =>
        {
            Assert.Equal("guard", state.SupportLoadout!.Actors["hero"].RoutineId);
            Assert.Equal(37, Current(state));
            var capture = SupportLoadoutPlanner.Capture(state, content);
            Assert.True(capture.IsSuccess, capture.IsFailure ? capture.Error : null);
            Assert.Equal("guard", capture.Value!.RoutineId);
        }, content);
        Assert.Equal("attack", parent.SupportLoadout!.Actors["hero"].RoutineId);
    }

    [Theory]
    [InlineData(SupportInventoryScope.Actor)]
    [InlineData(SupportInventoryScope.Run)]
    public void InventoryScopeAndEquipmentAreSeparateAndCannotWriteAnotherCharactersBuild(SupportInventoryScope scope)
    {
        var content = SupportContent(scope);
        var first = WithSupport(content);
        var second = SupportLoadoutPlanner.Initialize(first with { PlayerEntityId = "another" }, content).Value;
        Assert.Equal(scope == SupportInventoryScope.Run ? 1 : 2, second.SupportLoadout!.Inventories.Count);
        Assert.Equal(2, second.SupportLoadout.Actors.Count);
        Assert.True(SupportLoadoutPlanner.Plan(second, content, new("hero", "unit", "guard")).IsFailure);
        var changed = SupportLoadoutPlanner.Plan(second, content, new("another", "unit", "guard"));
        Assert.True(changed.IsSuccess, changed.IsFailure ? changed.Error : null);
        Assert.Equal("attack", changed.Value.SupportLoadout!.Actors["hero"].RoutineId);
        Assert.Equal("guard", changed.Value.SupportLoadout.Actors["another"].RoutineId);
        Assert.Same(first, SupportLoadoutPlanner.Initialize(first, content).Value);
        Assert.True(SupportLoadoutPlanner.Plan(first, content, new("hero", "unit", "unknown")).IsFailure);
        Assert.True(SupportLoadoutPlanner.Plan(first with { ActiveEncounterId = Guid.NewGuid() }, content, new("hero", "unit", "guard")).IsFailure);
    }

    [Fact]
    public void EquippedParticipantCapturesRoutineCadenceAndOwnerWithoutChangingOriginalRoster()
    {
        var content = SupportContent();
        var run = WithSupport(content);
        CombatParticipantReference[] original = [new("hero", "operator", "custom-side", new() { Kind = ControllerKind.Player }),
            new("enemy", "operator", "opposition", new() { Kind = ControllerKind.AI, PolicyId = "gambit" })];
        var result = SupportLoadoutPlanner.Participants(run, content, original);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var support = result.Value[2];
        Assert.Equal("custom-side", support.SideId);
        Assert.Equal("$support::hero", support.InstanceId);
        Assert.False(support.CountsForOutcome);
        Assert.Equal(3, support.ControllerBinding.Cadence!.Every);
        Assert.Equal("hero", support.ControllerBinding.Cadence.AnchorActorId);
        Assert.Equal(new[] { "hit" }, support.ControllerBinding.DecisionIds!.Value);
        Assert.Equal(2, original.Length);
        Assert.True(SupportLoadoutPlanner.Participants(run, content, result.Value).IsFailure);
        var capture = SupportLoadoutPlanner.Capture(run, content).Value;
        var unequipped = SupportLoadoutPlanner.Plan(run, content, new("hero", null, null)).Value;
        Assert.Null(SupportLoadoutPlanner.Capture(unequipped, content).Value);
        Assert.Equal("attack", capture!.RoutineId);
        Assert.Same(original, SupportLoadoutPlanner.Participants(unequipped, content, original).Value);
        Assert.True(SupportLoadoutPlanner.Capture(run with { ConfigName = "other" }, content).IsFailure);
        Assert.True(SupportLoadoutPlanner.Capture(run with { Determinism = run.Determinism.WithContentRevision("other") }, content).IsFailure);
        Assert.True(RunContentCompatibilityValidator.ValidateForActivation(run, content).IsSuccess);
    }
}
