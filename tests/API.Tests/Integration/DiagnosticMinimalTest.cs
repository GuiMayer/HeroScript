using System;
using System.Threading.Tasks;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Teste de diagnóstico mínimo para identificar causa raiz dos timeouts.
/// </summary>
public class DiagnosticMinimalTest : GameEngineIntegrationTestBase
{
    public DiagnosticMinimalTest(TestWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Diagnostic_StartRun_ReturnsSuccess()
    {
        // Step 1: Calling StartRunAsync
        var runId = await Client.StartRunAsync();
        
        // Step 2: Run started successfully
        Assert.NotEqual(Guid.Empty, runId);
        
        // Step 3: Retrieving run state
        var runState = await Client.GetRunStateAsync(runId);
        
        // Step 4: Run state retrieved successfully
        Assert.True(runState.TryGetProperty("runId", out var stateRunId));
        Assert.Equal(runId.ToString(), stateRunId.GetGuid().ToString());
    }

    [Fact]
    public async Task Diagnostic_StartCombat_ReturnsSuccess()
    {
        // Step 1: Starting combat
        var combatId = await Client.StartCombatAsync("hero", new[] { "enemy_1" });
        
        // Step 2: Combat started successfully
        Assert.NotEqual(Guid.Empty, combatId);
        
        // Step 3: Retrieving combat state
        var combatState = await Client.GetCombatStateAsync(combatId);
        
        // Step 4: Combat state retrieved successfully
        Assert.True(combatState.TryGetProperty("combatId", out var stateCombatId));
        Assert.Equal(combatId.ToString(), stateCombatId.GetGuid().ToString());
    }
}
