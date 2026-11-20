using API.Services;
using Core.Abstractions.Persistence;
using Core.Combat.Gambits;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Content;
using Core.Determinism;
using Core.Math;
using Core.Resources;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace API.Controllers;

[ApiController]
[Route("api/v1/diagnostics/gambits")]
public sealed class GambitDiagnosticsController(IRunQueryService runs, IRunCommitReader commits,
    IContentRuntimeResolver runtimes, ILegalActionResolver legal, IRuntimeFormulaEvaluator formulas,
    IToolAccessPolicy access) : ControllerBase
{
    [HttpGet("{runId:guid}/{combatId:guid}/{actorId}")]
    public async Task<IActionResult> Inspect(Guid runId, Guid combatId, string actorId, int? sequence, CancellationToken cancellationToken)
    {
        if (sequence is <= 0) return BadRequest(new { error = "Sequence must be positive" });
        var current = runs.GetRun(runId);
        if (current.IsFailure) return NotFound(new { error = current.Error });
        if (!access.Allows(current.Value, ToolCapabilities.TimelineInspectState)) return StatusCode(403, new { error = "Timeline inspection denied" });
        var run = sequence.HasValue ? await commits.LoadStateAsync(runId, sequence.Value, cancellationToken) : current.Value;
        if (run is null) return NotFound(new { error = "Run sequence not found" });
        var combat = run.GetEncounter(combatId)?.Combat;
        var actor = combat?.GetActor(actorId);
        if (combat is null || actor is null) return NotFound(new { error = "Combat or actor not found" });
        if (actor.ControllerBinding.Kind != ControllerKind.AI || actor.ControllerBinding.PolicyId != "gambit")
            return BadRequest(new { error = "Actor is not controlled by gambits" });
        var ids = actor.ControllerBinding.DecisionIds is { } explicitIds ? (IReadOnlyList<string>)explicitIds : run.ResolvedMode?.CombatRules.Flow.Ai.DecisionIds ?? [];
        var trace = new List<GambitRuleTrace>();
        var result = new GambitDecisionPolicy(runtimes, legal, formulas).Decide(new(run, combat, actorId, ids), trace.Add);
        return Ok(new { runId, combatId, actorId, run.Sequence, run.Determinism.ContentRevision, run.Determinism.Seed,
            stateHash = CanonicalJson.ComputeHash(run), decisionIds = ids, trace, success = result.IsSuccess,
            decision = result.IsSuccess ? result.Value : null, error = result.IsFailure ? result.Error : null });
    }

    [HttpGet("coverage")]
    public IActionResult Coverage(string settingId, string revision, string? runDefinitionId = null, int budget = 1000,
        [FromQuery] string[]? runDefinitionIds = null,
        CancellationToken cancellationToken = default)
    {
        if (budget is < 1 or > 5000) return BadRequest(new { error = "Budget must be 1–5000" });
        var loaded = runtimes.Resolve(revision, settingId);
        if (loaded.IsFailure) return NotFound(new { error = loaded.Error });
        var runtime = loaded.Value;
        var reducer = new GambitDecisionReducer(formulas);
        var determinism = DeterministicContext.Create(42, revision);
        var run = new RunState { RunId = Guid.Empty, ConfigName = settingId, Determinism = determinism };
        var failures = new List<object>();
        int checkedCases = 0, bindings = 0, failedCases = 0, skippedBindings = 0;
        if (runDefinitionIds?.Length > 50) return BadRequest(new { error = "At most 50 run definitions" });
        var scope = (runDefinitionIds ?? []).ToHashSet(StringComparer.Ordinal);
        if (runDefinitionId != null) scope.Add(runDefinitionId);
        var knownRuns = runtime.GetDefinitions("runs");
        if (scope.Any(id => !knownRuns.ContainsKey(id))) return BadRequest(new { error = "Coverage scope contains an unknown run definition" });
        bool incomplete = false;
        foreach (var definition in runtime.GetDefinitions("runs").OrderBy(p => p.Key))
        {
            if (scope.Count > 0 && !scope.Contains(definition.Key)) continue;
            if (!definition.Value.TryGetProperty("mapNodes", out var nodes)) continue;
            foreach (var node in nodes.EnumerateArray())
            {
                var activity = node.GetProperty("activity");
                if (!string.Equals(activity.GetProperty("type").GetString(), "Encounter", StringComparison.OrdinalIgnoreCase) ||
                    !activity.TryGetProperty("parameters", out var parameters) || !parameters.TryGetProperty("participants", out var participants)) continue;
                foreach (var participant in participants.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!participant.TryGetProperty("controllerBinding", out var binding) ||
                        !binding.TryGetProperty("kind", out var kind) || kind.GetString() != "AI") continue;
                    bindings++;
                    var entity = participant.GetProperty("definitionId").GetString()!;
                    var ids = binding.TryGetProperty("decisionIds", out var decisions) ? decisions.EnumerateArray().Select(i => i.GetString()!).ToArray() : [];
                    if (ids.Length == 0) { skippedBindings++; incomplete = true; continue; } // Requires mode-specific fallback resolution, not an explicit binding fixture.
                    var rules = new List<GambitDefinition>();
                    foreach (var id in ids)
                    {
                        var rule = runtime.GetDefinition<GambitDefinition>("gambits", id);
                        if (rule.IsSuccess) rules.Add(rule.Value);
                        else failures.Add(new { run = definition.Key, entity, rule = id, error = rule.Error });
                    }
                    var abilities = new HashSet<string>(StringComparer.Ordinal);
                    if (runtime.GetDefinitions("entities").TryGetValue(entity, out var entityDefinition) && entityDefinition.TryGetProperty("components", out var components))
                        foreach (var component in components.EnumerateArray())
                            if (component.TryGetProperty("type", out var type) && type.GetString() == "abilities" && component.TryGetProperty("abilityIds", out var owned))
                                foreach (var ability in owned.EnumerateArray()) abilities.Add(ability.GetString()!);
                    foreach (var rule in rules.Where(r => r.Action.ActionType == ActionType.POWER && r.Action.ActionId != null && !abilities.Contains(r.Action.ActionId)))
                        failures.Add(new { run = definition.Key, entity, rule = rule.GambitId, error = "Power not owned", power = rule.Action.ActionId });
                    foreach (var health in new[] { 90f, 50f, 20f })
                    foreach (var round in Enumerable.Range(1, 12))
                    foreach (var blocked in new[] { false, true })
                    {
                        if (checkedCases >= budget) { incomplete = true; continue; }
                        cancellationToken.ThrowIfCancellationRequested();
                        checkedCases++;
                        var combat = new CombatState { CurrentTurn = round, Determinism = determinism,
                            Actors = new Dictionary<string, CombatActorState> { ["enemy"] = Actor("enemy", "opposition", health), ["player"] = Actor("player", "player", 90) },
                            Relationships = new CombatRelationshipPolicy { DifferentSides = SideRelationship.Enemy } };
                        var candidates = blocked ? new[] { Candidate("END_TURN", ActionType.END_TURN, "enemy", run, combat) }
                            : abilities.Order().SelectMany(id => new[] { Candidate(id, ActionType.POWER, "enemy", run, combat), Candidate(id, ActionType.POWER, "player", run, combat) }).ToArray();
                        var trace = new List<GambitRuleTrace>();
                        var result = reducer.Decide(new(run, combat, "enemy", ids), rules, new LegalActionSet
                        { ActorId = "enemy", Candidates = candidates, Determinism = determinism, StateFingerprint = CanonicalJson.ComputeHash(combat) }, trace.Add);
                        if (result.IsFailure)
                        {
                            failedCases++;
                            if (failures.Count < 100) failures.Add(new { run = definition.Key, entity, round, health, blocked, error = result.Error, trace });
                        }
                    }
                }
            }
        }
        return Ok(new { settingId, contentRevision = revision, seed = 42, bindings, skippedBindings, checkedCases, failedCases, incomplete,
            method = "synthetic owned-power and end-turn fixtures; not full legality or exhaustive simulation", failures });
    }

    private static LegalActionCandidate Candidate(string id, ActionType type, string target, RunState run, CombatState combat) => new()
    {
        CandidateId = id + ":" + target, ActionId = id, Source = type == ActionType.POWER ? LegalActionSource.Ability : LegalActionSource.System,
        Command = new CombatActionCommand { RunId = run.RunId, ActorId = "enemy", ActionType = type, PowerId = type == ActionType.POWER ? id : null, TargetId = target, TargetIds = [target] },
        SuccessorRun = run, SuccessorCombat = combat
    };

    private static CombatActorState Actor(string id, string side, float health) => new()
    {
        InstanceId = id, DefinitionId = id, Name = id, SideId = side,
        ResourceState = new ResourceSet { OwnerId = id, Resources = new Dictionary<string, ResourcePool>
        { ["health"] = new() { ResourceId = "health", Current = health, Maximum = 100, Minimum = 0, Definition = new ResourceDefinition { ResourceId = "health" } } } }
    };
}
