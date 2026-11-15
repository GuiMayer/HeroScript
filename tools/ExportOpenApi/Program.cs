using API.Tests;
using System.Text.Json;

// Isolated test host/store. Never contacts or restarts the player's API.
var (authoring, check, output) = args switch
{
    [var target] => (false, false, target),
    ["--authoring", var target] => (true, false, target),
    ["--authoring", "--check", var target] => (true, true, target),
    _ => throw new ArgumentException("Usage: ExportOpenApi [--authoring [--check]] <output.json>")
};
var path = Path.GetFullPath(output);
if (Path.GetExtension(path) != ".json") throw new ArgumentException("Output must be JSON");
using var factory = new TestWebApplicationFactory();
using var client = factory.CreateClient();
using var response = await client.GetAsync(authoring ? "/openapi/authoring-v1.json" : "/openapi/v1.json");
var text = await response.Content.ReadAsStringAsync();
if (!response.IsSuccessStatusCode) throw new InvalidOperationException(text);
using var document = JsonDocument.Parse(text);
var expectedRoute = authoring
    ? "/api/v1/admin/authoring/settings/{settingId}/changes/validate"
    : "/api/v1/runs/{runId}/session";
if (!document.RootElement.GetProperty("paths").TryGetProperty(expectedRoute, out _))
    throw new InvalidOperationException($"Export does not include {expectedRoute}");
if (check)
{
    using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    if (!JsonElement.DeepEquals(document.RootElement, snapshot.RootElement))
    {
        Console.Error.WriteLine($"OpenAPI snapshot differs from the runtime. Regenerate {path} with --authoring.");
        return 1;
    }
    Console.WriteLine($"Verified {path}");
    return 0;
}
await File.WriteAllTextAsync(path, text + "\n");
Console.WriteLine($"Exported {path}");
return 0;
