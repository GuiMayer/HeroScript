using HeroScript.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new HeroScriptApiClient(McpSafetyPolicy.LocalUri(
    Environment.GetEnvironmentVariable("HEROSCRIPT_MCP_API_URL") ?? "http://127.0.0.1:5271")));
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<EngineTools>();
await builder.Build().RunAsync();
