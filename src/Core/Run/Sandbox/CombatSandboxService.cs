using Core.Abstractions.Persistence;
using Core.Combat;
using Core.Common;
using Core.Determinism;
using Core.Run.Projections;
using System.Text.Json;

namespace Core.Run.Sandbox;

public sealed record SandboxCombatLaunch
{
    public RunState Run { get; init; } = new();
    public Core.Combat.Models.CombatState Combat { get; init; } = new();
    public CombatResolutionRecord? Resolution { get; init; }
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
    private readonly IGameplayCommandGateway _commands;
    private readonly IRunCommitReader _repository;

    public CombatSandboxService(
        ICombatScenarioCompiler compiler,
        IRunManager runs,
        IGameplayCommandGateway commands,
        IRunCommitReader repository)
    {
        _compiler = compiler;
        _runs = runs;
        _commands = commands;
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
            return Result<SandboxCombatLaunch>.Failure(started.Error);
        var live = _runs.GetRun(started.Value.RunId);
        if (live.IsFailure) return Result<SandboxCombatLaunch>.Failure(live.Error);
        var existingEncounter = live.Value.GetActiveEncounter();
        if (existingEncounter != null)
        {
            return Result<SandboxCombatLaunch>.Success(new SandboxCombatLaunch
            {
                Run = live.Value,
                Combat = existingEncounter.Combat,
                Resolution = FindLatestResolution(live.Value.RunId),
                ScenarioHash = compiled.Value.ScenarioHash,
                Duplicate = true
            });
        }
        if (live.Value.Sequence > 1)
            return Result<SandboxCombatLaunch>.Failure("Existing scenario run has no active encounter; use a new attempt key");

        var payload = JsonSerializer.SerializeToElement(new StartSandboxEncounterCommand(
            compiled.Value.Participants,
            compiled.Value.InitialStatusEffects));
        var encounterResult = _commands.Execute(
            started.Value.RunId,
            new GameplayCommandEnvelope(
                new RunCommandIdentity(
                    DeterministicId.Create(
                        started.Value.Determinism.Seed,
                        checked((ulong)started.Value.Sequence),
                        $"sandbox-encounter:{compiled.Value.ScenarioHash}"),
                    GameplayCommandTypes.StartSandboxEncounter,
                    started.Value.Sequence,
                    started.Value.Determinism.Step,
                    CanonicalJson.ComputeHash(payload)),
                payload));
        if (encounterResult.IsFailure)
            return Result<SandboxCombatLaunch>.Failure(
                $"Scenario run was created but encounter launch failed: {encounterResult.Error}");
        if (encounterResult.Value.CombatState == null)
            return Result<SandboxCombatLaunch>.Failure(
                "Scenario encounter command completed without a combat state");

        return Result<SandboxCombatLaunch>.Success(new SandboxCombatLaunch
        {
            Run = encounterResult.Value.Receipt.State,
            Combat = encounterResult.Value.CombatState,
            Resolution = encounterResult.Value.Receipt.CombatResolution,
            ScenarioHash = compiled.Value.ScenarioHash,
            Duplicate = encounterResult.Value.Receipt.Duplicate
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

    private CombatResolutionRecord? FindLatestResolution(Guid runId)
    {
        try
        {
            return _repository.LoadCommitsAsync(runId).GetAwaiter().GetResult()
                .Select(CombatResolutionProjection.FromCommit)
                .LastOrDefault(resolution => resolution != null);
        }
        catch
        {
            return null;
        }
    }
}
