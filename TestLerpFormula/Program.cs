using Core.Math;
using Core.Config;

Console.WriteLine("=== Testando LERP via MathEngine ===\n");

// Invalidar cache do ResourceLoader para forçar reload do arquivo JSON
ResourceLoader.Instance.InvalidateCache();
Console.WriteLine("Cache do ResourceLoader invalidado\n");

var engine = new MathEngine();

// Debug: Listar todas as fórmulas carregadas
Console.WriteLine("Fórmulas carregadas:");
var formulas = engine.GetType().GetField("_formulas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(engine) as System.Collections.IDictionary;
if (formulas != null)
{
    foreach (var key in formulas.Keys)
    {
        Console.WriteLine($"  - {key}");
    }
    Console.WriteLine($"Total: {formulas.Count} fórmulas\n");
}
else
{
    Console.WriteLine("  Não foi possível acessar as fórmulas carregadas\n");
}

// Teste 1: LERP básico (0 -> 10, t=0.5)
var lerp1 = engine.BuildFromFormula("LERP", 0f, new Dictionary<string, float> 
{ 
    { "START", 0f },
    { "TARGET", 10f }, 
    { "T", 0.5f } 
}).Build();
Console.WriteLine($"1. LERP(0, 10, 0.5) = {lerp1}");
Console.WriteLine($"   Esperado: 5, Resultado: {(Math.Abs(lerp1 - 5f) < 0.0001f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste 2: LERP com t=0 (deve retornar valor inicial)
var lerp2 = engine.BuildFromFormula("LERP", 0f, new Dictionary<string, float> 
{ 
    { "START", 0f },
    { "TARGET", 10f }, 
    { "T", 0f } 
}).Build();
Console.WriteLine($"2. LERP(0, 10, 0) = {lerp2}");
Console.WriteLine($"   Esperado: 0, Resultado: {(Math.Abs(lerp2 - 0f) < 0.0001f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste 3: LERP com t=1 (deve retornar target)
var lerp3 = engine.BuildFromFormula("LERP", 0f, new Dictionary<string, float> 
{ 
    { "START", 0f },
    { "TARGET", 10f }, 
    { "T", 1f } 
}).Build();
Console.WriteLine($"3. LERP(0, 10, 1) = {lerp3}");
Console.WriteLine($"   Esperado: 10, Resultado: {(Math.Abs(lerp3 - 10f) < 0.0001f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste 4: LERP com extrapolação (t > 1)
var lerp4 = engine.BuildFromFormula("LERP", 0f, new Dictionary<string, float> 
{ 
    { "START", 0f },
    { "TARGET", 10f }, 
    { "T", 1.5f } 
}).Build();
Console.WriteLine($"4. LERP(0, 10, 1.5) = {lerp4}");
Console.WriteLine($"   Esperado: 15, Resultado: {(Math.Abs(lerp4 - 15f) < 0.0001f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste 5: LERP com valores negativos
var lerp5 = engine.BuildFromFormula("LERP", 0f, new Dictionary<string, float> 
{ 
    { "START", -10f },
    { "TARGET", 10f }, 
    { "T", 0.5f } 
}).Build();
Console.WriteLine($"5. LERP(-10, 10, 0.5) = {lerp5}");
Console.WriteLine($"   Esperado: 0, Resultado: {(Math.Abs(lerp5 - 0f) < 0.0001f ? "✓ PASSOU" : "✗ FALHOU")}\n");

// Teste 6: LERP com valores decimais
var lerp6 = engine.BuildFromFormula("LERP", 0f, new Dictionary<string, float> 
{ 
    { "START", 2.5f },
    { "TARGET", 7.5f }, 
    { "T", 0.25f } 
}).Build();
Console.WriteLine($"6. LERP(2.5, 7.5, 0.25) = {lerp6}");
Console.WriteLine($"   Esperado: 3.75, Resultado: {(Math.Abs(lerp6 - 3.75f) < 0.0001f ? "✓ PASSOU" : "✗ FALHOU")}\n");

Console.WriteLine("=== Todos os testes LERP concluídos ===");
