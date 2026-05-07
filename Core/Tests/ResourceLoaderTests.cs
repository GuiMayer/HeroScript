using System;
using System.Collections.Generic;
using System.Text.Json;
using Core.Config;
using Core.Config.Delta;

namespace Core.Tests
{
    public static class ResourceLoaderTests
    {
        private static int _totalTests = 0;
        private static int _passedTests = 0;
        private static int _failedTests = 0;

        public static void RunAllTests()
        {
            Console.WriteLine("=== [ RESOURCE LOADER: TESTE DE DELTA OPERATIONS ] ===\n");

            // Testes de validação
            TestDeltaValidation();

            // Testes de merge
            TestDeltaMerger();

            // Resumo
            Console.WriteLine("\n=== RESUMO DOS TESTES ===");
            Console.WriteLine($"Total: {_totalTests}");
            Console.WriteLine($"Passou: {_passedTests}");
            Console.WriteLine($"Falhou: {_failedTests}");
            Console.WriteLine($"Taxa de sucesso: {(_totalTests > 0 ? (_passedTests * 100.0 / _totalTests) : 0):F1}%");
        }

        private static void TestDeltaValidation()
        {
            Console.WriteLine("=== TESTES DE VALIDAÇÃO ===\n");

            // Teste 1: REPLACE válido
            TestValidation(
                "REPLACE com data",
                new DeltaDefinition
                {
                    Operation = DeltaOperationType.REPLACE,
                    Data = new Dictionary<string, JsonElement>
                    {
                        ["test"] = JsonDocument.Parse("\"value\"").RootElement
                    }
                },
                shouldBeValid: true
            );

            // Teste 2: MERGE_DEEP válido
            TestValidation(
                "MERGE_DEEP com data",
                new DeltaDefinition
                {
                    Operation = DeltaOperationType.MERGE_DEEP,
                    Data = new Dictionary<string, JsonElement>
                    {
                        ["test"] = JsonDocument.Parse("\"value\"").RootElement
                    }
                },
                shouldBeValid: true
            );

            // Teste 3: ARRAY_APPEND válido
            TestValidation(
                "ARRAY_APPEND com $value array",
                new DeltaDefinition
                {
                    Operation = DeltaOperationType.ARRAY_APPEND,
                    Value = JsonDocument.Parse("[1, 2, 3]").RootElement
                },
                shouldBeValid: true
            );

            // Teste 4: ARRAY_APPEND inválido (sem $value)
            TestValidation(
                "ARRAY_APPEND sem $value",
                new DeltaDefinition
                {
                    Operation = DeltaOperationType.ARRAY_APPEND
                },
                shouldBeValid: false
            );

            // Teste 5: ARRAY_REMOVE_INDEX válido
            TestValidation(
                "ARRAY_REMOVE_INDEX com $index",
                new DeltaDefinition
                {
                    Operation = DeltaOperationType.ARRAY_REMOVE_INDEX,
                    Index = 0
                },
                shouldBeValid: true
            );

            // Teste 6: ARRAY_REMOVE_INDEX inválido (índice negativo)
            TestValidation(
                "ARRAY_REMOVE_INDEX com índice negativo",
                new DeltaDefinition
                {
                    Operation = DeltaOperationType.ARRAY_REMOVE_INDEX,
                    Index = -1
                },
                shouldBeValid: false
            );

            // Teste 7: FIELD_DELETE válido
            TestValidation(
                "FIELD_DELETE com $target",
                new DeltaDefinition
                {
                    Operation = DeltaOperationType.FIELD_DELETE,
                    TargetPath = "params.SCALING_VALUE"
                },
                shouldBeValid: true
            );

            // Teste 8: FIELD_DELETE inválido (sem $target)
            TestValidation(
                "FIELD_DELETE sem $target",
                new DeltaDefinition
                {
                    Operation = DeltaOperationType.FIELD_DELETE
                },
                shouldBeValid: false
            );

            // Teste 9: DELETE válido (não precisa de nada)
            TestValidation(
                "DELETE sem campos extras",
                new DeltaDefinition
                {
                    Operation = DeltaOperationType.DELETE
                },
                shouldBeValid: true
            );
        }

        private static void TestDeltaMerger()
        {
            Console.WriteLine("\n=== TESTES DE MERGE ===\n");

            // Teste 1: REPLACE
            TestMerge(
                "REPLACE substitui recurso inteiro",
                baseJson: "{\"a\": 1, \"b\": 2}",
                deltaOp: DeltaOperationType.REPLACE,
                deltaData: "{\"c\": 3}",
                expectedJson: "{\"c\": 3}"
            );

            // Teste 2: MERGE_SHALLOW
            TestMerge(
                "MERGE_SHALLOW mescla campos top-level",
                baseJson: "{\"a\": 1, \"b\": {\"x\": 10}}",
                deltaOp: DeltaOperationType.MERGE_SHALLOW,
                deltaData: "{\"b\": {\"y\": 20}, \"c\": 3}",
                expectedJson: "{\"a\": 1, \"b\": {\"y\": 20}, \"c\": 3}"
            );

            // Teste 3: MERGE_DEEP
            TestMerge(
                "MERGE_DEEP mescla recursivamente",
                baseJson: "{\"a\": 1, \"b\": {\"x\": 10, \"z\": 30}}",
                deltaOp: DeltaOperationType.MERGE_DEEP,
                deltaData: "{\"b\": {\"y\": 20}, \"c\": 3}",
                expectedJson: "{\"a\": 1, \"b\": {\"x\": 10, \"y\": 20, \"z\": 30}, \"c\": 3}"
            );

            // Teste 4: ARRAY_APPEND
            TestMerge(
                "ARRAY_APPEND adiciona ao final",
                baseJson: "[1, 2, 3]",
                deltaOp: DeltaOperationType.ARRAY_APPEND,
                deltaValue: "[4, 5]",
                expectedJson: "[1, 2, 3, 4, 5]"
            );

            // Teste 5: ARRAY_PREPEND
            TestMerge(
                "ARRAY_PREPEND adiciona ao início",
                baseJson: "[3, 4, 5]",
                deltaOp: DeltaOperationType.ARRAY_PREPEND,
                deltaValue: "[1, 2]",
                expectedJson: "[1, 2, 3, 4, 5]"
            );

            // Teste 6: ARRAY_REMOVE_INDEX
            TestMerge(
                "ARRAY_REMOVE_INDEX remove item",
                baseJson: "[\"a\", \"b\", \"c\"]",
                deltaOp: DeltaOperationType.ARRAY_REMOVE_INDEX,
                deltaIndex: 1,
                expectedJson: "[\"a\", \"c\"]"
            );

            // Teste 7: ARRAY_REPLACE_INDEX
            TestMerge(
                "ARRAY_REPLACE_INDEX substitui item",
                baseJson: "[10, 20, 30]",
                deltaOp: DeltaOperationType.ARRAY_REPLACE_INDEX,
                deltaIndex: 1,
                deltaValue: "99",
                expectedJson: "[10, 99, 30]"
            );

            // Teste 8: FIELD_DELETE (campo top-level)
            TestMerge(
                "FIELD_DELETE remove campo top-level",
                baseJson: "{\"a\": 1, \"b\": 2, \"c\": 3}",
                deltaOp: DeltaOperationType.FIELD_DELETE,
                deltaTarget: "b",
                expectedJson: "{\"a\": 1, \"c\": 3}"
            );

            // Teste 9: FIELD_DELETE (campo nested)
            TestMerge(
                "FIELD_DELETE remove campo nested",
                baseJson: "{\"a\": 1, \"params\": {\"x\": 10, \"y\": 20}}",
                deltaOp: DeltaOperationType.FIELD_DELETE,
                deltaTarget: "params.x",
                expectedJson: "{\"a\": 1, \"params\": {\"y\": 20}}"
            );

            // Teste 10: DELETE
            TestMerge(
                "DELETE remove recurso",
                baseJson: "{\"a\": 1, \"b\": 2}",
                deltaOp: DeltaOperationType.DELETE,
                expectedJson: null
            );
        }

        private static void TestValidation(string testName, DeltaDefinition delta, bool shouldBeValid)
        {
            _totalTests++;
            Console.Write($"[{_totalTests}] {testName}... ");

            try
            {
                var result = DeltaValidator.Validate("TEST_RESOURCE", delta, strictMode: false);

                if (result.IsValid == shouldBeValid)
                {
                    Console.WriteLine("✓ PASSOU");
                    _passedTests++;
                }
                else
                {
                    Console.WriteLine($"✗ FALHOU");
                    Console.WriteLine($"    Esperado: {(shouldBeValid ? "válido" : "inválido")}");
                    Console.WriteLine($"    Obtido: {(result.IsValid ? "válido" : "inválido")}");
                    if (result.Errors.Count > 0)
                        Console.WriteLine($"    Erros: {string.Join(", ", result.Errors)}");
                    _failedTests++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ EXCEÇÃO: {ex.Message}");
                _failedTests++;
            }
        }

        private static void TestMerge(
            string testName,
            string? baseJson,
            DeltaOperationType deltaOp,
            string? deltaData = null,
            string? deltaValue = null,
            int? deltaIndex = null,
            string? deltaTarget = null,
            string? expectedJson = null)
        {
            _totalTests++;
            Console.Write($"[{_totalTests}] {testName}... ");

            try
            {
                // Parse base
                JsonElement? baseElement = baseJson != null
                    ? JsonDocument.Parse(baseJson).RootElement
                    : null;

                // Construir delta
                var delta = new DeltaDefinition
                {
                    Operation = deltaOp,
                    TargetPath = deltaTarget,
                    Index = deltaIndex
                };

                if (deltaData != null)
                {
                    var dataDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(deltaData);
                    delta.Data = dataDict;
                }

                if (deltaValue != null)
                {
                    delta.Value = JsonDocument.Parse(deltaValue).RootElement;
                }

                // Aplicar delta
                var result = DeltaMerger.ApplyDelta(baseElement, delta, "TEST_RESOURCE", strictMode: false);

                // Verificar resultado
                if (expectedJson == null)
                {
                    // Esperamos null (DELETE)
                    if (!result.HasValue)
                    {
                        Console.WriteLine("✓ PASSOU");
                        _passedTests++;
                    }
                    else
                    {
                        Console.WriteLine($"✗ FALHOU");
                        Console.WriteLine($"    Esperado: null");
                        Console.WriteLine($"    Obtido: {result.Value.GetRawText()}");
                        _failedTests++;
                    }
                }
                else
                {
                    // Comparar JSON
                    var expected = JsonDocument.Parse(expectedJson).RootElement;
                    var actual = result!.Value;

                    if (JsonEquals(expected, actual))
                    {
                        Console.WriteLine("✓ PASSOU");
                        _passedTests++;
                    }
                    else
                    {
                        Console.WriteLine($"✗ FALHOU");
                        Console.WriteLine($"    Esperado: {expected.GetRawText()}");
                        Console.WriteLine($"    Obtido: {actual.GetRawText()}");
                        _failedTests++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ EXCEÇÃO: {ex.Message}");
                _failedTests++;
            }
        }

        private static bool JsonEquals(JsonElement a, JsonElement b)
        {
            // Comparação simples via serialização
            // (não é perfeita para todos os casos, mas funciona para testes)
            var aJson = JsonSerializer.Serialize(a, new JsonSerializerOptions { WriteIndented = false });
            var bJson = JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = false });
            return aJson == bJson;
        }
    }
}
