using API.Models;
using Core.Config;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Controller for managing configurations and inheritance chains
/// </summary>
[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly ILogger<ConfigController> _logger;
    private readonly ConfigReloadSettings _reloadSettings;

    public ConfigController(ILogger<ConfigController> logger, ConfigReloadSettings reloadSettings)
    {
        _logger = logger;
        _reloadSettings = reloadSettings;
    }

    /// <summary>
    /// Get list of all available configurations
    /// </summary>
    /// <returns>List of configuration information</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<ConfigInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetConfigs()
    {
        try
        {
            var configs = ConfigManager.GetAvailableConfigs();
            var currentConfig = ConfigManager.CurrentConfig;
            var userDataPath = ConfigManager.GetUserDataPath();

            var result = configs.Select(configName =>
            {
                var metadata = ConfigManager.GetConfigMetadata(configName);
                if (metadata == null)
                {
                    return new ConfigInfoDto
                    {
                        Name = configName,
                        IsActive = configName.Equals(currentConfig, StringComparison.OrdinalIgnoreCase),
                        Path = configName,
                        Version = "unknown",
                        Author = "unknown",
                        Description = "Metadata not available",
                        CreatedAt = string.Empty
                    };
                }

                return new ConfigInfoDto
                {
                    Name = metadata.Name,
                    Version = metadata.Version,
                    Author = metadata.Author,
                    Description = metadata.Description,
                    Parent = metadata.Parent,
                    CreatedAt = metadata.CreatedAt,
                    IsActive = configName.Equals(currentConfig, StringComparison.OrdinalIgnoreCase),
                    Path = configName
                };
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting configurations");
            return StatusCode(500, new { error = "Failed to load configurations", details = ex.Message });
        }
    }

    /// <summary>
    /// Get current active configuration with inheritance chain
    /// </summary>
    /// <returns>Current configuration information with chain</returns>
    [HttpGet("current")]
    [ProducesResponseType(typeof(ConfigChainDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetCurrentConfig()
    {
        try
        {
            var currentConfig = ConfigManager.CurrentConfig;
            var chain = ConfigManager.ResolveInheritanceChain(currentConfig);
            var chainDescription = string.Join(" -> ", chain);

            var chainMetadata = chain.Select(configName =>
            {
                var metadata = ConfigManager.GetConfigMetadata(configName);
                if (metadata == null)
                {
                    return new ConfigInfoDto
                    {
                        Name = configName,
                        IsActive = configName.Equals(currentConfig, StringComparison.OrdinalIgnoreCase),
                        Path = configName,
                        Version = "unknown",
                        Author = "unknown",
                        Description = "Metadata not available",
                        CreatedAt = string.Empty
                    };
                }

                return new ConfigInfoDto
                {
                    Name = metadata.Name,
                    Version = metadata.Version,
                    Author = metadata.Author,
                    Description = metadata.Description,
                    Parent = metadata.Parent,
                    CreatedAt = metadata.CreatedAt,
                    IsActive = configName.Equals(currentConfig, StringComparison.OrdinalIgnoreCase),
                    Path = configName
                };
            }).ToList();

            var result = new ConfigChainDto
            {
                CurrentConfig = currentConfig,
                InheritanceChain = chain,
                ChainDescription = chainDescription,
                ChainMetadata = chainMetadata
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current configuration");
            return StatusCode(500, new { error = "Failed to load current configuration", details = ex.Message });
        }
    }

    /// <summary>
    /// Get details of a specific configuration
    /// </summary>
    /// <param name="name">Configuration name</param>
    /// <returns>Configuration information</returns>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ConfigInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetConfig(string name)
    {
        try
        {
            var metadata = ConfigManager.GetConfigMetadata(name);
            if (metadata == null)
            {
                return NotFound(new { error = $"Configuration '{name}' not found" });
            }

            var currentConfig = ConfigManager.CurrentConfig;
            var result = new ConfigInfoDto
            {
                Name = metadata.Name,
                Version = metadata.Version,
                Author = metadata.Author,
                Description = metadata.Description,
                Parent = metadata.Parent,
                CreatedAt = metadata.CreatedAt,
                IsActive = name.Equals(currentConfig, StringComparison.OrdinalIgnoreCase),
                Path = name
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting configuration {ConfigName}", name);
            return StatusCode(500, new { error = "Failed to load configuration", details = ex.Message });
        }
    }

    /// <summary>
    /// Get inheritance chain for a specific configuration
    /// </summary>
    /// <param name="name">Configuration name</param>
    /// <returns>Inheritance chain information</returns>
    [HttpGet("{name}/chain")]
    [ProducesResponseType(typeof(ConfigChainDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetConfigChain(string name)
    {
        try
        {
            // Check if config exists
            var metadata = ConfigManager.GetConfigMetadata(name);
            if (metadata == null)
            {
                return NotFound(new { error = $"Configuration '{name}' not found" });
            }

            var chain = ConfigManager.ResolveInheritanceChain(name);
            var chainDescription = string.Join(" -> ", chain);
            var currentConfig = ConfigManager.CurrentConfig;

            var chainMetadata = chain.Select(configName =>
            {
                var configMeta = ConfigManager.GetConfigMetadata(configName);
                if (configMeta == null)
                {
                    return new ConfigInfoDto
                    {
                        Name = configName,
                        IsActive = configName.Equals(currentConfig, StringComparison.OrdinalIgnoreCase),
                        Path = configName,
                        Version = "unknown",
                        Author = "unknown",
                        Description = "Metadata not available",
                        CreatedAt = string.Empty
                    };
                }

                return new ConfigInfoDto
                {
                    Name = configMeta.Name,
                    Version = configMeta.Version,
                    Author = configMeta.Author,
                    Description = configMeta.Description,
                    Parent = configMeta.Parent,
                    CreatedAt = configMeta.CreatedAt,
                    IsActive = configName.Equals(currentConfig, StringComparison.OrdinalIgnoreCase),
                    Path = configName
                };
            }).ToList();

            var result = new ConfigChainDto
            {
                CurrentConfig = name,
                InheritanceChain = chain,
                ChainDescription = chainDescription,
                ChainMetadata = chainMetadata
            };

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Circular inheritance detected for config {ConfigName}", name);
            return BadRequest(new { error = "Circular inheritance detected", details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inheritance chain for {ConfigName}", name);
            return StatusCode(500, new { error = "Failed to resolve inheritance chain", details = ex.Message });
        }
    }

    /// <summary>
    /// Validate a configuration structure
    /// </summary>
    /// <param name="name">Configuration name</param>
    /// <returns>Validation result</returns>
    [HttpPost("{name}/validate")]
    [ProducesResponseType(typeof(ConfigValidationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult ValidateConfig(string name)
    {
        try
        {
            var validationResult = ConfigValidator.ValidateConfigSafe(name);

            var result = new ConfigValidationDto
            {
                ConfigName = name,
                IsValid = validationResult.IsValid,
                Errors = validationResult.Errors.ToList(),
                Warnings = validationResult.Warnings?.ToList()
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating configuration {ConfigName}", name);
            return StatusCode(500, new { error = "Validation failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Load a configuration (admin operation - requires ALLOW_CONFIG_RELOAD flag)
    /// </summary>
    /// <param name="name">Configuration name</param>
    /// <param name="request">Load configuration request</param>
    /// <returns>Loaded configuration information</returns>
    [HttpPost("{name}/load")]
    [ProducesResponseType(typeof(ConfigChainDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult LoadConfig(string name, [FromBody] LoadConfigRequest? request = null)
    {
        // Check if config reload is enabled
        if (!_reloadSettings.Enabled)
        {
            return StatusCode(403, new
            {
                error = "Configuration reload is disabled",
                details = "Set ALLOW_CONFIG_RELOAD=true in environment or appsettings.json to enable this operation"
            });
        }

        try
        {
            // Check if config exists
            var metadata = ConfigManager.GetConfigMetadata(name);
            if (metadata == null)
            {
                return NotFound(new { error = $"Configuration '{name}' not found" });
            }

            // Validate config before loading
            var validationResult = ConfigValidator.ValidateConfigSafe(name);
            if (!validationResult.IsValid)
            {
                return BadRequest(new
                {
                    error = "Configuration validation failed",
                    validation = new ConfigValidationDto
                    {
                        ConfigName = name,
                        IsValid = false,
                        Errors = validationResult.Errors.ToList(),
                        Warnings = validationResult.Warnings?.ToList()
                    }
                });
            }

            // Log admin operation
            _logger.LogWarning("Loading configuration '{ConfigName}' (admin operation)", name);

            // Load config
            ConfigManager.LoadConfig(name);

            // Return loaded config info
            var chain = ConfigManager.ResolveInheritanceChain(name);
            var chainDescription = string.Join(" -> ", chain);
            var currentConfig = ConfigManager.CurrentConfig;

            var chainMetadata = chain.Select(configName =>
            {
                var configMeta = ConfigManager.GetConfigMetadata(configName);
                if (configMeta == null)
                {
                    return new ConfigInfoDto
                    {
                        Name = configName,
                        IsActive = configName.Equals(currentConfig, StringComparison.OrdinalIgnoreCase),
                        Path = configName,
                        Version = "unknown",
                        Author = "unknown",
                        Description = "Metadata not available",
                        CreatedAt = string.Empty
                    };
                }

                return new ConfigInfoDto
                {
                    Name = configMeta.Name,
                    Version = configMeta.Version,
                    Author = configMeta.Author,
                    Description = configMeta.Description,
                    Parent = configMeta.Parent,
                    CreatedAt = configMeta.CreatedAt,
                    IsActive = configName.Equals(currentConfig, StringComparison.OrdinalIgnoreCase),
                    Path = configName
                };
            }).ToList();

            var result = new ConfigChainDto
            {
                CurrentConfig = currentConfig,
                InheritanceChain = chain,
                ChainDescription = chainDescription,
                ChainMetadata = chainMetadata
            };

            _logger.LogInformation("Configuration '{ConfigName}' loaded successfully", name);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to load configuration {ConfigName}", name);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration {ConfigName}", name);
            return StatusCode(500, new { error = "Failed to load configuration", details = ex.Message });
        }
    }
}
