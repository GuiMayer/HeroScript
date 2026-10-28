using Core.GameplayEvents;

namespace Core.Run;

public sealed partial class RunManager
{
    private GameplayRunObservation? _observation;
    private IRunActivityEffectExecutor? _observedActivityEffects;
    private Core.CardZones.ICardZoneFlowExecutor? _observedCardZoneFlows;
    private IRunActivityEffectExecutor? ActivityEffects => _observedActivityEffects ?? _activityEffects;
    private Core.CardZones.ICardZoneFlowExecutor? CardZoneFlows => _observedCardZoneFlows ?? _cardZoneFlows;

    // Only a fresh, detached candidate engine may carry this command-local cursor.
    // It never enters a snapshot, service singleton, query cache, journal or event bus.
    internal void AttachObservation(GameplayRunObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (_repository != null || _eventBus != null || !_runs.IsEmpty || _observation != null)
            throw new InvalidOperationException("Observation requires a fresh detached planning engine");
        _observation = observation;
        _observedActivityEffects = _activityEffects == null ? null : observation.Wrap(_activityEffects);
        _observedCardZoneFlows = _cardZoneFlows == null ? null : new ObservedRunCardZoneFlows(_cardZoneFlows, observation);
    }
}
