using Core.Events.Domain;
using Core.Resources;
using Xunit;

namespace Core.Tests.Events;

public sealed class PhaseAndResourceEventsTests
{
    [Fact]
    public void ResourceChangedEvent_RecordsGenericFieldAndSignedAmount()
    {
        var changed = new ResourceChangedEvent
        {
            CombatId = Guid.NewGuid(),
            OwnerId = "hero",
            ResourceId = "mana",
            Field = ResourceValueField.Current,
            PreviousValue = 3,
            CurrentValue = 1,
            Reason = "card_played"
        };

        Assert.Equal(-2, changed.SignedAmount);
        Assert.Equal("mana", changed.ResourceId);
        Assert.Equal(ResourceValueField.Current, changed.Field);
        Assert.Equal("card_played", changed.Reason);
    }
}
