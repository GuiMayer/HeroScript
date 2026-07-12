using Core.Common;
using Core.Config;
using Core.Logging;
using System.Text.Json;

namespace Core.Resources;

/// <summary>
/// Implementação de persistência de definições de recursos em arquivos JSON.
/// Responsável por salvar, atualizar e deletar arquivos JSON de definições.
/// </summary>
public sealed class DefinitionPersister : IDefinitionPersister
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private readonly string _baseDataPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DefinitionPersister(
        IConfigManager configManager,
        ILogger logger,
        string? baseDataPath = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseDataPath = baseDataPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "configs");
    }

    public Result SaveDefinition(string resourceType, string resourceId, JsonDocument definition, string configName = "default")
    {
        if (string.IsNullOrWhiteSpace(resourceType))
            return Result.Failure("resourceType cannot be empty");
        
        if (string.IsNullOrWhiteSpace(resourceId))
            return Result.Failure("resourceId cannot be empty");
        
        if (definition == null)
            return Result.Failure("definition cannot be null");
        
        if (!IsValidResourceId(resourceId))
            return Result.Failure($"Invalid resourceId '{resourceId}'. Only alphanumeric, hyphens, and underscores are allowed.");

        _lock.Wait();
        try
        {
            var filePath = GetResourceFilePath(configName, resourceType, resourceId);
            
            if (File.Exists(filePath))
                return Result.Failure($"Definition '{resourceId}' already exists in config '{configName}'");

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogDebug($"Created directory: {directory}");
            }

            var jsonString = JsonSerializer.Serialize(definition, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            File.WriteAllText(filePath, jsonString);
            _logger.LogInformation($"Saved definition '{resourceId}' to {filePath}");
            
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError($"Permission denied writing to file: {ex.Message}");
            return Result.Failure($"Permission denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            _logger.LogError($"IO error writing file: {ex.Message}");
            return Result.Failure($"IO error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error saving definition: {ex.Message}");
            return Result.Failure($"Failed to save definition: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public Result UpdateDefinition(string resourceType, string resourceId, JsonDocument definition, string configName = "default")
    {
        if (string.IsNullOrWhiteSpace(resourceType))
            return Result.Failure("resourceType cannot be empty");
        
        if (string.IsNullOrWhiteSpace(resourceId))
            return Result.Failure("resourceId cannot be empty");
        
        if (definition == null)
            return Result.Failure("definition cannot be null");

        _lock.Wait();
        try
        {
            var filePath = GetResourceFilePath(configName, resourceType, resourceId);
            
            if (!File.Exists(filePath))
                return Result.Failure($"Definition '{resourceId}' not found in config '{configName}'");

            // Create backup before updating
            var backupPath = $"{filePath}.bak";
            File.Copy(filePath, backupPath, overwrite: true);
            _logger.LogDebug($"Created backup at {backupPath}");

            try
            {
                var jsonString = JsonSerializer.Serialize(definition, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(filePath, jsonString);
                _logger.LogInformation($"Updated definition '{resourceId}' at {filePath}");
                
                // Delete backup on success
                File.Delete(backupPath);
                
                return Result.Success();
            }
            catch (Exception)
            {
                // Restore backup on failure
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, filePath, overwrite: true);
                    File.Delete(backupPath);
                    _logger.LogWarning($"Restored backup for '{resourceId}' after failed update");
                }
                throw;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError($"Permission denied writing to file: {ex.Message}");
            return Result.Failure($"Permission denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            _logger.LogError($"IO error writing file: {ex.Message}");
            return Result.Failure($"IO error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error updating definition: {ex.Message}");
            return Result.Failure($"Failed to update definition: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public Result DeleteDefinition(string resourceType, string resourceId, string configName = "default")
    {
        if (string.IsNullOrWhiteSpace(resourceType))
            return Result.Failure("resourceType cannot be empty");
        
        if (string.IsNullOrWhiteSpace(resourceId))
            return Result.Failure("resourceId cannot be empty");

        _lock.Wait();
        try
        {
            var filePath = GetResourceFilePath(configName, resourceType, resourceId);
            
            if (!File.Exists(filePath))
                return Result.Failure($"Definition '{resourceId}' not found in config '{configName}'");

            File.Delete(filePath);
            _logger.LogInformation($"Deleted definition '{resourceId}' from {filePath}");
            
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError($"Permission denied deleting file: {ex.Message}");
            return Result.Failure($"Permission denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            _logger.LogError($"IO error deleting file: {ex.Message}");
            return Result.Failure($"IO error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error deleting definition: {ex.Message}");
            return Result.Failure($"Failed to delete definition: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool DefinitionExists(string resourceType, string resourceId, string configName = "default")
    {
        if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(resourceId))
            return false;

        var filePath = GetResourceFilePath(configName, resourceType, resourceId);
        return File.Exists(filePath);
    }

    private string GetResourceFilePath(string configName, string resourceType, string resourceId)
    {
        return Path.Combine(_baseDataPath, configName, "Resources", resourceType, $"{resourceId}.json");
    }

    private static bool IsValidResourceId(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return false;

        foreach (var c in resourceId)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        }

        return true;
    }
}
