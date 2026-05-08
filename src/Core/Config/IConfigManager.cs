using System.Collections.Generic;

namespace Core.Config;

/// <summary>
/// Interface for configuration management service.
/// Manages loading, switching, and querying game configurations.
/// </summary>
public interface IConfigManager
{
    /// <summary>
    /// Gets the name of the currently loaded configuration.
    /// </summary>
    string CurrentConfig { get; }

    /// <summary>
    /// Gets the default configuration name.
    /// </summary>
    string DefaultConfig { get; }

    /// <summary>
    /// Gets the base path where user configurations are stored.
    /// </summary>
    string UserConfigsPath { get; }

    /// <summary>
    /// Loads a configuration by name and sets it as the current configuration.
    /// </summary>
    /// <param name="configName">Name of the configuration to load</param>
    /// <exception cref="InvalidOperationException">Thrown if configuration is invalid or not found</exception>
    void LoadConfig(string configName);

    /// <summary>
    /// Gets the physical path to a configuration directory.
    /// </summary>
    /// <param name="configName">Name of the configuration</param>
    /// <returns>Full path to the configuration directory</returns>
    string GetConfigPath(string configName);

    /// <summary>
    /// Gets the user data path where configurations are stored.
    /// </summary>
    /// <returns>Full path to the user data directory</returns>
    string GetUserDataPath();

    /// <summary>
    /// Resolves the inheritance chain for a configuration.
    /// </summary>
    /// <param name="configName">Name of the configuration</param>
    /// <returns>List of configuration names from base to most derived</returns>
    IEnumerable<string> ResolveInheritanceChain(string configName);

    /// <summary>
    /// Gets metadata for a specific configuration.
    /// </summary>
    /// <param name="configName">Name of the configuration</param>
    /// <returns>Configuration metadata, or null if not found</returns>
    ConfigMetadata? GetConfigMetadata(string configName);

    /// <summary>
    /// Gets a list of all available configuration names.
    /// </summary>
    /// <returns>Enumerable of configuration names</returns>
    IEnumerable<string> GetAvailableConfigs();

    /// <summary>
    /// Checks if a configuration exists.
    /// </summary>
    /// <param name="configName">Name of the configuration to check</param>
    /// <returns>True if the configuration exists, false otherwise</returns>
    bool ConfigExists(string configName);
}
