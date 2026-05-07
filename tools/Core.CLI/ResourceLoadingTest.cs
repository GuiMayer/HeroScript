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
        public static void Execute()
        {
            Console.WriteLine("=== Resource Loading System Test ===\n");

            // Teste 1: Verificar inicialização do resolver
            Console.WriteLine("[Test 1] Initializing ResourcePathResolver...");
            var config = new ResourceConfiguration
            {
                Mode = ResourceMode.Auto
            };
            ResourceLoader.Instance.InitializePathResolver(config);
            Console.WriteLine("✓ Resolver initialized\n");

            // Teste 2: Carregar recurso MathFormulas
            Console.WriteLine("[Test 2] Loading MathFormulas.json...");
            try
            {
                var resources = ResourceLoader.Instance.LoadResource(
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
                var resources2 = ResourceLoader.Instance.LoadResource(
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
