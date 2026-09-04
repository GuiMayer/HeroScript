using Core.Combat.TurnOrder;

namespace Core.Config;

/// <summary>
/// Configuration options for the combat system
/// </summary>
public class CombatOptions
{
    /// <summary>
    /// Complete turn-order definition for direct, host-managed combats.
    /// </summary>
    public string TurnOrderStrategy { get; set; } = string.Empty;
    public string? TurnOrderResourceId { get; set; }
    public float? TurnOrderMissingResourceValue { get; set; }
    public int InitiativeDieSides { get; set; }
    public float InitiativeResourcePerModifier { get; set; }
    public float AtbFillRate { get; set; }
    public float AtbReferenceResourceValue { get; set; }
    public float AtbReadyThreshold { get; set; }
    public string? TurnOrderPriorityResourceId { get; set; }
    public float TurnOrderPriorityThreshold { get; set; }

    public TurnOrderConfiguration ToTurnOrderConfiguration()
    {
        if (!Enum.TryParse<TurnStrategy>(TurnOrderStrategy, true, out var strategy))
            strategy = TurnStrategy.UNSPECIFIED;
        return new TurnOrderConfiguration
        {
            Strategy = strategy,
            OrderResourceId = TurnOrderResourceId,
            MissingResourceValue = TurnOrderMissingResourceValue,
            InitiativeDieSides = InitiativeDieSides,
            InitiativeResourcePerModifier = InitiativeResourcePerModifier,
            AtbFillRate = AtbFillRate,
            AtbReferenceResourceValue = AtbReferenceResourceValue,
            AtbReadyThreshold = AtbReadyThreshold,
            PriorityResourceId = TurnOrderPriorityResourceId,
            PriorityThreshold = TurnOrderPriorityThreshold
        };
    }
}
