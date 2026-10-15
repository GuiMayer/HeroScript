using System.Collections.Immutable;
using Core.Calculations;
using Core.Common;
using Core.Content;
using Core.Run.Content;

namespace Core.Effects;

/// <summary>Revisioned, source-independent defaults. No calculation, RNG or mutation occurs here.</summary>
public sealed record EffectResolutionPolicyDefinition : Core.Determinism.ICanonicalHashMemoizable
{
    public string PolicyId { get; init; } = string.Empty;
    public ImmutableArray<EffectResolutionRuleDefinition> Rules { get; init; } = [];
    public ImmutableArray<ActorAttributeProjectionDefinition> Attributes { get; init; } = [];
}

public sealed record ActorAttributeProjectionDefinition
{
    public string AttributeId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string PipelineId { get; init; } = string.Empty;
    public string DisplayUnit { get; init; } = string.Empty;
    public float DisplayScale { get; init; } = 1;
    public float DisplayOffset { get; init; }
    public float FlatValue { get; init; }
    public ImmutableArray<string> Tags { get; init; } = [];
}

public sealed record EffectResolutionRuleDefinition
{
    public string RuleId { get; init; } = string.Empty;
    public ImmutableArray<string> RequiredTags { get; init; } = [];
    public ImmutableArray<string> ExcludedTags { get; init; } = [];
    public ImmutableArray<string> ExcludedChannels { get; init; } = [];
    public ImmutableArray<string> Channels { get; init; } = [];
    public string AmountPipelineId { get; init; } = string.Empty;
    public string? NonCriticalPipelineId { get; init; }
    public string? CriticalStageId { get; init; }
    public string? ArmorBypassTag { get; init; }
    public EffectRandomInputDefinition? CriticalInput { get; init; }
    public string? TierCheckpointId { get; init; }
    public string? BonusCaptureId { get; init; }
    public ImmutableArray<string> ProjectionTags { get; init; } = [];
}

/// <summary>Explicit overrides can be supplied by any effect owner, not just cards.</summary>
public sealed record EffectRuleOverrideDefinition
{
    public string? EffectId { get; init; }
    public ImmutableArray<string> RequiredTags { get; init; } = [];
    public bool? Critical { get; init; }
    public bool? Armor { get; init; }
}

public sealed record EffectResolutionPolicyReceipt(string PolicyId, string RuleId, string Fingerprint,
    bool CriticalEnabled, bool ArmorEnabled);

public sealed record ResolvedEffectPolicy(EffectDefinition Effect, EffectResolutionPolicyReceipt? Receipt);

public static class EffectResolutionPolicyResolver
{
    public static Result ValidateOverride(EffectRuleOverrideDefinition rule) =>
        rule == null || rule.EffectId is { Length: 0 } || rule.RequiredTags.Any(string.IsNullOrWhiteSpace) ||
        rule.Critical == null && rule.Armor == null
            ? Result.Failure("Effect rule override requires a valid selector and at least one explicit rule")
            : Result.Success();

    public static Result<ResolvedEffectPolicy> Resolve(EffectDefinition effect, EffectResolutionPolicyDefinition? policy,
        ContentRuntime? runtime, EffectiveCardDefinition? card = null, IReadOnlySet<string>? suppliedTags = null)
    {
        if (policy == null) return Result<ResolvedEffectPolicy>.Success(new(effect, null));
        var tags = CalculationResolver.NormalizeTags(effect, suppliedTags ?? ImmutableHashSet<string>.Empty);
        var amount = effect.Parameters.FirstOrDefault(p => p.Parameter == EffectNumericParameter.Amount);
        var channel = amount?.Channel ?? effect.CalculationChannel;
        var matching = policy.Rules.Where(rule => rule.RequiredTags.All(tags.Contains) &&
            !rule.ExcludedTags.Any(tags.Contains) && !rule.ExcludedChannels.Contains(channel) &&
            (rule.Channels.IsEmpty || rule.Channels.Contains(channel))).ToArray();
        if (matching.Length > 1) return Result<ResolvedEffectPolicy>.Failure("Ambiguous global effect resolution rules");
        if (matching.Length == 0) return Result<ResolvedEffectPolicy>.Success(new(effect, null));
        var rule = matching[0];
        var overrides = effect.RuleOverrides.Concat(card?.All<CardEffectRuleOverrideComponentDefinition>()
            .Select(component => component.Rule) ?? []).Where(value =>
                (value.EffectId == null || value.EffectId == effect.EffectId) && value.RequiredTags.All(tags.Contains)).ToArray();
        // Denial wins: a local allow cannot bypass an explicit prohibition.
        var critical = rule.CriticalInput != null && !overrides.Any(value => value.Critical == false);
        var armor = !overrides.Any(value => value.Armor == false);
        if (!armor && string.IsNullOrWhiteSpace(rule.ArmorBypassTag))
            return Result<ResolvedEffectPolicy>.Failure("Armor override requires a configured bypass tag");
        if (amount != null && amount.Distribution == null && !amount.StageIds.IsEmpty && rule.CriticalStageId != null &&
            !amount.StageIds.Contains(rule.CriticalStageId)) critical = false;
        var pipelineId = critical ? rule.AmountPipelineId : rule.NonCriticalPipelineId ?? rule.AmountPipelineId;
        var pipeline = runtime?.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", pipelineId);
        if (pipeline == null || pipeline.IsFailure)
            return Result<ResolvedEffectPolicy>.Failure("Global effect pipeline is unavailable");
        var parameters = effect.Parameters.Select(parameter => parameter.Parameter != EffectNumericParameter.Amount ? parameter :
            parameter with { PipelineId = pipelineId, StageIds = parameter.StageIds.IsEmpty ? [] :
                pipeline.Value.Stages.Where(stage => parameter.StageIds.Contains(stage.StageId) ||
                    critical && parameter.Distribution != null && stage.StageId == rule.CriticalStageId)
                    .Select(stage => stage.StageId).ToImmutableArray() }).ToImmutableArray();
        if (amount == null)
            parameters = parameters.Add(new() { Parameter = EffectNumericParameter.Amount, FlatValue = effect.FlatValue,
                FormulaValue = effect.FormulaValue, Channel = pipeline.Value.Channel, PipelineId = pipelineId, UnitId = pipeline.Value.UnitId });
        var inputs = effect.RandomInputs.Where(input => input.InputId != rule.CriticalInput?.InputId).ToImmutableArray();
        if (critical) inputs = inputs.Add(rule.CriticalInput!);
        if (inputs.Length > 16) return Result<ResolvedEffectPolicy>.Failure("Global rules exceed the random input limit");
        var resolved = effect with { CalculationPipelineId = pipelineId, Parameters = parameters,
            RandomInputs = inputs, Tags = armor ? effect.Tags : effect.Tags.Append(rule.ArmorBypassTag!).Distinct().ToImmutableArray() };
        return Result<ResolvedEffectPolicy>.Success(new(resolved,
            new(policy.PolicyId, rule.RuleId, Core.Determinism.CanonicalJson.ComputeHash(policy), critical, armor)));
    }

    public static Result Validate(EffectResolutionPolicyDefinition policy, ContentRuntime runtime)
    {
        if (policy.Attributes.Length > 64 || policy.Attributes.Select(value => value.AttributeId).Distinct(StringComparer.Ordinal).Count() != policy.Attributes.Length)
            return Result.Failure("Attribute projections must be unique and bounded");
        foreach (var attribute in policy.Attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.AttributeId) || string.IsNullOrWhiteSpace(attribute.DisplayName) ||
                !float.IsFinite(attribute.DisplayScale) || attribute.DisplayScale == 0 || !float.IsFinite(attribute.DisplayOffset) ||
                !float.IsFinite(attribute.FlatValue) || attribute.Tags.Any(string.IsNullOrWhiteSpace))
                return Result.Failure("Invalid attribute projection");
            var profile = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", attribute.PipelineId);
            if (profile.IsFailure || profile.Value.ResourceInfluenceBindings.Any(binding => binding.Settlement != null))
                return Result.Failure("Attribute projection requires a read-only calculation profile");
        }
        if (string.IsNullOrWhiteSpace(policy.PolicyId) || policy.Rules.IsEmpty || policy.Rules.Length > 32 ||
            policy.Rules.Select(rule => rule.RuleId).Distinct(StringComparer.Ordinal).Count() != policy.Rules.Length)
            return Result.Failure("Effect resolution policy requires unique, bounded rules");
        foreach (var rule in policy.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.RuleId) || rule.RequiredTags.Any(string.IsNullOrWhiteSpace) ||
                rule.ExcludedTags.Any(string.IsNullOrWhiteSpace) || rule.ExcludedChannels.Any(string.IsNullOrWhiteSpace) ||
                rule.Channels.Any(string.IsNullOrWhiteSpace)) return Result.Failure("Invalid effect rule selector");
            foreach (var id in new[] { rule.AmountPipelineId, rule.NonCriticalPipelineId }.Where(id => id != null))
                if (runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", id!).IsFailure)
                    return Result.Failure($"Global effect rule references missing pipeline {id}");
            if (rule.CriticalInput is { } input)
            {
                if (input.Scope != EffectRandomScope.Impact || input.Probability == null || string.IsNullOrWhiteSpace(input.InputId) ||
                    rule.NonCriticalPipelineId == null || string.IsNullOrWhiteSpace(rule.CriticalStageId))
                    return Result.Failure("Critical rule requires an Impact input, stage and non-critical pipeline");
                var valid = EffectRandomProbabilityPolicies.Validate(input.Probability);
                if (valid.IsFailure) return valid;
                var criticalPipeline = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", rule.AmountPipelineId).Value;
                if (!criticalPipeline.Stages.Any(stage => stage.StageId == rule.CriticalStageId))
                    return Result.Failure("Critical rule references an unknown impact stage");
                var probabilityPipeline = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", input.Probability.PipelineId);
                if (probabilityPipeline.IsFailure) return Result.Failure(probabilityPipeline.Error);
                valid = EffectRandomProbabilityPolicies.ValidatePipeline(input.Probability, probabilityPipeline.Value, input.Scope);
                if (valid.IsFailure) return valid;
                if (rule.TierCheckpointId != null && !probabilityPipeline.Value.Stages.Any(stage => stage.StageId == rule.TierCheckpointId) ||
                    rule.BonusCaptureId != null && !input.Probability.Captures.ContainsKey(rule.BonusCaptureId) || rule.ProjectionTags.Any(string.IsNullOrWhiteSpace))
                    return Result.Failure("Invalid critical projection mapping");
                foreach (var id in input.Probability.Captures.Values.Select(value => value.PipelineId).Append(input.Probability.PipelineId))
                    if (id == null || runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", id).IsFailure)
                        return Result.Failure($"Critical template references missing pipeline {id}");
                foreach (var capture in input.Probability.Captures.Values)
                {
                    var profile = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", capture.PipelineId!).Value;
                    valid = EffectRandomProbabilityPolicies.ValidateCapturePipeline(capture, profile, input.Scope, input.Probability.SharedContextCapture);
                    if (valid.IsFailure) return valid;
                }
            }
        }
        return Result.Success();
    }
}
