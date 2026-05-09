using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

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
            // Por exemplo, substituir serviços por mocks
        });

        builder.UseEnvironment("Test");
    }
}
