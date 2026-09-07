using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace API.Tests;

/// <summary>
/// Factory para criar instâncias de teste da API
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _persistenceRoot = Path.Combine(
        Path.GetTempPath(),
        "HeroScript",
        "api-integration-tests",
        Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var projectRoot = FindProjectRoot();

        // Test hosts must not write to the Windows Event Log, which is unavailable
        // in ordinary developer and CI environments.
        builder.ConfigureLogging(logging => logging.ClearProviders());

        // Usar ambiente de teste
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Enabled", "true");
        builder.UseSetting("Admin:ApiKey", "dev-admin-key");
        builder.UseSetting("Combat:TurnOrderStrategy", "FIXED");
        builder.UseSetting("Persistence:OperationalTelemetryPath", Path.Combine(_persistenceRoot, "telemetry"));
        builder.UseSetting("Persistence:RunStatePath", Path.Combine(_persistenceRoot, "runs"));
        builder.UseSetting("Persistence:ContentStorePath", Path.Combine(_persistenceRoot, "content"));
         
        // Configurar content root para encontrar arquivos de configuração
        if (projectRoot != null)
        {
            builder.UseContentRoot(projectRoot);
        }
    }

    private static string? FindProjectRoot()
    {
        // Allow CI/test environment to specify root explicitly
        var envRoot = Environment.GetEnvironmentVariable("HERESCRIPT_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return envRoot;
        }
        
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        
        // Limit depth to prevent scanning to filesystem root
        for (int depth = 0; depth < 12 && dir != null; depth++)
        {
            var apiPath = Path.Combine(dir.FullName, "src", "API");
            if (Directory.Exists(apiPath))
            {
                return dir.FullName;
            }
            
            dir = dir.Parent;
        }
        
        return null;
    }

}
