namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando uma fórmula matemática é avaliada.
/// </summary>
public record MathFormulaEvaluatedEvent : GameEvent
{
    public string FormulaName { get; init; } = string.Empty;
    public float InputValue { get; init; }
    public float OutputValue { get; init; }
    public Dictionary<string, float> Parameters { get; init; } = new();

    public MathFormulaEvaluatedEvent()
    {
        EventType = nameof(MathFormulaEvaluatedEvent);
        Category = EventCategory.META;
        Severity = EventSeverity.DEBUG;
        Subject = "MathEngine";
        Verb = "evaluated";
    }
}
