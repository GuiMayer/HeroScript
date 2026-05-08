using System.Text.Json;

namespace Core.Helpers;

/// <summary>
/// Helper class for JSON file operations
/// </summary>
public static class JsonFileHelper
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Reads and deserializes a JSON file
    /// </summary>
    /// <typeparam name="T">Type to deserialize to</typeparam>
    /// <param name="filePath">Path to the JSON file</param>
    /// <param name="options">Optional serializer options</param>
    /// <returns>Deserialized object or null if file doesn't exist or deserialization fails</returns>
    public static T? ReadJsonFile<T>(string filePath, JsonSerializerOptions? options = null) where T : class
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            string jsonContent = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(jsonContent, options ?? DefaultOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads and deserializes a JSON file with exception handling
    /// </summary>
    /// <typeparam name="T">Type to deserialize to</typeparam>
    /// <param name="filePath">Path to the JSON file</param>
    /// <param name="options">Optional serializer options</param>
    /// <returns>Tuple with success flag and deserialized object</returns>
    public static (bool Success, T? Data, string? Error) TryReadJsonFile<T>(string filePath, JsonSerializerOptions? options = null) where T : class
    {
        if (!File.Exists(filePath))
            return (false, null, $"File not found: {filePath}");

        try
        {
            string jsonContent = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<T>(jsonContent, options ?? DefaultOptions);
            
            if (data == null)
                return (false, null, "Deserialization returned null");

            return (true, data, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Writes an object to a JSON file
    /// </summary>
    /// <typeparam name="T">Type to serialize</typeparam>
    /// <param name="filePath">Path to the JSON file</param>
    /// <param name="data">Object to serialize</param>
    /// <param name="options">Optional serializer options</param>
    /// <returns>True if successful, false otherwise</returns>
    public static bool WriteJsonFile<T>(string filePath, T data, JsonSerializerOptions? options = null) where T : class
    {
        try
        {
            string jsonContent = JsonSerializer.Serialize(data, options ?? DefaultOptions);
            File.WriteAllText(filePath, jsonContent);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes an object to a JSON file with exception handling
    /// </summary>
    /// <typeparam name="T">Type to serialize</typeparam>
    /// <param name="filePath">Path to the JSON file</param>
    /// <param name="data">Object to serialize</param>
    /// <param name="options">Optional serializer options</param>
    /// <returns>Tuple with success flag and error message</returns>
    public static (bool Success, string? Error) TryWriteJsonFile<T>(string filePath, T data, JsonSerializerOptions? options = null) where T : class
    {
        try
        {
            string jsonContent = JsonSerializer.Serialize(data, options ?? DefaultOptions);
            File.WriteAllText(filePath, jsonContent);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
