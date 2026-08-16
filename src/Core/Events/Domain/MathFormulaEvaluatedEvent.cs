using System.Collections.Immutable;

namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando uma fórmula matemática é avaliada.
/// </summary>
public record MathFormulaEvaluatedEvent : GameEvent
{
    private ImmutableDictionary<string, float> _parameters =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);

    public string FormulaName { get; init; } = string.Empty;
    public float InputValue { get; init; }
    public float OutputValue { get; init; }
    public IReadOnlyDictionary<string, float> Parameters
    {
        get => _parameters;
        init => _parameters = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    }

    public MathFormulaEvaluatedEvent()
    {
        EventType = nameof(MathFormulaEvaluatedEvent);
        Category = EventCategory.META;
        Severity = EventSeverity.DEBUG;
        Subject = "MathEngine";
        Verb = "evaluated";
    }
}
