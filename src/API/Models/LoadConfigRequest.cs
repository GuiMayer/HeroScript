namespace API.Models;

/// <summary>
/// Request to load a configuration
/// </summary>
public class LoadConfigRequest
{
    /// <summary>
    /// Configuration name to load
    /// </summary>
    public string ConfigName { get; set; } = string.Empty;

    /// <summary>
    /// Force load even if there are warnings (default: false)
    /// </summary>
    public bool Force { get; set; } = false;
}
