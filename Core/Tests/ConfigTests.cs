using Core.Config;
using M = Core.Math;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Core.Tests
{
    public static class ConfigTests
    {
        private static int _totalTests = 0;
        private static int _passedTests = 0;
        private static int _failedTests = 0;

        public static void RunAllTests()
        {
            Console.WriteLine("\n=== [ CONFIG SYSTEM: TESTES DE CONFIGURAÇÃO ] ===\n");

            // Testes básicos de validação
            TestConfigValidation();

            // Testes de herança
            TestConfigInheritance();

            // Testes de origens de fórmulas
            TestFormulaOrigins();

            // Testes de ciclos de herança
            TestCircularInheritance();

            PrintSummary();
        }

        private static void TestConfigValidation()
        {
            Console.WriteLine("=== TESTES DE VALIDAÇÃO ===\n");

            // Teste 1: Validar config inexistente deve falhar
            TestValidationFails("config_inexistente_12345", "Config inexistente deve falhar validação");

            // Teste 2: Validar config dev (fallback) deve passar com warnings
            TestValidationPasses("dev", "Config dev (fallback) deve passar", expectWarnings: true);
        }

        private static void TestConfigInheritance()
        {
            Console.WriteLine("\n=== TESTES DE HERANÇA DELTA ===\n");

            // Teste 3: Carregar config sem herança (dev mode)
            TestLoadConfig("dev", "Carregar config dev sem herança");

            // Teste 4: Verificar que fórmulas base existem
            TestFormulaExists("HYPERBOLIC_CURVE", "Fórmula base HYPERBOLIC_CURVE deve existir");
            TestFormulaExists("LINEAR_ADDITIVE", "Fórmula base LINEAR_ADDITIVE deve existir");

            // Teste 5: Contar fórmulas carregadas
            TestFormulaCount(17, "Config dev deve ter 17 fórmulas base");
        }

        private static void TestFormulaOrigins()
        {
            Console.WriteLine("\n=== TESTES DE ORIGEM DE FÓRMULAS ===\n");

            // Teste 6: Verificar origem das fórmulas
            TestFormulaOrigin("HYPERBOLIC_CURVE", "dev", "HYPERBOLIC_CURVE deve vir de dev");
            TestFormulaOrigin("LINEAR_ADDITIVE", "dev", "LINEAR_ADDITIVE deve vir de dev");
        }

        private static void TestCircularInheritance()
        {
            Console.WriteLine("\n=== TESTES DE CICLOS DE HERANÇA ===\n");

            // Teste 7: Detectar ciclo de herança (simulado)
            TestCircularInheritanceDetection();
        }

        // ===== MÉTODOS DE TESTE =====

        private static void TestValidationFails(string configName, string description)
        {
            _totalTests++;
            try
            {
                var result = ConfigValidator.ValidateConfigSafe(configName);
                
                if (!result.IsValid)
                {
                    _passedTests++;
                    Console.WriteLine($"✓ PASS | {description}");
                }
                else
                {
                    _failedTests++;
                    Console.WriteLine($"✗ FAIL | {description} - Validação deveria ter falhado");
                }
            }
            catch (Exception ex)
            {
                _failedTests++;
                Console.WriteLine($"✗ ERROR | {description} - {ex.Message}");
            }
        }

        private static void TestValidationPasses(string configName, string description, bool expectWarnings = false)
        {
            _totalTests++;
            try
            {
                var result = ConfigValidator.ValidateConfigSafe(configName);
                
                if (result.IsValid)
                {
                    if (expectWarnings && result.Warnings.Count > 0)
                    {
                        _passedTests++;
                        Console.WriteLine($"✓ PASS | {description} (com {result.Warnings.Count} warnings)");
                    }
                    else if (!expectWarnings && result.Warnings.Count == 0)
                    {
                        _passedTests++;
                        Console.WriteLine($"✓ PASS | {description}");
                    }
                    else
                    {
                        _failedTests++;
                        Console.WriteLine($"✗ FAIL | {description} - Warnings inesperados: {result.Warnings.Count}");
                    }
                }
                else
                {
                    _failedTests++;
                    Console.WriteLine($"✗ FAIL | {description} - Validação falhou");
                    foreach (var error in result.Errors)
                        Console.WriteLine($"       Error: {error}");
                }
            }
            catch (Exception ex)
            {
                _failedTests++;
                Console.WriteLine($"✗ ERROR | {description} - {ex.Message}");
            }
        }

        private static void TestLoadConfig(string configName, string description)
        {
            _totalTests++;
            try
            {
                ConfigManager.LoadConfig(configName);
                _passedTests++;
                Console.WriteLine($"✓ PASS | {description}");
            }
            catch (Exception ex)
            {
                _failedTests++;
                Console.WriteLine($"✗ ERROR | {description} - {ex.Message}");
            }
        }

        private static void TestFormulaExists(string formulaName, string description)
        {
            _totalTests++;
            try
            {
                var engine = new M.MathEngine();
                var formulas = engine.GetAvailableFormulas();
                
                bool exists = false;
                foreach (var name in formulas)
                {
                    if (name.Equals(formulaName, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    _passedTests++;
                    Console.WriteLine($"✓ PASS | {description}");
                }
                else
                {
                    _failedTests++;
                    Console.WriteLine($"✗ FAIL | {description} - Fórmula não encontrada");
                }
            }
            catch (Exception ex)
            {
                _failedTests++;
                Console.WriteLine($"✗ ERROR | {description} - {ex.Message}");
            }
        }

        private static void TestFormulaCount(int expectedCount, string description)
        {
            _totalTests++;
            try
            {
                var engine = new M.MathEngine();
                var formulas = engine.GetAvailableFormulas();
                
                int count = 0;
                foreach (var _ in formulas)
                    count++;

                if (count == expectedCount)
                {
                    _passedTests++;
                    Console.WriteLine($"✓ PASS | {description} (encontradas: {count})");
                }
                else
                {
                    _failedTests++;
                    Console.WriteLine($"✗ FAIL | {description} - Esperado: {expectedCount}, Encontrado: {count}");
                }
            }
            catch (Exception ex)
            {
                _failedTests++;
                Console.WriteLine($"✗ ERROR | {description} - {ex.Message}");
            }
        }

        private static void TestFormulaOrigin(string formulaName, string expectedOrigin, string description)
        {
            _totalTests++;
            try
            {
                var origins = M.MathEngine.GetFormulaOrigins();
                
                if (origins.TryGetValue(formulaName, out string? origin))
                {
                    if (origin.Equals(expectedOrigin, StringComparison.OrdinalIgnoreCase))
                    {
                        _passedTests++;
                        Console.WriteLine($"✓ PASS | {description}");
                    }
                    else
                    {
                        _failedTests++;
                        Console.WriteLine($"✗ FAIL | {description} - Esperado: {expectedOrigin}, Encontrado: {origin}");
                    }
                }
                else
                {
                    _failedTests++;
                    Console.WriteLine($"✗ FAIL | {description} - Fórmula não encontrada em origins");
                }
            }
            catch (Exception ex)
            {
                _failedTests++;
                Console.WriteLine($"✗ ERROR | {description} - {ex.Message}");
            }
        }

        private static void TestCircularInheritanceDetection()
        {
            _totalTests++;
            
            // Criar configs temporárias com ciclo
            string userDataPath = ConfigManager.GetUserDataPath();
            string configA = Path.Combine(userDataPath, "test-cycle-a");
            string configB = Path.Combine(userDataPath, "test-cycle-b");

            try
            {
                // Criar estrutura de pastas
                Directory.CreateDirectory(Path.Combine(configA, "Resources", "Pipelines"));
                Directory.CreateDirectory(Path.Combine(configB, "Resources", "Pipelines"));

                // Config A herda de B
                var metadataA = new ConfigMetadata
                {
                    Name = "Test Cycle A",
                    Version = "1.0.0",
                    Author = "test",
                    Parent = "test-cycle-b"
                };
                File.WriteAllText(
                    Path.Combine(configA, "config.json"),
                    JsonSerializer.Serialize(metadataA, new JsonSerializerOptions { WriteIndented = true })
                );

                // Config B herda de A (ciclo!)
                var metadataB = new ConfigMetadata
                {
                    Name = "Test Cycle B",
                    Version = "1.0.0",
                    Author = "test",
                    Parent = "test-cycle-a"
                };
                File.WriteAllText(
                    Path.Combine(configB, "config.json"),
                    JsonSerializer.Serialize(metadataB, new JsonSerializerOptions { WriteIndented = true })
                );

                // Criar MathFormulas.json vazios
                File.WriteAllText(
                    Path.Combine(configA, "Resources", "Pipelines", "MathFormulas.json"),
                    "{}"
                );
                File.WriteAllText(
                    Path.Combine(configB, "Resources", "Pipelines", "MathFormulas.json"),
                    "{}"
                );

                // Tentar resolver cadeia (deve lançar exceção)
                try
                {
                    var chain = ConfigManager.ResolveInheritanceChain("test-cycle-a");
                    _failedTests++;
                    Console.WriteLine($"✗ FAIL | Ciclo de herança não detectado");
                }
                catch (InvalidOperationException ex)
                {
                    if (ex.Message.Contains("Circular inheritance"))
                    {
                        _passedTests++;
                        Console.WriteLine($"✓ PASS | Ciclo de herança detectado corretamente");
                    }
                    else
                    {
                        _failedTests++;
                        Console.WriteLine($"✗ FAIL | Exceção incorreta: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _failedTests++;
                Console.WriteLine($"✗ ERROR | Erro ao criar configs de teste - {ex.Message}");
            }
            finally
            {
                // Limpar configs temporárias
                try
                {
                    if (Directory.Exists(configA))
                        Directory.Delete(configA, true);
                    if (Directory.Exists(configB))
                        Directory.Delete(configB, true);
                }
                catch
                {
                    // Ignorar erros de limpeza
                }
            }
        }

        public static void PrintSummary()
        {
            Console.WriteLine("\n=== RESUMO DOS TESTES DE CONFIGURAÇÃO ===");
            Console.WriteLine($"Total de testes executados: {_totalTests}");
            Console.WriteLine($"Testes bem-sucedidos: {_passedTests}");
            Console.WriteLine($"Testes falhos: {_failedTests}");

            if (_failedTests == 0)
            {
                Console.WriteLine("\n✓ TODOS OS TESTES DE CONFIGURAÇÃO PASSARAM!");
            }
            else
            {
                Console.WriteLine($"\n✗ {_failedTests} TESTE(S) DE CONFIGURAÇÃO FALHARAM");
            }
        }

        public static void ResetCounters()
        {
            _totalTests = 0;
            _passedTests = 0;
            _failedTests = 0;
        }

        public static (int total, int passed, int failed) GetTestResults()
        {
            return (_totalTests, _passedTests, _failedTests);
        }
    }
}
