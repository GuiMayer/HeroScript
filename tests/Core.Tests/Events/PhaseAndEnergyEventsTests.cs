using Core.Events.Domain;
using Xunit;

namespace Core.Tests.Events;

public sealed class PhaseAndEnergyEventsTests
{
    [Fact]
    public void PhaseEvents_PreserveContentDefinedIds()
    {
        var started = new PhaseStartedEvent
        {
            CombatId = Guid.NewGuid(),
            PhaseId = "modded_combo_window",
            ActivePlayerId = "hero",
            Turn = 4
        };
        var ended = new PhaseEndedEvent
        {
            CombatId = started.CombatId,
            PhaseId = started.PhaseId,
            NextPhaseId = "cleanup",
            Turn = started.Turn,
            DurationMs = 12
        };

        Assert.Equal("modded_combo_window", started.PhaseId);
        Assert.Equal("cleanup", ended.NextPhaseId);
    }

    [Fact]
    public void EnergyChangedEvent_RecordsSignedDelta()
    {
        var changed = new EnergyChangedEvent
        {
            CombatId = Guid.NewGuid(),
            OldEnergy = 3,
            NewEnergy = 1,
            Delta = -2,
            Reason = "card_played"
        };

        Assert.Equal(-2, changed.Delta);
        Assert.Equal("card_played", changed.Reason);
    }
}
