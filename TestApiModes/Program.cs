using Core.Math;
using Core.Tests;

Console.WriteLine("=== Testing Math Expression API - Three Modes ===\n");

try
{
    MathExpressionApiTests.RunAllTests();
    Console.WriteLine("\n✓ All tests passed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"\n✗ Test failed: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    return 1;
}

return 0;
