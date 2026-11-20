using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace HeroScript.Mcp;

[McpServerToolType]
public sealed class EngineTools(HeroScriptApiClient api)
{
    [McpServerTool(Name = "get_engine_status", ReadOnly = true), Description("Check access to the local HeroScript API without changing game state.")]
    public async Task<object> Status(CancellationToken token)
    {
        var settings = await api.Get("/api/v1/content/settings", token);
        return new { available = true, contract = "api/v1", settings = settings.GetProperty("items").GetArrayLength() };
    }
}
