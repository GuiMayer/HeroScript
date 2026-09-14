namespace API.Models;

/// <summary>
/// Trusted host-side access profile. Unlike game-mode policies, this setting is
/// not loaded from moddable content and therefore cannot be elevated by a mod.
/// </summary>
public sealed class ToolAccessSettings
{
    public string Profile { get; set; } = ToolAccessProfiles.Normal;
    public string[] CustomCapabilities { get; set; } = [];
}

public static class ToolAccessProfiles
{
    public const string Normal = "normal";
    public const string Experimental = "experimental";
    public const string Sandbox = "sandbox";
    public const string DevModder = "dev_modder";
    public const string Custom = "custom";
}
