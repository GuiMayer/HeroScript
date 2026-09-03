using Core.Common;

namespace Core.Resources;

/// <summary>Single validation authority for authored resource definitions.</summary>
public static class ResourceDefinitionValidator
{
    public static Result Validate(ResourceDefinition definition, string? expectedResourceId = null)
    {
        if (definition == null)
            return Result.Failure("Resource definition cannot be null");
        if (string.IsNullOrWhiteSpace(definition.ResourceId))
            return Result.Failure("Resource ID cannot be empty");
        if (!string.IsNullOrWhiteSpace(expectedResourceId) &&
            !string.Equals(definition.ResourceId, expectedResourceId, StringComparison.Ordinal))
        {
            return Result.Failure(
                $"Resource definition id '{definition.ResourceId}' does not match content key '{expectedResourceId}'");
        }
        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            return Result.Failure("Display name cannot be empty");
        if (!IsFinite(definition.DefaultMin) ||
            !IsFinite(definition.DefaultMax) ||
            !IsFinite(definition.DefaultCurrent))
            return Result.Failure("Resource defaults must be finite");
        if (!IsFinite(definition.CostMultiplier) || definition.CostMultiplier < 0)
            return Result.Failure("Resource cost multiplier must be finite and non-negative");
        if (definition.DefaultMax < definition.DefaultMin)
            return Result.Failure("Default max cannot be less than default min");
        if (definition.DefaultCurrent < definition.DefaultMin && !definition.CanBeNegative)
            return Result.Failure("Default current cannot be less than default min");
        if (definition.DefaultCurrent > definition.DefaultMax && !definition.CanExceedMax)
            return Result.Failure("Default current cannot exceed default max");
        if (definition.Tags.Any(string.IsNullOrWhiteSpace))
            return Result.Failure("Resource tags cannot be empty");
        if (definition.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != definition.Tags.Count)
            return Result.Failure("Resource tags must be unique");

        var duplicatePolicyIds = definition.ThresholdPolicies
            .Where(policy => !string.IsNullOrWhiteSpace(policy.PolicyId))
            .GroupBy(policy => policy.PolicyId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePolicyIds != null)
            return Result.Failure($"Duplicate resource threshold policy id: {duplicatePolicyIds.Key}");
        foreach (var policy in definition.ThresholdPolicies)
        {
            if (string.IsNullOrWhiteSpace(policy.PolicyId))
                return Result.Failure("Resource threshold policy id cannot be empty");
            if (policy.Comparison == ResourceThresholdComparison.Unspecified)
                return Result.Failure($"Resource threshold comparison is required: {policy.PolicyId}");
            if (policy.ThresholdSource == ResourceThresholdSource.Unspecified)
                return Result.Failure($"Resource threshold source is required: {policy.PolicyId}");
            if (policy.ThresholdSource == ResourceThresholdSource.Constant &&
                (!policy.ThresholdValue.HasValue || !IsFinite(policy.ThresholdValue.Value)))
                return Result.Failure($"Resource threshold constant must be finite: {policy.PolicyId}");
            if (policy.ThresholdSource != ResourceThresholdSource.Constant && policy.ThresholdValue.HasValue)
                return Result.Failure(
                    $"Resource threshold value is only valid for Constant source: {policy.PolicyId}");
            if (!IsFinite(policy.Tolerance) || policy.Tolerance < 0)
                return Result.Failure(
                    $"Resource threshold tolerance must be finite and non-negative: {policy.PolicyId}");
            if (policy.Consequence == ResourceThresholdConsequence.Unspecified)
                return Result.Failure($"Resource threshold consequence is required: {policy.PolicyId}");
        }

        if (definition.Regeneration is { } regeneration)
        {
            if (!IsFinite(regeneration.AmountPerTurn))
                return Result.Failure("Resource regeneration amount must be finite");
            if (!Enum.IsDefined(regeneration.Timing))
                return Result.Failure("Resource regeneration timing is invalid");
        }

        return Result.Success();
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
