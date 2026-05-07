using Core.Math;

Console.WriteLine("=== Testando todas as operações matemáticas ===\n");

// Teste ABS
Console.WriteLine("1. Teste ABS (valor absoluto):");
var absResult = new MathExpression(-15.5f).Abs().Build();
Console.WriteLine($"   ABS(-15.5) = {absResult}");
Console.WriteLine($"   Esperado: 15.5, Resultado: {(absResult == 15.5f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste SQRT
Console.WriteLine("2. Teste SQRT (raiz quadrada):");
var sqrtResult = new MathExpression(16f).Sqrt().Build();
Console.WriteLine($"   SQRT(16) = {sqrtResult}");
Console.WriteLine($"   Esperado: 4, Resultado: {(sqrtResult == 4f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste POW
Console.WriteLine("3. Teste POW (potência):");
var powResult = new MathExpression(2f).Pow(3f).Build();
Console.WriteLine($"   POW(2, 3) = {powResult}");
Console.WriteLine($"   Esperado: 8, Resultado: {(powResult == 8f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste ROUND
Console.WriteLine("4. Teste ROUND (arredondamento):");
var roundResult = new MathExpression(3.14159f).Round(2).Build();
Console.WriteLine($"   ROUND(3.14159, 2) = {roundResult}");
Console.WriteLine($"   Esperado: 3.14, Resultado: {(Math.Abs(roundResult - 3.14f) < 0.01f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste FLOOR
Console.WriteLine("5. Teste FLOOR (arredonda para baixo):");
var floorResult = new MathExpression(3.7f).Floor().Build();
Console.WriteLine($"   FLOOR(3.7) = {floorResult}");
Console.WriteLine($"   Esperado: 3, Resultado: {(floorResult == 3f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste CEIL
Console.WriteLine("6. Teste CEIL (arredonda para cima):");
var ceilResult = new MathExpression(3.2f).Ceil().Build();
Console.WriteLine($"   CEIL(3.2) = {ceilResult}");
Console.WriteLine($"   Esperado: 4, Resultado: {(ceilResult == 4f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste MIN
Console.WriteLine("7. Teste MIN (mínimo):");
var minResult = new MathExpression(10f).Min(5f, 15f, 3f).Build();
Console.WriteLine($"   MIN(10, 5, 15, 3) = {minResult}");
Console.WriteLine($"   Esperado: 3, Resultado: {(minResult == 3f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste MAX
Console.WriteLine("8. Teste MAX (máximo):");
var maxResult = new MathExpression(10f).Max(5f, 15f, 3f).Build();
Console.WriteLine($"   MAX(10, 5, 15, 3) = {maxResult}");
Console.WriteLine($"   Esperado: 15, Resultado: {(maxResult == 15f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste combinado complexo
Console.WriteLine("9. Teste COMBINADO:");
var complexResult = new MathExpression(10f)
    .Add(5f)           // 15
    .Multiply(2f)      // 30
    .Sqrt()            // 5.477...
    .Round(2)          // 5.48
    .Build();
Console.WriteLine($"   (10 + 5) * 2 -> SQRT -> ROUND(2) = {complexResult}");
Console.WriteLine($"   Esperado: ~5.48, Resultado: {(Math.Abs(complexResult - 5.48f) < 0.01f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste com valores negativos e ABS
Console.WriteLine("10. Teste NEGATIVO + ABS:");
var negAbsResult = new MathExpression(5f)
    .Subtract(10f)     // -5
    .Abs()             // 5
    .Pow(2f)           // 25
    .Build();
Console.WriteLine($"   (5 - 10) -> ABS -> POW(2) = {negAbsResult}");
Console.WriteLine($"   Esperado: 25, Resultado: {(negAbsResult == 25f ? "✓ PASSOU" : "✗ FALHOU")}\n");

Console.WriteLine("=== Todos os testes concluídos ===");
