namespace Core.Helpers;

/// <summary>
/// Helper class for path resolution operations
/// </summary>
public static class PathResolver
{
    /// <summary>
    /// Gets the user data path for HeroScript
    /// </summary>
    /// <returns>Path to user data directory</returns>
    public static string GetUserDataPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "HeroScript");
    }

    /// <summary>
    /// Gets the path for a specific configuration
    /// </summary>
    /// <param name="configName">Name of the configuration</param>
    /// <returns>Full path to the configuration directory</returns>
    public static string GetConfigPath(string configName)
    {
        return Path.Combine(GetUserDataPath(), configName);
    }

    /// <summary>
    /// Gets the path to config.json for a specific configuration
    /// </summary>
    /// <param name="configName">Name of the configuration</param>
    /// <returns>Full path to config.json</returns>
    public static string GetConfigJsonPath(string configName)
    {
        return Path.Combine(GetConfigPath(configName), "config.json");
    }

    /// <summary>
    /// Gets the path to the Resources directory for a specific configuration
    /// </summary>
    /// <param name="configName">Name of the configuration</param>
    /// <returns>Full path to Resources directory</returns>
    public static string GetConfigResourcesPath(string configName)
    {
        return Path.Combine(GetConfigPath(configName), "Resources");
    }

    /// <summary>
    /// Gets the path to MathFormulas.json for a specific configuration
    /// </summary>
    /// <param name="configName">Name of the configuration</param>
    /// <returns>Full path to MathFormulas.json</returns>
    public static string GetMathFormulasPath(string configName)
    {
        return Path.Combine(GetConfigPath(configName), "Resources", "Pipelines", "MathFormulas.json");
    }

    /// <summary>
    /// Combines a base path with a relative path safely
    /// </summary>
    /// <param name="basePath">Base directory path</param>
    /// <param name="relativePath">Relative path to combine</param>
    /// <returns>Combined full path</returns>
    public static string CombineSafe(string basePath, string relativePath)
    {
        return Path.Combine(basePath, relativePath);
    }

    /// <summary>
    /// Finds the project root by looking for HeroScript.sln
    /// </summary>
    /// <param name="startPath">Starting directory path</param>
    /// <returns>Project root path or null if not found</returns>
    public static string? FindProjectRoot(string? startPath = null)
    {
        var current = startPath ?? AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "HeroScript.sln")))
                return current;

            var parent = Directory.GetParent(current);
            if (parent == null)
                break;

            current = parent.FullName;
        }

        return null;
    }

    /// <summary>
    /// Gets the build output Resources path
    /// </summary>
    /// <returns>Path to Resources in build output</returns>
    public static string GetBuildResourcesPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Resources");
    }

    /// <summary>
    /// Gets the development Resources path (src/Core/Resources)
    /// </summary>
    /// <returns>Path to Resources in source code or null if not found</returns>
    public static string? GetDevResourcesPath()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null)
            return null;

        return Path.Combine(projectRoot, "src", "Core", "Resources");
    }
}
