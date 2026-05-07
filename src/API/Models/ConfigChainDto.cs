namespace API.Models;

/// <summary>
/// Configuration inheritance chain information
/// </summary>
public class ConfigChainDto
{
    /// <summary>
    /// Current active configuration name
    /// </summary>
    public string CurrentConfig { get; set; } = string.Empty;

    /// <summary>
    /// Inheritance chain (ordered from base to current)
    /// </summary>
    public List<string> InheritanceChain { get; set; } = new();

    /// <summary>
    /// Human-readable chain description (e.g., "base -> mod1 -> mod2")
    /// </summary>
    public string ChainDescription { get; set; } = string.Empty;

    /// <summary>
    /// Metadata for each config in the chain
    /// </summary>
    public List<ConfigInfoDto> ChainMetadata { get; set; } = new();
}
