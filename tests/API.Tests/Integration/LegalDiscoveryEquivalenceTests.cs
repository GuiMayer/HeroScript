using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Combat.LegalActions;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class LegalDiscoveryEquivalenceTests
{
    [Theory]
    [InlineData(250)]
    [InlineData(251)]
    public async Task CoalescingEquivalentResolvedInputsPreservesEveryCandidateAndSuccessor(int seed)
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client); var player = "discovery-" + Guid.NewGuid().ToString("N");
        var id = await game.StartRunAsync("ascendant", "ascendant_world_one_run", player, (ulong)seed, "ascendant_world_one");
        var runs = factory.Services.GetRequiredService<IRunQueryService>();
        var start = runs.GetAvailableCommands(id).Value.Single(value => value.Type == "START_ENCOUNTER");
        using var response = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
        { commandId = Guid.NewGuid(), type = start.Type, payload = start.ValidPayload, expectedSequence = start.ExpectedSequence, expectedStep = start.ExpectedStep });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var run = runs.GetRun(id).Value; var combat = run.GetActiveEncounter()!.Combat;
        var before = CanonicalJson.ComputeHash(run);
        var original = ActivatorUtilities.CreateInstance<LegalActionResolver>(factory.Services, false);
        var optimized = ActivatorUtilities.CreateInstance<LegalActionResolver>(factory.Services, true);
        var a = original.Resolve(run, combat, player, CombatCommandOrigin.PlayerInput).Value;
        var b = optimized.Resolve(run, combat, player, CombatCommandOrigin.PlayerInput).Value;
        Assert.Equal(CanonicalJson.ComputeHash(a), CanonicalJson.ComputeHash(b));
        Assert.Equal(a.Candidates.Select(value => value.CandidateId), b.Candidates.Select(value => value.CandidateId));
        for (var i = 0; i < a.Candidates.Count; i++)
        {
            Assert.Equal(CanonicalJson.ComputeHash(a.Candidates[i].SuccessorRun), CanonicalJson.ComputeHash(b.Candidates[i].SuccessorRun));
            Assert.Equal(CanonicalJson.ComputeHash(a.Candidates[i].SuccessorCombat), CanonicalJson.ComputeHash(b.Candidates[i].SuccessorCombat));
        }
        Assert.True(optimized.CoalescedExecutions > 0);
        Assert.Equal(before, CanonicalJson.ComputeHash(runs.GetRun(id).Value));
    }
}
