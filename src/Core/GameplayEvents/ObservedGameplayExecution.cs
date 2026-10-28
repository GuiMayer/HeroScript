namespace Core.GameplayEvents;

/// <summary>A canonical result and its private shadow candidate; neither is externally committed here.</summary>
public sealed record ObservedGameplayExecution<T>(T Execution, GameplayEffectObservation Observation);
