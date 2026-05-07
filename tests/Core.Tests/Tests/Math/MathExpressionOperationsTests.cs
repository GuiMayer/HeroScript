using M = Core.Math;
using System;

namespace Core.Tests.Math
{
    public static class MathExpressionOperationsTests
    {
        private static int _totalTests = 0;
        private static int _passedTests = 0;
        private static int _failedTests = 0;

        public static void RunAllTests()
        {
            Console.WriteLine("=== [ MATH EXPRESSION: TESTE DE OPERAÇÕES BÁSICAS ] ===\n");

            _totalTests = 0;
            _passedTests = 0;
            _failedTests = 0;

            // Testes MIN
            TestMinOperation();

            // Resumo final
            Console.WriteLine("\n=== RESUMO DOS TESTES ===");
            Console.WriteLine($"Total: {_totalTests}");
            Console.WriteLine($"Passou: {_passedTests}");
            Console.WriteLine($"Falhou: {_failedTests}");
            Console.WriteLine($"Taxa de sucesso: {(_totalTests > 0 ? (_passedTests * 100.0 / _totalTests) : 0):F1}%\n");

            if (_failedTests > 0)
            {
                throw new Exception($"{_failedTests} teste(s) falharam!");
            }
        }

        private static void TestMinOperation()
        {
            Console.WriteLine("=== TESTES: MIN ===\n");

            // Teste 1: MIN com valor menor
            TestOperation(
                "MIN: 10 min 5 = 5",
                () => new M.MathExpression(10).Min(5).Build(),
                expectedResult: 5
            );

            // Teste 2: MIN com valor maior
            TestOperation(
                "MIN: 10 min 20 = 10",
                () => new M.MathExpression(10).Min(20).Build(),
                expectedResult: 10
            );

            // Teste 3: MIN com valores iguais
            TestOperation(
                "MIN: 10 min 10 = 10",
                () => new M.MathExpression(10).Min(10).Build(),
                expectedResult: 10
            );

            // Teste 4: MIN com valores negativos
            TestOperation(
                "MIN: -5 min -10 = -10",
                () => new M.MathExpression(-5).Min(-10).Build(),
                expectedResult: -10
            );

            // Teste 5: MIN com múltiplos valores
            TestOperation(
                "MIN: 100 min 50, 75, 25 = 25",
                () => new M.MathExpression(100).Min(50, 75, 25).Build(),
                expectedResult: 25
            );

            // Teste 6: MIN com zero
            TestOperation(
                "MIN: 10 min 0 = 0",
                () => new M.MathExpression(10).Min(0).Build(),
                expectedResult: 0
            );

            // Teste 7: MIN encadeado
            TestOperation(
                "MIN: 100 min 50 min 75 = 50",
                () => new M.MathExpression(100).Min(50).Min(75).Build(),
                expectedResult: 50
            );

            // Teste 8: MIN sem valores (deve lançar exceção)
            TestOperationException(
                "MIN: sem valores deve lançar exceção",
                () => new M.MathExpression(10).AddRawStep("MIN", Array.Empty<float>()).Build(),
                expectedExceptionType: typeof(InvalidOperationException)
            );

            Console.WriteLine();
        }

        private static void TestOperation(string testName, Func<float> operation, float expectedResult, float tolerance = 0.0001f)
        {
            _totalTests++;
            try
            {
                float result = operation();
                if (System.Math.Abs(result - expectedResult) <= tolerance)
                {
                    Console.WriteLine($"✓ {testName}");
                    Console.WriteLine($"  Resultado: {result}");
                    _passedTests++;
                }
                else
                {
                    Console.WriteLine($"✗ {testName}");
                    Console.WriteLine($"  Esperado: {expectedResult}, Obtido: {result}");
                    _failedTests++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ {testName}");
                Console.WriteLine($"  Exceção inesperada: {ex.Message}");
                _failedTests++;
            }
        }

        private static void TestOperationException(string testName, Func<float> operation, Type expectedExceptionType)
        {
            _totalTests++;
            try
            {
                float result = operation();
                Console.WriteLine($"✗ {testName}");
                Console.WriteLine($"  Esperava exceção {expectedExceptionType.Name}, mas obteve resultado: {result}");
                _failedTests++;
            }
            catch (Exception ex)
            {
                if (ex.GetType() == expectedExceptionType)
                {
                    Console.WriteLine($"✓ {testName}");
                    Console.WriteLine($"  Exceção esperada: {ex.Message}");
                    _passedTests++;
                }
                else
                {
                    Console.WriteLine($"✗ {testName}");
                    Console.WriteLine($"  Esperava {expectedExceptionType.Name}, mas obteve {ex.GetType().Name}: {ex.Message}");
                    _failedTests++;
                }
            }
        }
    }
}
