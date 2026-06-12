namespace Core.Config;

/// <summary>
/// Configuration options for the combat system
/// </summary>
public class CombatOptions
{
    /// <summary>
    /// Gets or sets the turn order strategy to use
    /// Valid values: "fixed", "speed_based", "initiative", "atb", "conditional"
    /// Default: "fixed"
    /// </summary>
    public string TurnOrderStrategy { get; set; } = "fixed";
}
