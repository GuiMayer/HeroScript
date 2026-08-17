using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace API.Contracts;

/// <summary>
/// Common transport envelope for deterministic aggregate commands.
/// </summary>
public sealed record CommandEnvelope : IValidatableObject
{
    /// <summary>Client-generated idempotency key. Retrying this command with the same value is safe.</summary>
    public Guid CommandId { get; init; }

    /// <summary>Persisted run sequence the client observed before issuing the command.</summary>
    public int? ExpectedSequence { get; init; }

    /// <summary>Deterministic aggregate step the client observed before issuing the command.</summary>
    public ulong? ExpectedStep { get; init; }

    /// <summary>Canonical command type, such as <c>DRAW</c> or <c>EXECUTE_ACTION</c>.</summary>
    [Required]
    public string Type { get; init; } = string.Empty;

    /// <summary>Type-specific JSON payload. It is hashed as part of command identity.</summary>
    public JsonElement Payload { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CommandId == Guid.Empty)
            yield return new ValidationResult("CommandId must not be empty", [nameof(CommandId)]);

        if (string.IsNullOrWhiteSpace(Type))
            yield return new ValidationResult("Type is required", [nameof(Type)]);

        if (ExpectedSequence is < 0)
            yield return new ValidationResult("ExpectedSequence cannot be negative", [nameof(ExpectedSequence)]);
    }
}
