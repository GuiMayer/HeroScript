using Core.Abstractions.Persistence;
using Core.Combat;
using Core.Common;

namespace Core.Run.Sandbox;

public sealed record SandboxCombatLaunch
{
    public RunState Run { get; init; } = new();
    public Core.Combat.Models.CombatState Combat { get; init; } = new();
    public string ScenarioHash { get; init; } = string.Empty;
    public bool Duplicate { get; init; }
}

public interface ICombatSandboxService
{
    Result<CompiledCombatScenario> Validate(CombatScenarioDefinition scenario, string configName = "default");
    Result<SandboxCombatLaunch> Launch(CombatScenarioDefinition scenario, string configName = "default");
    Result<CombatScenarioDefinition> GetScenario(Guid runId);
}

/// <summary>
/// Authoritative application service for sandbox launch. It is deliberately a
/// thin composition of compiler, run aggregate and combat coordinator: neither
/// REST clients nor Godot construct game state themselves.
/// </summary>
public sealed class CombatSandboxService : ICombatSandboxService
{
    private readonly ICombatScenarioCompiler _compiler;
    private readonly IRunManager _runs;
    private readonly ICombatRunCoordinator _combats;
    private readonly IRunStateRepository _repository;

    public CombatSandboxService(
        ICombatScenarioCompiler compiler,
        IRunManager runs,
        ICombatRunCoordinator combats,
        IRunStateRepository repository)
    {
        _compiler = compiler;
        _runs = runs;
        _combats = combats;
        _repository = repository;
    }

    public Result<CompiledCombatScenario> Validate(CombatScenarioDefinition scenario, string configName = "default") =>
        _compiler.Compile(scenario, configName);

    public Result<SandboxCombatLaunch> Launch(CombatScenarioDefinition scenario, string configName = "default")
    {
        var compiled = _compiler.Compile(scenario, configName);
        if (compiled.IsFailure)
            return Result<SandboxCombatLaunch>.Failure(compiled.Error);

        var started = _runs.StartRun(compiled.Value.RunStart);
        if (started.IsFailure)
        {
            var duplicate = FindExisting(compiled.Value.ScenarioHash, compiled.Value.Scenario.AttemptKey);
            if (duplicate == null)
                return Result<SandboxCombatLaunch>.Failure(started.Error);
            var encounter = duplicate.GetActiveEncounter();
            if (encounter == null)
                return Result<SandboxCombatLaunch>.Failure(
                    "Existing scenario run is incomplete and cannot be relaunched automatically");
            return Result<SandboxCombatLaunch>.Success(new SandboxCombatLaunch
            {
                Run = duplicate,
                Combat = encounter.Combat,
                ScenarioHash = compiled.Value.ScenarioHash,
                Duplicate = true
            });
        }

        var encounterResult = _combats.StartEncounter(
            started.Value.RunId,
            compiled.Value.Hero,
            compiled.Value.Enemies);
        if (encounterResult.IsFailure)
            return Result<SandboxCombatLaunch>.Failure(
                $"Scenario run was created but encounter launch failed: {encounterResult.Error}");

        return Result<SandboxCombatLaunch>.Success(new SandboxCombatLaunch
        {
            Run = encounterResult.Value.RunState,
            Combat = encounterResult.Value.CombatState,
            ScenarioHash = compiled.Value.ScenarioHash
        });
    }

    public Result<CombatScenarioDefinition> GetScenario(Guid runId)
    {
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return Result<CombatScenarioDefinition>.Failure(run.Error);
        return run.Value.Scenario == null
            ? Result<CombatScenarioDefinition>.Failure($"Run is not a sandbox scenario: {runId}")
            : Result<CombatScenarioDefinition>.Success(run.Value.Scenario);
    }

    private RunState? FindExisting(string scenarioHash, string attemptKey)
    {
        try
        {
            foreach (var runId in _repository.ListRunIdsAsync().GetAwaiter().GetResult().OrderBy(id => id))
            {
                var state = _repository.LoadLatestAsync(runId).GetAwaiter().GetResult();
                if (state != null &&
                    string.Equals(state.ScenarioHash, scenarioHash, StringComparison.Ordinal) &&
                    string.Equals(state.AttemptKey, attemptKey, StringComparison.Ordinal))
                {
                    return state;
                }
            }
        }
        catch
        {
            // The original start error remains the useful failure if durable
            // lookup is unavailable.
        }
        return null;
    }
}
