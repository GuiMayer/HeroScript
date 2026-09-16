using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Core.Common;

namespace Core.CardZones;

public sealed record CompiledCardZoneSystem
{
    public string SystemId { get; init; } = string.Empty;
    public ImmutableDictionary<string, CardZoneDefinition> Zones { get; init; } =
        ImmutableDictionary<string, CardZoneDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableDictionary<string, CardZoneFlowDefinition> Flows { get; init; } =
        ImmutableDictionary<string, CardZoneFlowDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableDictionary<string, ImmutableArray<CardZoneFlowDefinition>> FlowsByTrigger { get; init; } =
        ImmutableDictionary<string, ImmutableArray<CardZoneFlowDefinition>>.Empty.WithComparers(StringComparer.Ordinal);

    public Result<CardZoneDefinition> GetZone(string zoneId) =>
        Zones.TryGetValue(zoneId, out var zone)
            ? Result<CardZoneDefinition>.Success(zone)
            : Result<CardZoneDefinition>.Failure($"Card zone definition not found: {zoneId}");

    public Result<CardZoneFlowDefinition> GetFlow(string flowId) =>
        Flows.TryGetValue(flowId, out var flow)
            ? Result<CardZoneFlowDefinition>.Success(flow)
            : Result<CardZoneFlowDefinition>.Failure($"Card-zone flow not found: {flowId}");
}

public static partial class CardZoneSystemCompiler
{
    public static Result<CompiledCardZoneSystem> Compile(CardZoneSystemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var validation = Validate(definition);
        if (validation.IsFailure)
            return Result<CompiledCardZoneSystem>.Failure(validation.Error);
        var zones = definition.Zones.ToImmutableDictionary(zone => zone.ZoneId, StringComparer.Ordinal);
        var flows = definition.Flows.ToImmutableDictionary(flow => flow.FlowId, StringComparer.Ordinal);
        var byTrigger = definition.Flows
            .SelectMany(flow => flow.Triggers.Select(trigger => (trigger, flow)))
            .GroupBy(item => item.trigger, StringComparer.Ordinal)
            .ToImmutableDictionary(
                group => group.Key,
                group => group.Select(item => item.flow)
                    .OrderBy(flow => flow.Priority)
                    .ThenBy(flow => flow.FlowId, StringComparer.Ordinal)
                    .ToImmutableArray(),
                StringComparer.Ordinal);
        return Result<CompiledCardZoneSystem>.Success(new CompiledCardZoneSystem
        {
            SystemId = definition.CardZoneSystemId,
            Zones = zones,
            Flows = flows,
            FlowsByTrigger = byTrigger
        });
    }

    public static Result Validate(CardZoneSystemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();
        if (!ValidId(definition.CardZoneSystemId)) errors.Add("cardZoneSystemId is invalid");
        if (definition.FlowOwnerBinding is not (CardZoneOwnerBinding.RunOwner or
            CardZoneOwnerBinding.ActiveActor or CardZoneOwnerBinding.Global))
            errors.Add("flowOwnerBinding must be RunOwner, ActiveActor, or Global");
        if (definition.Zones.Count == 0) errors.Add("At least one card zone is required");
        if (definition.Zones.Select(zone => zone.ZoneId).Distinct(StringComparer.Ordinal).Count() != definition.Zones.Count)
            errors.Add("Card zone ids must be unique");
        if (definition.Flows.Select(flow => flow.FlowId).Distinct(StringComparer.Ordinal).Count() != definition.Flows.Count)
            errors.Add("Card-zone flow ids must be unique");
        var zones = definition.Zones.GroupBy(zone => zone.ZoneId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var flows = definition.Flows.GroupBy(flow => flow.FlowId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(definition.GameplayGrantFlowId) &&
            (!flows.TryGetValue(definition.GameplayGrantFlowId, out var grantFlow) ||
             !grantFlow.AllowedInvocations.Contains(CardZoneFlowInvocation.GameplayCommand)))
            errors.Add("gameplayGrantFlowId must reference a gameplay-command flow");

        foreach (var zone in definition.Zones)
        {
            var path = $"zones/{zone.ZoneId}";
            if (!ValidId(zone.ZoneId)) errors.Add($"{path}: zoneId is invalid");
            if (zone.OwnerScope == CardZoneOwnerScope.Unspecified) errors.Add($"{path}: ownerScope is required");
            if (zone.Ordering == CardZoneOrdering.Unspecified) errors.Add($"{path}: ordering is required");
            if (zone.Capacity < 0) errors.Add($"{path}: capacity cannot be negative");
            if (zone.Visibility.Contents == CardZoneVisibility.Unspecified ||
                zone.Visibility.Order == CardZoneOrderVisibility.Unspecified)
                errors.Add($"{path}: visibility is incomplete");
            if (zone.Ordering == CardZoneOrdering.Unordered && zone.Visibility.Order == CardZoneOrderVisibility.Visible)
                errors.Add($"{path}: unordered zones cannot expose an ordered position");
        }

        foreach (var flow in definition.Flows)
        {
            var path = $"flows/{flow.FlowId}";
            if (!ValidId(flow.FlowId)) errors.Add($"{path}: flowId is invalid");
            if (flow.AllowedInvocations.Count == 0 || flow.AllowedInvocations.Contains(CardZoneFlowInvocation.Unspecified))
                errors.Add($"{path}: allowedInvocations is required");
            if (flow.PlayerInvokable && !flow.AllowedInvocations.Contains(CardZoneFlowInvocation.GameplayCommand))
                errors.Add($"{path}: playerInvokable requires GameplayCommand invocation");
            if (flow.Steps.Count == 0) errors.Add($"{path}: at least one step is required");
            if (flow.Steps.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != flow.Steps.Count)
                errors.Add($"{path}: step ids must be unique");
            if (flow.Triggers.Count > 0 && !flow.AllowedInvocations.Contains(CardZoneFlowInvocation.Boundary))
                errors.Add($"{path}: triggered flows must allow boundary invocation");
            foreach (var step in flow.Steps)
                ValidateStep(step, flow, path, zones, flows, errors);
        }
        ValidateAcyclic(definition.Flows, errors);
        return errors.Count == 0 ? Result.Success() : Result.Failure(string.Join("; ", errors));
    }

    private static void ValidateStep(
        CardZoneFlowStepDefinition step,
        CardZoneFlowDefinition parentFlow,
        string flowPath,
        IReadOnlyDictionary<string, CardZoneDefinition> zones,
        IReadOnlyDictionary<string, CardZoneFlowDefinition> flows,
        ICollection<string> errors)
    {
        var path = $"{flowPath}/steps/{step.StepId}";
        if (!ValidId(step.StepId)) errors.Add($"{path}: stepId is invalid");
        if (step.Operation == CardZoneOperation.Unspecified) errors.Add($"{path}: operation is required");
        if (step.Operation is CardZoneOperation.Move or CardZoneOperation.Destroy or CardZoneOperation.Shuffle or CardZoneOperation.Reorder)
            ValidateZone(step.SourceZoneId, "sourceZoneId");
        if (step.Operation is CardZoneOperation.Move or CardZoneOperation.Create)
            ValidateZone(step.TargetZoneId, "targetZoneId");
        if (step.Operation == CardZoneOperation.Move && string.Equals(step.SourceZoneId, step.TargetZoneId, StringComparison.Ordinal))
            errors.Add($"{path}: source and target zones must differ");
        if (step.Operation == CardZoneOperation.Create && string.IsNullOrWhiteSpace(step.CardDefinitionId))
            errors.Add($"{path}: create requires cardDefinitionId");
        if (step.CardDefinitionId == "$input" &&
            (step.Operation != CardZoneOperation.Create ||
             !parentFlow.AllowedInvocations.Any(invocation => invocation is
                 CardZoneFlowInvocation.Tool or CardZoneFlowInvocation.Effect or
                 CardZoneFlowInvocation.GameplayCommand)))
            errors.Add($"{path}: $input requires a tool, effect, or gameplay-command create flow");
        if (step.Operation == CardZoneOperation.Create && step.CardDefinitionId?.StartsWith('$') == true &&
            step.CardDefinitionId != "$input")
            errors.Add($"{path}: unknown dynamic card definition token");
        if (step.Operation == CardZoneOperation.Create && step.Lifetime.Strategy == CardInstanceLifetimeStrategy.Unspecified)
            errors.Add($"{path}: create requires lifetime strategy");
        if (step.Lifetime.Strategy == CardInstanceLifetimeStrategy.UntilBoundary && string.IsNullOrWhiteSpace(step.Lifetime.Boundary))
            errors.Add($"{path}: until-boundary lifetime requires boundary");
        if (step.Operation is CardZoneOperation.Move or CardZoneOperation.Destroy or CardZoneOperation.Reorder &&
            step.Selection.Strategy == CardZoneSelectionStrategy.Unspecified)
            errors.Add($"{path}: operation requires a selection strategy");
        if (step.Selection.Count < 0) errors.Add($"{path}: selection count cannot be negative");
        if (step.Selection.TargetZoneCount < 0)
            errors.Add($"{path}: selection targetZoneCount cannot be negative");
        var countSources = new object?[]
        {
            step.Selection.Count,
            string.IsNullOrWhiteSpace(step.Selection.CountFormula) ? null : step.Selection.CountFormula,
            step.Selection.TargetZoneCount,
            string.IsNullOrWhiteSpace(step.Selection.TargetZoneCountFormula)
                ? null
                : step.Selection.TargetZoneCountFormula
        }.Count(value => value != null);
        if (countSources > 1)
            errors.Add($"{path}: selection can define only one count source");
        if ((step.Selection.TargetZoneCount is not null ||
             !string.IsNullOrWhiteSpace(step.Selection.TargetZoneCountFormula)) &&
            step.Operation != CardZoneOperation.Move)
            errors.Add($"{path}: target-zone count requires a move operation");
        if (step.Operation is CardZoneOperation.Move or CardZoneOperation.Create &&
            step.Insertion.Strategy == CardZoneInsertionStrategy.Unspecified)
            errors.Add($"{path}: insertion strategy is required");
        if (step.Insertion.Strategy == CardZoneInsertionStrategy.AtIndex && step.Insertion.Index is null)
            errors.Add($"{path}: indexed insertion requires index");
        if (step.OnInsufficient == CardZoneInsufficientPolicy.Unspecified)
            errors.Add($"{path}: onInsufficient is required");
        if (step.OnOverflow == CardZoneOverflowPolicy.Unspecified)
            errors.Add($"{path}: onOverflow is required");
        if (step.OnInsufficient == CardZoneInsufficientPolicy.ExecuteFallbackAndRetry)
            ValidateFlow(step.FallbackFlowId, "fallbackFlowId");
        else if (!string.IsNullOrWhiteSpace(step.FallbackFlowId))
            errors.Add($"{path}: fallbackFlowId requires ExecuteFallbackAndRetry");
        if (step.MoveAvailableBeforeFallback &&
            (step.Operation != CardZoneOperation.Move ||
             step.OnInsufficient != CardZoneInsufficientPolicy.ExecuteFallbackAndRetry ||
             !step.RetryAfterFallback ||
             step.Selection.Strategy is CardZoneSelectionStrategy.All or CardZoneSelectionStrategy.Explicit))
            errors.Add($"{path}: moveAvailableBeforeFallback requires a retryable move selection");
        if (step.OnOverflow == CardZoneOverflowPolicy.RedirectOverflow)
            ValidateFlow(step.OverflowFlowId, "overflowFlowId");
        else if (!string.IsNullOrWhiteSpace(step.OverflowFlowId))
            errors.Add($"{path}: overflowFlowId requires RedirectOverflow");
        if (step.Operation == CardZoneOperation.ExecuteFlow)
            ValidateFlow(step.NestedFlowId, "nestedFlowId");
        foreach (var referencedFlowId in new[] { step.FallbackFlowId, step.OverflowFlowId, step.NestedFlowId }
                     .Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            if (flows.TryGetValue(referencedFlowId!, out var referencedFlow) &&
                parentFlow.AllowedInvocations.Any(invocation => !referencedFlow.AllowedInvocations.Contains(invocation)))
                errors.Add($"{path}: referenced flow {referencedFlowId} must allow the parent's invocations");
        }
        if (step.Operation is CardZoneOperation.Move or CardZoneOperation.Create &&
            zones.TryGetValue(step.TargetZoneId ?? string.Empty, out var targetZone) &&
            targetZone.Ordering == CardZoneOrdering.Unordered &&
            step.Insertion.Strategy is CardZoneInsertionStrategy.Top or CardZoneInsertionStrategy.AtIndex or
                CardZoneInsertionStrategy.RandomPosition or CardZoneInsertionStrategy.ShuffleAfterInsert or
                CardZoneInsertionStrategy.CreationOrder)
            errors.Add($"{path}: unordered target zone cannot use positional insertion");
        if (zones.TryGetValue(step.SourceZoneId ?? string.Empty, out var sourceZone))
        {
            if (sourceZone.Ordering == CardZoneOrdering.Unordered &&
                step.Selection.Strategy is CardZoneSelectionStrategy.Top or CardZoneSelectionStrategy.Bottom)
                errors.Add($"{path}: unordered source zone cannot use top/bottom selection");
            if (sourceZone.Ordering == CardZoneOrdering.Unordered &&
                step.Operation is CardZoneOperation.Shuffle or CardZoneOperation.Reorder)
                errors.Add($"{path}: unordered source zone cannot be shuffled or reordered");
        }
        if (step.Operation == CardZoneOperation.Reorder &&
            step.Selection.Strategy is not (CardZoneSelectionStrategy.All or CardZoneSelectionStrategy.Explicit))
            errors.Add($"{path}: reorder requires all or explicit selection");
        if (step.Selection.Strategy == CardZoneSelectionStrategy.All &&
            countSources > 0)
            errors.Add($"{path}: all selection cannot define a count");
        if (step.Selection.Strategy == CardZoneSelectionStrategy.ByTags &&
            step.Selection.RequiredTags.Count == 0 && step.Selection.ExcludedTags.Count == 0)
            errors.Add($"{path}: tag selection requires requiredTags or excludedTags");
        if (step.Selection.SelectAllMatches &&
            (step.Selection.Strategy is not (CardZoneSelectionStrategy.ByTags or
                CardZoneSelectionStrategy.ByCondition or CardZoneSelectionStrategy.ByDefinition) ||
             countSources > 0))
            errors.Add($"{path}: selectAllMatches requires an unbounded filtered selection");
        if (step.Selection.Strategy == CardZoneSelectionStrategy.ByCondition && string.IsNullOrWhiteSpace(step.Selection.Condition))
            errors.Add($"{path}: conditional selection requires condition");

        void ValidateZone(string? id, string member)
        {
            if (string.IsNullOrWhiteSpace(id) || !zones.ContainsKey(id))
                errors.Add($"{path}: {member} references an unknown zone");
        }
        void ValidateFlow(string? id, string member)
        {
            if (string.IsNullOrWhiteSpace(id) || !flows.ContainsKey(id))
                errors.Add($"{path}: {member} references an unknown flow");
        }
    }

    private static void ValidateAcyclic(IReadOnlyList<CardZoneFlowDefinition> flows, ICollection<string> errors)
    {
        var graph = flows.ToDictionary(
            flow => flow.FlowId,
            flow => flow.Steps.SelectMany(step => new[] { step.FallbackFlowId, step.OverflowFlowId, step.NestedFlowId })
                .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in graph.Keys)
            Visit(id);

        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id))
            {
                errors.Add($"Card-zone flow graph contains a cycle at {id}");
                return;
            }
            foreach (var next in graph.GetValueOrDefault(id) ?? []) Visit(next);
            visiting.Remove(id);
            visited.Add(id);
        }
    }

    private static bool ValidId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && IdPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
