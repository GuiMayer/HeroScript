using Core.CardZones;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneDefinitionsTests
{
    [Fact]
    public void Definitions_DefensivelyCopyAndCanonicalizeAuthoredCollections()
    {
        var triggers = new List<string> { "activation.started", "activation.started", " encounter.started " };
        var zones = new List<CardZoneDefinition>
        {
            new() { ZoneId = "library", OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered }
        };
        var definition = new CardZoneSystemDefinition
        {
            CardZoneSystemId = "test",
            Zones = zones,
            Flows =
            [
                new CardZoneFlowDefinition
                {
                    FlowId = "prepare",
                    Triggers = triggers,
                    AllowedInvocations = [CardZoneFlowInvocation.Boundary],
                    Steps =
                    [
                        new CardZoneFlowStepDefinition
                        {
                            StepId = "move",
                            Operation = CardZoneOperation.Move,
                            SourceZoneId = "library",
                            TargetZoneId = "ready",
                            Selection = new() { Strategy = CardZoneSelectionStrategy.Top, Count = 1 }
                        }
                    ]
                }
            ]
        };

        zones.Clear();
        triggers.Clear();

        Assert.Single(definition.Zones);
        Assert.Equal(["activation.started", "encounter.started"], definition.Flows[0].Triggers);
        Assert.Equal("library", definition.Flows[0].Steps[0].SourceZoneId);
    }
}
