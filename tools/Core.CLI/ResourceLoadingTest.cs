using System;
using System.Linq;
using Core.Config;

namespace Core.CLI
{
    /// <summary>
    /// Teste simples do sistema de carregamento de recursos
    /// </summary>
    public class ResourceLoadingTest
    {
        public static void Execute(IConfigManager configManager)
        {
            Console.WriteLine("=== Resource Loading System Test ===\n");

            // Teste 1: Verificar configuração atual
            Console.WriteLine("[Test 1] Checking current configuration...");
            Console.WriteLine($"✓ Current config: {configManager.CurrentConfig}\n");

            // Initialize resource loader
            var logger = new ConsoleLogger();
            var providerFactory = new ResourceProviderFactory(configManager);
            var resourceLoader = new ResourceLoader(logger, providerFactory);

            // Teste 2: Carregar recurso MathFormulas
            Console.WriteLine("[Test 2] Loading MathFormulas.json...");
            try
            {
                var resources = resourceLoader.LoadResource(
                    "Pipelines/MathFormulas.json",
                    new[] { "alisyum" },
                    strictMode: false
                );

                Console.WriteLine($"✓ Loaded {resources.Count} formulas");
                
                if (resources.Count > 0)
                {
                    Console.WriteLine("\nFormulas found:");
                    foreach (var key in resources.Keys.Take(5))
                    {
                        Console.WriteLine($"  - {key}");
                    }
                    if (resources.Count > 5)
                    {
                        Console.WriteLine($"  ... and {resources.Count - 5} more");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
            }

            // Teste 3: Verificar cache
            Console.WriteLine("\n[Test 3] Testing cache...");
            try
            {
                var resources2 = resourceLoader.LoadResource(
                    "Pipelines/MathFormulas.json",
                    new[] { "alisyum" },
                    strictMode: false
                );
                Console.WriteLine($"✓ Cache working - returned {resources2.Count} formulas");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
            }

            Console.WriteLine("\n=== Test Complete ===");
        }
    }
}
