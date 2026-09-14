namespace API.Models;

/// <summary>
/// Settings for configuration reload operations
/// </summary>
public class ConfigReloadSettings
{
    /// <summary>
    /// Whether configuration reload is enabled (default: false)
    /// Set AllowConfigReload=true in the environment or appsettings.json to enable
    /// </summary>
    public bool Enabled { get; set; } = false;
}
