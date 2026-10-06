using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Determinism;

namespace Core.Calculations;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalculationDistributionMode { Continuous, Quantized }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalculationRemainderAllocation { Earliest, Latest }

public sealed record CalculationDistributionPolicy
{
    public CalculationDistributionMode Mode { get; init; }
    public float? Quantum { get; init; }
    public CalculationRemainderAllocation RemainderAllocation { get; init; }
    /// <summary>Opt-in for distributing quantities that already include a target context. Receipts are never removed.</summary>
    public bool AllowTargetScopedInput { get; init; }
}

public sealed record CalculationDistributionRecipient
{
    public string RecipientId { get; init; } = string.Empty;
    public int Order { get; init; }
}

public sealed record CalculationDistributionRequest
{
    private ImmutableArray<CalculationDistributionRecipient> _recipients = [];
    public string DistributionId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string UnitId { get; init; } = string.Empty;
    public CalculationQuantity InputQuantity { get; init; } = new();
    public CalculationDistributionPolicy Policy { get; init; } = new();
    public IReadOnlyList<CalculationDistributionRecipient> Recipients
    {
        get => _recipients;
        init => _recipients = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CalculationDistributionShare
{
    public string RecipientId { get; init; } = string.Empty;
    public int Order { get; init; }
    public int Index { get; init; }
    public int Units { get; init; }
    public bool ReceivedRemainderUnit { get; init; }
    public CalculationQuantity Quantity { get; init; } = new();
}

/// <summary>Pure allocation trace. It does not apply effects or settle resources.</summary>
public sealed record CalculationDistributionResult
{
    public string DistributionId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string UnitId { get; init; } = string.Empty;
    public CalculationQuantity InputQuantity { get; init; } = new();
    public CalculationDistributionPolicy Policy { get; init; } = new();
    public double AllocationQuantum { get; init; }
    public int TotalUnits { get; init; }
    public int BaseUnitsPerRecipient { get; init; }
    public int RemainderUnits { get; init; }
    public double AllocatedTotal { get; init; }
    public double ConservationRemainder { get; init; }
    public ImmutableArray<CalculationDistributionShare> Shares { get; init; } = [];
    public string Fingerprint { get; init; } = string.Empty;
}

public sealed partial class CalculationEngine
{
    // Numeric limits, not gameplay rules: Float32's exact integer domain and bounded allocation work.
    public const int MaximumDistributionRecipients = 4096;
    public const int MaximumDistributionUnits = 16_777_216;

    public Result<CalculationDistributionResult> Distribute(CalculationDistributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = request.InputQuantity;
        var policy = request.Policy;
        if (string.IsNullOrWhiteSpace(request.DistributionId) || string.IsNullOrWhiteSpace(request.ContentRevision) ||
            string.IsNullOrWhiteSpace(request.UnitId) || input == null || policy == null ||
            !Enum.IsDefined(policy.Mode) || !Enum.IsDefined(policy.RemainderAllocation))
            return Result<CalculationDistributionResult>.Failure("Distribution requires identity, unit, revision and valid policies");
        if (!float.IsFinite(input.Value) || input.ContentRevision != request.ContentRevision || input.UnitId != request.UnitId ||
            string.IsNullOrWhiteSpace(input.CalculationFingerprint))
            return Result<CalculationDistributionResult>.Failure("Distribution quantity has incompatible unit, revision or numeric provenance");
        if (input.IncorporatedStages.IsDefaultOrEmpty || input.IncorporatedStages.Length > MaximumDistributionRecipients ||
            input.IncorporatedStages.Any(receipt => receipt == null || !Enum.IsDefined(receipt.Scope) ||
                string.IsNullOrWhiteSpace(receipt.StageId) || string.IsNullOrWhiteSpace(receipt.PipelineId) ||
                string.IsNullOrWhiteSpace(receipt.PipelineFingerprint) ||
                (receipt.Scope == CalculationStageScope.Shared ? !string.IsNullOrEmpty(receipt.ContextId) : string.IsNullOrWhiteSpace(receipt.ContextId))) ||
            input.IncorporatedStages.Select(receipt => (receipt.StageId, receipt.Scope, receipt.ContextId)).Distinct().Count() != input.IncorporatedStages.Length)
            return Result<CalculationDistributionResult>.Failure("Distribution requires bounded, unique and valid incorporated stage receipts");
        if (!policy.AllowTargetScopedInput && input.IncorporatedStages.Any(receipt => receipt.Scope == CalculationStageScope.Target))
            return Result<CalculationDistributionResult>.Failure("Distribution source budget cannot already include target stages without explicit opt-in");
        if (request.Recipients.Count is < 1 or > MaximumDistributionRecipients ||
            request.Recipients.Any(recipient => recipient == null || string.IsNullOrWhiteSpace(recipient.RecipientId) || recipient.RecipientId.Length > 128) ||
            request.Recipients.Select(recipient => recipient.RecipientId).Distinct(StringComparer.Ordinal).Count() != request.Recipients.Count)
            return Result<CalculationDistributionResult>.Failure("Distribution requires bounded recipients with nonempty unique identities");

        int units;
        double quantum;
        if (policy.Mode == CalculationDistributionMode.Continuous)
        {
            if (policy.Quantum != null)
                return Result<CalculationDistributionResult>.Failure("Continuous distribution does not accept an authored quantum");
            // Allocate on the input's exact binary lattice rather than rounding n independent quotients.
            // Each share is representable in Float32 and their Double sum equals the original Float32 exactly.
            var bits = BitConverter.SingleToUInt32Bits(MathF.Abs(input.Value));
            var exponent = (int)((bits >> 23) & 255);
            units = (int)(bits & 0x7fffff);
            if (exponent != 0) units |= 1 << 23;
            quantum = System.Math.ScaleB(1d, exponent == 0 ? -149 : exponent - 150);
        }
        else
        {
            if (policy.Quantum is not { } step || !float.IsFinite(step) || step <= 0)
                return Result<CalculationDistributionResult>.Failure("Quantized distribution requires an explicit positive finite quantum");
            quantum = step;
            var ratio = System.Math.Abs((double)input.Value) / quantum;
            if (!double.IsFinite(ratio) || ratio > MaximumDistributionUnits || ratio != System.Math.Truncate(ratio))
                return Result<CalculationDistributionResult>.Failure("Quantized budget must be an exact bounded multiple of its quantum; convert the budget explicitly before distribution");
            units = (int)ratio;
            if (units * quantum != System.Math.Abs((double)input.Value))
                return Result<CalculationDistributionResult>.Failure("Quantized budget cannot be conserved exactly");
        }

        var ordered = request.Recipients.OrderBy(recipient => recipient.Order)
            .ThenBy(recipient => recipient.RecipientId, StringComparer.Ordinal).ToImmutableArray();
        var baseUnits = units / ordered.Length;
        var remainder = units % ordered.Length;
        var sign = input.Value < 0 ? -1d : 1d;
        var allocations = ImmutableArray.CreateBuilder<DistributionAllocation>(ordered.Length);
        double total = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var extra = policy.RemainderAllocation == CalculationRemainderAllocation.Earliest
                ? index < remainder : index >= ordered.Length - remainder;
            var shareUnits = baseUnits + (extra ? 1 : 0);
            var exactValue = sign * shareUnits * quantum;
            var value = (float)exactValue;
            if (!float.IsFinite(value) || (double)value != exactValue)
                return Result<CalculationDistributionResult>.Failure("Distribution share is not exactly representable in Float32");
            total += value;
            allocations.Add(new(ordered[index].RecipientId, ordered[index].Order, index, shareUnits, extra, value));
        }
        if (total != (double)input.Value)
            return Result<CalculationDistributionResult>.Failure("Distribution failed exact budget conservation");
        var captured = allocations.ToImmutable();
        var fingerprint = CanonicalJson.ComputeHash(new
        {
            request.DistributionId, request.ContentRevision, request.UnitId, Input = input, Policy = policy,
            AllocationQuantum = quantum, TotalUnits = units, BaseUnitsPerRecipient = baseUnits,
            RemainderUnits = remainder, Allocations = captured, AllocatedTotal = total
        });
        return Result<CalculationDistributionResult>.Success(new()
        {
            DistributionId = request.DistributionId, ContentRevision = request.ContentRevision, UnitId = request.UnitId,
            InputQuantity = input, Policy = policy, AllocationQuantum = quantum, TotalUnits = units,
            BaseUnitsPerRecipient = baseUnits, RemainderUnits = remainder, AllocatedTotal = total,
            ConservationRemainder = (double)input.Value - total, Fingerprint = fingerprint,
            Shares = captured.Select(allocation => new CalculationDistributionShare
            {
                RecipientId = allocation.RecipientId, Order = allocation.Order, Index = allocation.Index,
                Units = allocation.Units, ReceivedRemainderUnit = allocation.ReceivedRemainderUnit,
                Quantity = input with
                {
                    Value = allocation.Value,
                    CalculationFingerprint = CanonicalJson.ComputeHash(new { DistributionFingerprint = fingerprint, Allocation = allocation })
                }
            }).ToImmutableArray()
        });
    }

    private sealed record DistributionAllocation(string RecipientId, int Order, int Index, int Units,
        bool ReceivedRemainderUnit, float Value);
}
