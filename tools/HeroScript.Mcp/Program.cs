using HeroScript.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new HeroScriptApiClient(McpSafetyPolicy.LocalUri(
    Environment.GetEnvironmentVariable("HEROSCRIPT_MCP_API_URL") ?? "http://127.0.0.1:5271")));
var sandboxUrl = Environment.GetEnvironmentVariable("HEROSCRIPT_MCP_SANDBOX_URL");
builder.Services.AddSingleton(new SandboxSession(string.IsNullOrWhiteSpace(sandboxUrl) ? null : new HeroScriptApiClient(McpSafetyPolicy.LocalUri(sandboxUrl))));
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<EngineTools>().WithTools<SimulationTools>().WithTools<BalanceTools>();
await builder.Build().RunAsync();
