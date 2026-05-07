using System.Text.Json;

var jsonPath = "MathFormulas.json";
var jsonContent = File.ReadAllText(jsonPath);

Console.WriteLine($"File size: {jsonContent.Length} bytes\n");

var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);

if (dict == null)
{
    Console.WriteLine("Failed to deserialize JSON");
    return;
}

Console.WriteLine($"Deserialized {dict.Count} keys:\n");
foreach (var key in dict.Keys.OrderBy(k => k))
{
    Console.WriteLine($"  - {key}");
}

Console.WriteLine($"\nLooking for LERP: {(dict.ContainsKey("LERP") ? "FOUND" : "NOT FOUND")}");
