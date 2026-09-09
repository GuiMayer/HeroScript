using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Math;
using Core.Run;

namespace Core.Combat.Gambits;

public sealed record DecisionPolicyRequest(
    RunState Run,
    CombatState Combat,
    string ActorId,
    IReadOnlyList<string> DecisionIds);

public interface IDecisionPolicy
{
    string PolicyId { get; }
    Result<DecisionPolicyResult> Decide(DecisionPolicyRequest request);
}

public interface IDecisionPolicyRegistry
{
    Result<DecisionPolicyResult> Decide(
        ControllerBinding binding,
        DecisionPolicyRequest request);
}

/// <summary>Explicit registry: unknown or absent policy IDs are content errors.</summary>
public sealed class DecisionPolicyRegistry : IDecisionPolicyRegistry
{
    private readonly IReadOnlyDictionary<string, IDecisionPolicy> _policies;

    public DecisionPolicyRegistry(IEnumerable<IDecisionPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        _policies = policies.ToDictionary(policy => policy.PolicyId, StringComparer.Ordinal);
    }

    public Result<DecisionPolicyResult> Decide(
        ControllerBinding binding,
        DecisionPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);
        if (binding.Kind != ControllerKind.AI)
            return Result<DecisionPolicyResult>.Failure(
                $"Decision policies can only control AI actors: {request.ActorId}");
        if (string.IsNullOrWhiteSpace(binding.PolicyId))
            return Result<DecisionPolicyResult>.Failure(
                $"AI actor '{request.ActorId}' requires a decision policy id");
        return !_policies.TryGetValue(binding.PolicyId, out var policy)
            ? Result<DecisionPolicyResult>.Failure($"Unknown decision policy: {binding.PolicyId}")
            : policy.Decide(request);
    }
}

/// <summary>Loads only definitions from the run's pinned content revision.</summary>
public sealed class GambitDecisionPolicy : IDecisionPolicy
{
    private readonly IContentRuntimeResolver _runtimes;
    private readonly ILegalActionResolver _legalActions;
    private readonly GambitDecisionReducer _reducer;

    public GambitDecisionPolicy(
        IContentRuntimeResolver runtimes,
        ILegalActionResolver legalActions,
        IRuntimeFormulaEvaluator formulas)
    {
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _legalActions = legalActions ?? throw new ArgumentNullException(nameof(legalActions));
        _reducer = new GambitDecisionReducer(formulas);
    }

    public string PolicyId => "gambit";

    public Result<DecisionPolicyResult> Decide(DecisionPolicyRequest request)
    {
        if (request.DecisionIds.Count == 0)
            return Result<DecisionPolicyResult>.Failure(
                $"Gambit policy has no configured decisions for actor '{request.ActorId}'");
        var runtime = _runtimes.Resolve(
            request.Run.Determinism.ContentRevision,
            request.Run.ConfigName);
        if (runtime.IsFailure)
            return Result<DecisionPolicyResult>.Failure(runtime.Error);
        var definitions = new List<GambitDefinition>();
        foreach (var id in request.DecisionIds.Distinct(StringComparer.Ordinal)
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            var definition = runtime.Value.GetDefinition<GambitDefinition>("gambits", id);
            if (definition.IsFailure)
                return Result<DecisionPolicyResult>.Failure(definition.Error);
            definitions.Add(definition.Value);
        }
        var decisionCombat = ProjectActivation(request.Combat, request.ActorId);
        var effectiveRequest = request with { Combat = decisionCombat };
        var legal = _legalActions.Resolve(
            request.Run,
            decisionCombat,
            request.ActorId,
            CombatCommandOrigin.AutomaticController);
        return legal.IsFailure
            ? Result<DecisionPolicyResult>.Failure(legal.Error)
            : _reducer.Decide(effectiveRequest, definitions, legal.Value);
    }

    private static CombatState ProjectActivation(CombatState combat, string actorId)
    {
        if (combat.PriorityWindow != null || combat.ActivationState == null ||
            string.Equals(combat.ActivationState.ActiveActorId, actorId, StringComparison.Ordinal))
            return combat;
        return combat with
        {
            ActivationState = combat.ActivationState with
            {
                ActiveActorId = actorId,
                WaitingForInput = false,
                ActionsTaken = 0
            }
        };
    }
}

/// <summary>Pure deterministic rule reducer; no cache, filesystem, CRUD or fallback.</summary>
public sealed class GambitDecisionReducer
{
    private readonly IRuntimeFormulaEvaluator _formulas;

    public GambitDecisionReducer(IRuntimeFormulaEvaluator formulas) =>
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));

    public Result<DecisionPolicyResult> Decide(
        DecisionPolicyRequest request,
        IReadOnlyList<GambitDefinition> definitions,
        LegalActionSet legalActions)
    {
        var actor = request.Combat.GetActor(request.ActorId);
        if (actor == null)
            return Result<DecisionPolicyResult>.Failure($"Actor not found: {request.ActorId}");
        foreach (var rule in definitions
                     .OrderByDescending(rule => rule.Priority)
                     .ThenBy(rule => rule.GambitId, StringComparer.Ordinal))
        {
            var candidates = legalActions.Candidates
                .Where(candidate => MatchesAction(rule.Action, candidate))
                .ToArray();
            var selected = SelectTarget(rule.Action.TargetSelector, actor, request.Combat, candidates);
            if (selected.IsFailure)
                return Result<DecisionPolicyResult>.Failure(
                    $"Gambit {rule.GambitId}: {selected.Error}");
            foreach (var candidate in selected.Value)
            {
                var predicates = MatchPredicates(rule, request, actor, candidate);
                if (predicates.IsFailure)
                    return Result<DecisionPolicyResult>.Failure(predicates.Error);
                if (!predicates.Value)
                    continue;
                var fingerprint = CanonicalJson.ComputeHash(new
                {
                    policyId = "gambit",
                    rule.GambitId,
                    rule.Priority,
                    legalActions.StateFingerprint,
                    candidate.CandidateId,
                    determinism = request.Run.Determinism
                });
                return Result<DecisionPolicyResult>.Success(new DecisionPolicyResult
                {
                    Candidate = candidate,
                    PolicyId = "gambit",
                    RuleId = rule.GambitId,
                    Priority = rule.Priority,
                    Intent = rule.Intent,
                    Determinism = request.Run.Determinism,
                    StateFingerprint = legalActions.StateFingerprint,
                    DecisionFingerprint = fingerprint
                });
            }
        }
        var priorityPass = legalActions.Candidates.FirstOrDefault(candidate =>
            candidate.Command.ActionType == ActionType.PASS_PRIORITY);
        if (priorityPass == null)
        {
            return Result<DecisionPolicyResult>.Failure(
                $"No configured decision matched a legal action for actor '{request.ActorId}'");
        }
        var passFingerprint = CanonicalJson.ComputeHash(new
        {
            policyId = "gambit",
            ruleId = "system.priority.pass",
            legalActions.StateFingerprint,
            priorityPass.CandidateId,
            determinism = request.Run.Determinism
        });
        return Result<DecisionPolicyResult>.Success(new DecisionPolicyResult
        {
            Candidate = priorityPass,
            PolicyId = "gambit",
            RuleId = "system.priority.pass",
            Intent = new GambitIntentDefinition
            {
                DisplayName = "Pass priority",
                TelegraphType = "PassPriority",
                Tags = ["priority", "pass"]
            },
            Determinism = request.Run.Determinism,
            StateFingerprint = legalActions.StateFingerprint,
            DecisionFingerprint = passFingerprint
        });
    }

    private Result<bool> MatchPredicates(
        GambitDefinition rule,
        DecisionPolicyRequest request,
        CombatActorState actor,
        LegalActionCandidate candidate)
    {
        var target = PrimaryTarget(request.Combat, candidate);
        var variables = BuildVariables(request.Combat, actor, target);
        foreach (var predicate in rule.Predicates)
        {
            var evaluated = _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
                ? revisioned.EvaluateAtRevision(
                    predicate.Expression,
                    request.Run.Determinism.ContentRevision,
                    variables)
                : _formulas.Evaluate(predicate.Expression, variables);
            if (evaluated.IsFailure)
                return Result<bool>.Failure(
                    $"Gambit {rule.GambitId} predicate '{predicate.Expression}': {evaluated.Error}");
            if (predicate.Minimum.HasValue && evaluated.Value < predicate.Minimum.Value)
                return Result<bool>.Success(false);
            if (predicate.Maximum.HasValue && evaluated.Value > predicate.Maximum.Value)
                return Result<bool>.Success(false);
            if (!predicate.Minimum.HasValue && !predicate.Maximum.HasValue && evaluated.Value <= 0)
                return Result<bool>.Success(false);
        }
        return Result<bool>.Success(true);
    }

    private static bool MatchesAction(GambitActionMatcher matcher, LegalActionCandidate candidate) =>
        (!matcher.ActionType.HasValue || candidate.Command.ActionType == matcher.ActionType.Value) &&
        (string.IsNullOrWhiteSpace(matcher.ActionId) ||
         string.Equals(candidate.ActionId, matcher.ActionId, StringComparison.Ordinal)) &&
        (string.IsNullOrWhiteSpace(matcher.CardDefinitionId) ||
         string.Equals(candidate.CardDefinitionId, matcher.CardDefinitionId, StringComparison.Ordinal)) &&
        (string.IsNullOrWhiteSpace(matcher.CostOptionId) ||
         string.Equals(candidate.Command.CostOptionId, matcher.CostOptionId, StringComparison.Ordinal));

    private static Result<IReadOnlyList<LegalActionCandidate>> SelectTarget(
        DecisionTargetSelectorDefinition selector,
        CombatActorState actor,
        CombatState combat,
        IReadOnlyList<LegalActionCandidate> candidates)
    {
        IEnumerable<LegalActionCandidate> eligible = candidates;
        if (selector.Strategy == DecisionTargetSelection.Self)
            eligible = eligible.Where(candidate => Targets(candidate).Contains(actor.InstanceId, StringComparer.Ordinal));
        if (selector.Relationship.HasValue)
        {
            eligible = eligible.Where(candidate => PrimaryTarget(combat, candidate) is { } target &&
                combat.Relationship(actor, target) == selector.Relationship.Value);
        }
        var ordered = eligible
            .OrderBy(candidate => PrimaryTarget(combat, candidate)?.InstanceId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
            return Result<IReadOnlyList<LegalActionCandidate>>.Success([]);
        if (selector.Strategy is DecisionTargetSelection.LowestResource or DecisionTargetSelection.HighestResource)
        {
            if (string.IsNullOrWhiteSpace(selector.ResourceId))
                return Result<IReadOnlyList<LegalActionCandidate>>.Failure(
                    $"Target selector {selector.Strategy} requires resourceId");
            var withResource = ordered
                .Select(candidate => (Candidate: candidate, Target: PrimaryTarget(combat, candidate)))
                .Where(item => item.Target?.GetResource(selector.ResourceId) != null);
            ordered = (selector.Strategy == DecisionTargetSelection.LowestResource
                    ? withResource.OrderBy(item => item.Target!.GetResource(selector.ResourceId)!.Current)
                    : withResource.OrderByDescending(item => item.Target!.GetResource(selector.ResourceId)!.Current))
                .ThenBy(item => item.Target!.InstanceId, StringComparer.Ordinal)
                .ThenBy(item => item.Candidate.CandidateId, StringComparer.Ordinal)
                .Select(item => item.Candidate)
                .Take(1)
                .ToArray();
        }
        else if (selector.Strategy is DecisionTargetSelection.FirstOrdinal or DecisionTargetSelection.Self)
        {
            ordered = ordered.Take(1).ToArray();
        }
        return Result<IReadOnlyList<LegalActionCandidate>>.Success(ordered);
    }

    private static CombatActorState? PrimaryTarget(CombatState combat, LegalActionCandidate candidate) =>
        Targets(candidate).Select(combat.GetActor).FirstOrDefault(target => target != null);

    private static IReadOnlyList<string> Targets(LegalActionCandidate candidate) =>
        candidate.Command.TargetIds.Count > 0
            ? candidate.Command.TargetIds
            : string.IsNullOrWhiteSpace(candidate.Command.TargetId) ? [] : [candidate.Command.TargetId];

    private static Dictionary<string, float> BuildVariables(
        CombatState combat,
        CombatActorState actor,
        CombatActorState? target)
    {
        var variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["turn"] = combat.CurrentTurn
        };
        AddActorVariables(variables, "actor", actor);
        if (target != null)
            AddActorVariables(variables, "target", target);
        return variables;
    }

    private static void AddActorVariables(
        IDictionary<string, float> variables,
        string prefix,
        CombatActorState actor)
    {
        foreach (var (id, resource) in actor.ResourceState.Resources)
        {
            variables[$"{prefix}_resource_{id}_current"] = resource.Current;
            variables[$"{prefix}_resource_{id}_maximum"] = resource.Maximum;
            variables[$"{prefix}_resource_{id}_percent"] = resource.Maximum == 0
                ? 0
                : resource.Current / resource.Maximum;
        }
        foreach (var (id, value) in actor.Component<StatEntityComponentState>()?.Values ??
                 new Dictionary<string, float>())
            variables[$"{prefix}_stat_{id}"] = value;
    }
}
