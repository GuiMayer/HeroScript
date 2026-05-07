namespace API.Models;

/// <summary>
/// Information about resource origins in the configuration system
/// </summary>
public class ResourceOriginDto
{
    /// <summary>
    /// Resource path (e.g., "Pipelines/MathFormulas.json")
    /// </summary>
    public string ResourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Origins of each key in the resource (key -> config source)
    /// </summary>
    public Dictionary<string, string> Origins { get; set; } = new();
}
