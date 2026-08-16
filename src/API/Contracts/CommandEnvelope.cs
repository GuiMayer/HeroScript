using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace API.Contracts;

/// <summary>
/// Common transport envelope for deterministic aggregate commands.
/// </summary>
public sealed record CommandEnvelope : IValidatableObject
{
    public Guid CommandId { get; init; }
    public int? ExpectedSequence { get; init; }
    public ulong? ExpectedStep { get; init; }

    [Required]
    public string Type { get; init; } = string.Empty;

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
