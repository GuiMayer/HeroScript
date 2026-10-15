using API.Tests;
using System.Text.Json;

// Isolated test host/store. Never contacts or restarts the player's API.
if (args.Length != 1) throw new ArgumentException("Supply the explicit output .json path");
var path = Path.GetFullPath(args[0]);
if (Path.GetExtension(path) != ".json") throw new ArgumentException("Output must be JSON");
using var factory = new TestWebApplicationFactory();
using var client = factory.CreateClient();
using var response = await client.GetAsync("/openapi/v1.json");
var text = await response.Content.ReadAsStringAsync();
if (!response.IsSuccessStatusCode) throw new InvalidOperationException(text);
using var document = JsonDocument.Parse(text);
if (!document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/runs/{runId}/session", out _))
    throw new InvalidOperationException("Export does not include the session contract");
await File.WriteAllTextAsync(path, text + Environment.NewLine);
Console.WriteLine($"Exported {path}");
