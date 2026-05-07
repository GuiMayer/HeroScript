using M = Core.Math;
using System;
using System.Collections.Generic;

namespace Core
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== [ MATH ENGINE: TESTE DE FÓRMULAS DINÂMICAS ] ===\n");

            var engine = new M.MathEngine();

            // Listar todas as fórmulas disponíveis
            Console.WriteLine("Fórmulas disponíveis:");
            foreach (var name in engine.GetAvailableFormulas())
            {
                Console.WriteLine($"  - {name}: {engine.GetFormulaDescription(name)}");
            }
            Console.WriteLine();

            // Testes das fórmulas
            Console.WriteLine("=== EXECUTANDO TESTES ===\n");

            // Teste 1: LINEAR_ADDITIVE (input + 10)
            TestFormula(engine, "LINEAR_ADDITIVE", 5, expectedResult: 15);

            // Teste 2: MULTIPLICATIVE_BUFF (input * 1.5)
            TestFormula(engine, "MULTIPLICATIVE_BUFF", 10, expectedResult: 15);

            // Teste 3: HYPERBOLIC_CURVE ((input + 100) / 100)
            // Exemplo: (50 + 100) = 150, então 100 / 150 = 0.6667
            TestFormula(engine, "HYPERBOLIC_CURVE", 50, expectedResult: 0.6667f, tolerance: 0.001f);

            // Teste 4: EXPONENTIAL_SCALING ((input * 2) ^ 2)
            // Exemplo: (3 * 2) = 6, então 6^2 = 36
            TestFormula(engine, "EXPONENTIAL_SCALING", 3, expectedResult: 36);

            // Teste 5: LOGARITHMIC_SCALING (log(input) * 10)
            // Exemplo: log(2.718281828) ≈ 1, então 1 * 10 = 10
            TestFormula(engine, "LOGARITHMIC_SCALING", 2.718281828f, expectedResult: 10f, tolerance: 0.1f);

            // Teste 6: SIGMOID_CURVE (curva S complexa)
            // No ponto médio (50), deve retornar aproximadamente MAX_VALUE/2 = 50
            TestFormula(engine, "SIGMOID_CURVE", 50, expectedResult: 50f, tolerance: 5f);

            // Teste 7: CLAMP_SATURATION (clamp input entre 0 e 999)
            TestFormula(engine, "CLAMP_SATURATION", 1500, expectedResult: 999);
            TestFormula(engine, "CLAMP_SATURATION", -50, expectedResult: 0);
            TestFormula(engine, "CLAMP_SATURATION", 500, expectedResult: 500);

            // Teste 8: LIFE_STEAL_CONVERSION (input * 0.2)
            TestFormula(engine, "LIFE_STEAL_CONVERSION", 100, expectedResult: 20);

            // Teste 9: REFLECT_DAMAGE (input * 0.15)
            TestFormula(engine, "REFLECT_DAMAGE", 100, expectedResult: 15);

            // Teste 10: STEP_FUNCTION_MANA (input / 10 * 1)
            // Exemplo: 35 / 10 = 3.5, então 3.5 * 1 = 3.5
            TestFormula(engine, "STEP_FUNCTION_MANA", 35, expectedResult: 3.5f);

            // Teste 11: ARMOR_REDUCTION (1 / (input/100 + 1))
            // Exemplo: 50 / 100 = 0.5, 0.5 + 1 = 1.5, 1 / 1.5 = 0.6667
            TestFormula(engine, "ARMOR_REDUCTION", 50, expectedResult: 0.6667f, tolerance: 0.001f);

            // Teste 12: PERCENTAGE_BASED_DAMAGE ((input + 100) * 0.5)
            // Exemplo: (50 + 100) * 0.5 = 75
            TestFormula(engine, "PERCENTAGE_BASED_DAMAGE", 50, expectedResult: 75);

            // Teste 13: CRITICAL_MULTIPLIER (input * 2.0 + 50)
            // Exemplo: 100 * 2.0 + 50 = 250
            TestFormula(engine, "CRITICAL_MULTIPLIER", 100, expectedResult: 250);

            // Teste 14: COMPOUND_SCALING (input * 1.5 + 25)
            // Exemplo: 10 * 1.5 + 25 = 40
            TestFormula(engine, "COMPOUND_SCALING", 10, expectedResult: 40);

            // Teste 15: INVERSE_COMPOUND_SCALING (input + 25) * 1.5
            // Exemplo: (10 + 25) * 1.5 = 52.5
            TestFormula(engine, "INVERSE_COMPOUND_SCALING", 10, expectedResult: 52.5f);

            // Teste 16: SQUARE_ROOT_SCALING (sqrt(input) * 10)
            // Exemplo: sqrt(16) * 10 = 40
            TestFormula(engine, "SQUARE_ROOT_SCALING", 16, expectedResult: 40);

            // Teste 17: THRESHOLD_BONUS ((input - 100) clamped [0, 9999]) * 1.25 + 100
            // Exemplo com input=150: (150-100) = 50, clamped = 50, 50*1.25 = 62.5, 62.5+100 = 162.5
            TestFormula(engine, "THRESHOLD_BONUS", 150, expectedResult: 162.5f);
            // Exemplo com input=50: (50-100) = -50, clamped = 0, 0*1.25 = 0, 0+100 = 100
            TestFormula(engine, "THRESHOLD_BONUS", 50, expectedResult: 100);

            // Teste 18: Parâmetros customizados
            Console.WriteLine("\n=== TESTES COM PARÂMETROS CUSTOMIZADOS ===\n");
            var customParams = new Dictionary<string, float> { { "FLAT_BONUS", 20 } };
            TestFormula(engine, "LINEAR_ADDITIVE", 5, expectedResult: 25, paramOverrides: customParams);

            // Teste 19: Casos de erro
            Console.WriteLine("\n=== TESTES DE CASOS DE ERRO ===\n");
            TestErrorCase(engine, "FORMULA_INVALIDA", 10, "Fórmula inexistente");
            TestErrorCase(engine, "HYPERBOLIC_CURVE", 0, "Divisão por zero em DIVIDE_INVERSE", expectError: false); // Não deve dar erro

            Console.WriteLine("\n=== RESUMO DOS TESTES ===");
            Console.WriteLine($"Total de testes executados: {_totalTests}");
            Console.WriteLine($"Testes bem-sucedidos: {_passedTests}");
            Console.WriteLine($"Testes falhos: {_failedTests}");

            if (_failedTests == 0)
            {
                Console.WriteLine("\n✓ TODOS OS TESTES PASSARAM!");
            }
            else
            {
                Console.WriteLine($"\n✗ {_failedTests} TESTE(S) FALHARAM");
            }

            Console.WriteLine("\n[DEBUG]: Teste concluído. Pressione ENTER para fechar.");
            Console.ReadLine();
        }

        private static int _totalTests = 0;
        private static int _passedTests = 0;
        private static int _failedTests = 0;

        static void TestFormula(
            M.MathEngine engine,
            string formulaName,
            float input,
            float expectedResult,
            float tolerance = 0.01f,
            Dictionary<string, float>? paramOverrides = null)
        {
            _totalTests++;
            try
            {
                var expr = engine.BuildFromFormula(formulaName, input, paramOverrides);
                float result = expr.Build();

                bool passed = System.Math.Abs(result - expectedResult) <= tolerance;
                string status = passed ? "✓ PASS" : "✗ FAIL";

                if (passed)
                {
                    _passedTests++;
                    Console.WriteLine($"{status} | {formulaName}({input}) = {result:F4} (esperado: {expectedResult:F4})");
                }
                else
                {
                    _failedTests++;
                    Console.WriteLine($"{status} | {formulaName}({input}) = {result:F4} (esperado: {expectedResult:F4})");
                    Console.WriteLine($"       Diferença: {System.Math.Abs(result - expectedResult):F4}");
                }
            }
            catch (Exception ex)
            {
                _failedTests++;
                Console.WriteLine($"✗ ERROR | {formulaName}({input}) - {ex.Message}");
            }
        }

        static void TestErrorCase(
            M.MathEngine engine,
            string formulaName,
            float input,
            string description,
            bool expectError = true)
        {
            _totalTests++;
            try
            {
                var expr = engine.BuildFromFormula(formulaName, input);
                expr.Build();

                if (expectError)
                {
                    _failedTests++;
                    Console.WriteLine($"✗ FAIL | {description} - Deveria lançar exceção");
                }
                else
                {
                    _passedTests++;
                    Console.WriteLine($"✓ PASS | {description} - Executou sem erro");
                }
            }
            catch (Exception)
            {
                if (expectError)
                {
                    _passedTests++;
                    Console.WriteLine($"✓ PASS | {description} - Exceção lançada corretamente");
                }
                else
                {
                    _failedTests++;
                    Console.WriteLine($"✗ FAIL | {description} - Não deveria lançar exceção");
                }
            }
        }
    }
}
