using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.Tests;

/// <summary>
/// Factory para criar instâncias de teste da API
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Configurações adicionais de teste podem ser adicionadas aqui
            // Por exemplo, substituir serviços por mocks se necessário
        });

        // Usar ambiente de teste
        builder.UseEnvironment("Development");
        
        // Configurar content root para encontrar arquivos de configuração
        builder.UseContentRoot(Directory.GetCurrentDirectory());
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Garantir que o diretório de trabalho está correto
        var projectRoot = FindProjectRoot();
        if (projectRoot != null)
        {
            Directory.SetCurrentDirectory(projectRoot);
        }
        
        return base.CreateHost(builder);
    }

    private static string? FindProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        
        // Procurar pela pasta src/API que contém os arquivos de configuração
        while (directory != null)
        {
            var apiPath = Path.Combine(directory, "src", "API");
            if (Directory.Exists(apiPath))
            {
                return directory;
            }
            
            directory = Directory.GetParent(directory)?.FullName;
        }
        
        return null;
    }
}
